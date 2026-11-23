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
internal sealed record VaultListing(string Name, string Account);
internal sealed record VaultPending(bool Codes, VaultListing Listing, byte[] WrappedKey, byte[] Ciphertext);
internal sealed record VaultInbox(byte[] PublicKey, byte[] PrivateKey, VaultListing[] Passwords, VaultListing[] Codes, VaultPending[] Pending);

internal sealed class VaultStore(string path)
{
    internal sealed record Document(byte[] Salt, byte[] PinCheck, byte[] Passwords, byte[] Codes, bool PinPasswords, bool PinCodes,
        int Failures = 0, DateTimeOffset RetryAt = default, VaultHelloCredential? Hello = null, Guid Revision = default, VaultInbox? Inbox = null)
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
            Write(new(salt, Encode(true, key), Encode(passwords, pinPasswords ? key : null), Encode(codes, pinCodes ? key : null), pinPasswords, pinCodes,
                Inbox: CreateInbox(key, passwords, codes)));
        }
        finally { if (newPin is not null) CryptographicOperations.ZeroMemory(key); }
    }

    internal void ConfigureHello(Document current, byte[] oldKey, VaultHelloCredential credential, byte[] helloKey)
    {
        RequireCurrent(current);
        if (current.Hello is not null || !Decode<bool>(current.PinCheck, oldKey)) throw new CryptographicException();
        var passwords = Entries(current, false, oldKey);
        var codes = Entries(current, true, oldKey);
        Write(new([], [], Seal(passwords, helloKey, false), Seal(codes, helloKey, true),
            current.PinPasswords, current.PinCodes, Hello: credential, Inbox: CreateInbox(helloKey, passwords, codes)));
    }

    internal List<VaultEntry> Entries(Document document, bool codes, byte[]? key)
    {
        byte[] payload = codes ? document.Codes : document.Passwords;
        byte[]? secret = Key(document, codes, key);
        var entries = document.Hello is not null ? Unseal(payload, secret!, codes) : Decode<List<VaultEntry>>(payload, secret);
        if (document.Inbox is not { } inbox || !inbox.Pending.Any(item => item.Codes == codes)) return entries;
        byte[] privateKey = Decode<byte[]>(inbox.PrivateKey, key ?? throw new CryptographicException());
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportPkcs8PrivateKey(privateKey, out _);
            foreach (var item in inbox.Pending.Where(item => item.Codes == codes))
            {
                byte[] contentKey = rsa.Decrypt(item.WrappedKey, RSAEncryptionPadding.OaepSHA256);
                try { entries.AddRange(Unseal(item.Ciphertext, contentKey, codes)); }
                finally { CryptographicOperations.ZeroMemory(contentKey); }
            }
        }
        finally { CryptographicOperations.ZeroMemory(privateKey); }
        return entries;
    }

    internal void PrepareListings(Document document, byte[] key)
    {
        if (document.Inbox is not null) return;
        RequireCurrent(document);
        Write(document with { Inbox = CreateInbox(key, Entries(document, false, key), Entries(document, true, key)) });
    }

    internal List<VaultEntry> Listings(Document document, bool codes)
    {
        if (document.Inbox is not { } inbox) return [];
        return (codes ? inbox.Codes : inbox.Passwords).Concat(inbox.Pending.Where(item => item.Codes == codes).Select(item => item.Listing))
            .Select(item => new VaultEntry(item.Name, item.Account, "", null)).ToList();
    }

    internal void AddLocked(Document document, bool codes, VaultEntry entry)
    {
        RequireCurrent(document);
        var inbox = document.Inbox ?? throw new IOException("Unlock Vault once to enable locked additions.");
        if (!document.Protected(codes)) throw new InvalidOperationException();
        using var rsa = RSA.Create();
        rsa.ImportSubjectPublicKeyInfo(inbox.PublicKey, out _);
        byte[] key = RandomNumberGenerator.GetBytes(32);
        try
        {
            var pending = new VaultPending(codes, new(entry.Name, entry.Account), rsa.Encrypt(key, RSAEncryptionPadding.OaepSHA256), Seal([entry], key, codes));
            Write(document with { Inbox = inbox with { Pending = [.. inbox.Pending, pending] } });
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    private static VaultListing[] Index(List<VaultEntry> entries) => entries.Select(item => new VaultListing(item.Name, item.Account)).ToArray();

    private static VaultInbox CreateInbox(byte[] key, List<VaultEntry> passwords, List<VaultEntry> codes)
    {
        using var rsa = RSA.Create(2048);
        byte[] privateKey = rsa.ExportPkcs8PrivateKey();
        try { return new(rsa.ExportSubjectPublicKeyInfo(), Encode(privateKey, key), Index(passwords), Index(codes), []); }
        finally { CryptographicOperations.ZeroMemory(privateKey); }
    }

    internal void Save(Document document, bool codes, byte[]? key, List<VaultEntry> entries)
    {
        RequireCurrent(document);
        byte[]? secret = Key(document, codes, key);
        byte[] encrypted = document.Hello is not null ? Seal(entries, secret!, codes) : Encode(entries, secret);
        var inbox = document.Inbox;
        if (inbox is not null)
        {
            inbox = codes ? inbox with { Codes = Index(entries) } : inbox with { Passwords = Index(entries) };
            inbox = inbox with { Pending = inbox.Pending.Where(item => item.Codes != codes).ToArray() };
        }
        Write(codes ? document with { Codes = encrypted, Inbox = inbox } : document with { Passwords = encrypted, Inbox = inbox });
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
