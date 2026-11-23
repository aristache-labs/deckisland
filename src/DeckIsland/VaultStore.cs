using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using DeckIsland.Core;

namespace DeckIsland;

internal sealed record VaultEntry(string Name, string Account, string Password, VaultOtp? Otp)
{
    public override string ToString() => nameof(VaultEntry);
}

internal sealed record VaultHelloCredential(string Name, byte[] Challenge, byte[] PublicKey);

internal sealed class VaultStore(string path)
{
    internal sealed record Document(byte[] Salt, byte[] PinCheck, byte[] Passwords, byte[] Codes, bool PinPasswords, bool PinCodes,
        int Failures = 0, DateTimeOffset RetryAt = default, VaultHelloCredential? Hello = null, Guid Revision = default)
    {
        internal bool Protected(bool codes) => Hello is not null || (codes ? PinCodes : PinPasswords);
    }

    private const DataProtectionScope Scope = DataProtectionScope.CurrentUser;

    internal Document? Read()
    {
        byte[] encrypted;
        try { encrypted = File.ReadAllBytes(path); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        return Decode<Document>(encrypted, null);
    }

    internal byte[]? Unlock(Document document, string pin, DateTimeOffset now)
    {
        if (document.Hello is not null || now < document.RetryAt || !VaultRules.ValidPin(pin)) return null;
        RequireCurrent(document);
        var key = Derive(pin, document.Salt);
        bool valid = false;
        try { valid = Decode<bool>(document.PinCheck, key); }
        catch (CryptographicException) { }
        try
        {
            if (valid)
            {
                if (document.Failures != 0) Write(document with { Failures = 0, RetryAt = default });
                return key;
            }
            int failures = Math.Min(document.Failures + 1, 25);
            Write(document with { Failures = failures, RetryAt = now + VaultRules.Delay(failures) });
        }
        catch
        {
            CryptographicOperations.ZeroMemory(key);
            throw;
        }
        CryptographicOperations.ZeroMemory(key);
        return null;
    }

    internal void Configure(Document? current, byte[]? oldKey, string? newPin, bool pinPasswords, bool pinCodes)
    {
        RequireCurrent(current);
        if (current?.Hello is not null && newPin is null) throw new InvalidDataException();
        var salt = newPin is null ? current!.Salt : RandomNumberGenerator.GetBytes(32);
        var key = newPin is null ? oldKey! : Derive(newPin, salt);
        try
        {
            var passwords = current is null ? [] : Entries(current, false, oldKey);
            var codes = current is null ? [] : Entries(current, true, oldKey);
            Write(new(salt, Encode(true, key), Encode(passwords, pinPasswords ? key : null), Encode(codes, pinCodes ? key : null), pinPasswords, pinCodes));
        }
        finally { if (newPin is not null) CryptographicOperations.ZeroMemory(key); }
    }

    internal void ConfigureHello(Document current, byte[] oldKey, VaultHelloCredential credential, byte[] helloKey)
    {
        RequireCurrent(current);
        if (current.Hello is not null || !Decode<bool>(current.PinCheck, oldKey)) throw new CryptographicException();
        Write(new([], [], Seal(Entries(current, false, oldKey), helloKey, false), Seal(Entries(current, true, oldKey), helloKey, true),
            current.PinPasswords, current.PinCodes, Hello: credential));
    }

    internal List<VaultEntry> Entries(Document document, bool codes, byte[]? key)
    {
        byte[] payload = codes ? document.Codes : document.Passwords;
        byte[]? secret = Key(document, codes, key);
        return document.Hello is not null ? Unseal(payload, secret!, codes) : Decode<List<VaultEntry>>(payload, secret);
    }

    internal void Save(Document document, bool codes, byte[]? key, List<VaultEntry> entries)
    {
        RequireCurrent(document);
        byte[]? secret = Key(document, codes, key);
        byte[] encrypted = document.Hello is not null ? Seal(entries, secret!, codes) : Encode(entries, secret);
        Write(codes ? document with { Codes = encrypted } : document with { Passwords = encrypted });
    }

    internal void Reset()
    {
        if (!Directory.Exists(Path.GetDirectoryName(path))) return;
        File.Delete(path + ".tmp");
        File.Delete(path);
    }

    private static byte[]? Key(Document doc, bool codes, byte[]? key) =>
        doc.Protected(codes) ? key ?? throw new InvalidOperationException() : null;

    private void RequireCurrent(Document? expected)
    {
        if (Read()?.Revision != expected?.Revision)
            throw new IOException("Vault has changed.");
    }

    private static ReadOnlySpan<byte> Section(bool codes) => codes ? "DeckIsland Vault 2FA v1"u8 : "DeckIsland Vault passwords v1"u8;

    private static byte[] Seal(List<VaultEntry> entries, byte[] key, bool codes)
    {
        byte[] plain = JsonSerializer.SerializeToUtf8Bytes(entries);
        try
        {
            byte[] payload = new byte[12 + 16 + plain.Length];
            RandomNumberGenerator.Fill(payload.AsSpan(0, 12));
            using var gcm = new AesGcm(key, 16);
            gcm.Encrypt(payload.AsSpan(0, 12), plain, payload.AsSpan(28), payload.AsSpan(12, 16), Section(codes));
            return payload;
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    private static List<VaultEntry> Unseal(byte[] payload, byte[] key, bool codes)
    {
        if (payload.Length < 28) throw new InvalidDataException();
        byte[] plain = new byte[payload.Length - 28];
        try
        {
            using var gcm = new AesGcm(key, 16);
            gcm.Decrypt(payload.AsSpan(0, 12), payload.AsSpan(28), payload.AsSpan(12, 16), plain, Section(codes));
            return JsonSerializer.Deserialize<List<VaultEntry>>(plain) ?? throw new InvalidDataException();
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    private static byte[] Derive(string pin, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(pin, salt, 600_000, HashAlgorithmName.SHA256, 32);

    private void Write(Document document)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(Encode(document with { Revision = Guid.NewGuid() }, null));
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, path, overwrite: true);
        }
        finally { File.Delete(temp); }
    }

    private static byte[] Encode<T>(T value, byte[]? entropy)
    {
        byte[] plain = JsonSerializer.SerializeToUtf8Bytes(value);
        try { return ProtectedData.Protect(plain, entropy, Scope); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    private static T Decode<T>(byte[] encrypted, byte[]? entropy)
    {
        byte[] plain = ProtectedData.Unprotect(encrypted, entropy, Scope);
        try { return JsonSerializer.Deserialize<T>(plain) ?? throw new InvalidDataException(); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
}
