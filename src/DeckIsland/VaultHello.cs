using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Security.Cryptography;
using DeckIsland.Core;
using Windows.Security.Credentials;

namespace DeckIsland;

internal static class VaultHello
{
    private const string Prefix = "DeckIsland.Vault.";

    internal static string Unavailable => Loc.T(
        "无法使用经过 TPM 证明的 Windows Hello 密钥。", "A TPM-attested Windows Hello key is unavailable on this device.");

    internal static bool PlatformError(Exception error) => error is COMException or UnauthorizedAccessException
        or NotSupportedException or InvalidOperationException or CryptographicException;

    internal static async Task<(VaultHelloCredential Credential, byte[] Key)?> Enroll()
    {
        if (!await KeyCredentialManager.IsSupportedAsync()) throw new NotSupportedException(Unavailable);
        string name = Prefix + Guid.NewGuid().ToString("N");
        var result = await KeyCredentialManager.RequestCreateAsync(name, KeyCredentialCreationOption.FailIfExists);
        if (Canceled(result.Status)) return null;
        if (result.Status != KeyCredentialStatus.Success) throw new NotSupportedException(Unavailable);

        bool keep = false;
        try
        {
            var credential = result.Credential;
            // Windows can provision a software credential. Require local TPM attestation for this exact key.
            var attestation = await credential.GetAttestationAsync();
            if (attestation.Status != KeyCredentialAttestationStatus.Success || attestation.AttestationBuffer is not { Length: > 0 })
                throw new NotSupportedException(Unavailable);
            var reference = new VaultHelloCredential(name, RandomNumberGenerator.GetBytes(32), credential.RetrievePublicKey().ToArray());
            var key = await Derive(credential, reference);
            if (key is null) return null;
            keep = true;
            return (reference, key);
        }
        finally { if (!keep) await Delete(name); }
    }

    internal static async Task<byte[]?> Unlock(VaultHelloCredential reference)
    {
        if (!OwnedName(reference.Name)) throw new CryptographicException();
        var result = await KeyCredentialManager.OpenAsync(reference.Name);
        if (Canceled(result.Status)) return null;
        if (result.Status != KeyCredentialStatus.Success) throw new NotSupportedException(Unavailable);
        return await Derive(result.Credential, reference);
    }

    private static bool Canceled(KeyCredentialStatus status) =>
        status is KeyCredentialStatus.UserCanceled or KeyCredentialStatus.UserPrefersPassword;

    private static bool OwnedName(string name) =>
        name.StartsWith(Prefix, StringComparison.Ordinal) && Guid.TryParseExact(name.AsSpan(Prefix.Length), "N", out _);

    internal static async Task Delete(string name)
    {
        if (!OwnedName(name)) return;
        try { await KeyCredentialManager.DeleteAsync(name); }
        catch (Exception error) when (PlatformError(error)) { }
    }

    private static async Task<byte[]?> Derive(KeyCredential credential, VaultHelloCredential reference)
    {
        byte[] challenge = [.."DeckIsland Vault local key derivation v1"u8, ..reference.Challenge];
        var operation = await credential.RequestSignAsync(challenge.AsBuffer());
        if (Canceled(operation.Status)) return null;
        if (operation.Status != KeyCredentialStatus.Success) throw new NotSupportedException(Unavailable);

        // This dedicated credential signs only the vault challenge. The deterministic signature is secret key material.
        byte[] signature = operation.Result.ToArray();
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportSubjectPublicKeyInfo(reference.PublicKey, out int read);
            if (read != reference.PublicKey.Length || !rsa.VerifyData(challenge, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                throw new CryptographicException();
            return HKDF.DeriveKey(HashAlgorithmName.SHA256, signature, 32, reference.Challenge, "DeckIsland Vault AES-GCM v1"u8.ToArray());
        }
        finally
        {
            CryptographicOperations.ZeroMemory(signature);
        }
    }
}
