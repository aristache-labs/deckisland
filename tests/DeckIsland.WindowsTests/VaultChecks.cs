using System.IO;
using System.Security.Cryptography;
using DeckIsland;

internal static class VaultChecks
{
    internal static void Run(Action<string, bool> check)
    {
        string path = Path.Combine(Path.GetTempPath(), $"deckisland-vault-test-{Guid.NewGuid():N}.dat");
        var vault = new VaultStore(path);
        var now = DateTimeOffset.UtcNow;
        var session = new DeckIsland.Core.VaultSession();
        var sessionKey = RandomNumberGenerator.GetBytes(32);
        session.Open(sessionKey, now);
        check("Vault timeout is measured from unlock without extending on access",
            session.Remaining(15, now.AddMinutes(10)) == TimeSpan.FromMinutes(5) &&
            session.Remaining(15, now.AddMinutes(15)) == TimeSpan.Zero);
        check("Until exit disables the deadline and session lock clears key material",
            session.Remaining(0, now.AddHours(10)) is null && session.Key == sessionKey);
        session.Lock();
        check("Ending the Vault session erases the in-memory key", session.Key is null && sessionKey.All(value => value == 0));
        try
        {
            vault.Configure(null, null, "1234", pinPasswords: true, pinCodes: false);
            var key = vault.Unlock(vault.Read()!, "1234", now)!;
            try { vault.Save(vault.Read()!, false, key, [new("Sample", "test", "sentinel-password", null)]); }
            finally { CryptographicOperations.ZeroMemory(key); }
            var doc = vault.Read()!;
            check("Vault encrypts the file and protects each section independently",
                File.ReadAllBytes(path).AsSpan().IndexOf("sentinel-password"u8) < 0 &&
                doc.Protected(false) && !doc.Protected(true) && vault.Entries(doc, true, null).Count == 0);
            for (int i = 0; i < 5; i++)
                check("Wrong PIN cannot unlock Vault", vault.Unlock(vault.Read()!, "0000", now) is null);
            vault = new VaultStore(path);
            check("Vault lockout survives reopening", vault.Read()!.RetryAt == now.AddSeconds(30) &&
                vault.Unlock(vault.Read()!, "1234", now) is null);
            check("A wrong attempt after the first lockout doubles the wait",
                vault.Unlock(vault.Read()!, "0000", now.AddSeconds(30)) is null &&
                vault.Read()!.RetryAt == now.AddSeconds(90));
            key = vault.Unlock(vault.Read()!, "1234", now.AddSeconds(90))!;
            try { vault.Configure(vault.Read(), key, "9876", pinPasswords: true, pinCodes: true); }
            finally { CryptographicOperations.ZeroMemory(key); }
            key = vault.Unlock(vault.Read()!, "9876", now.AddSeconds(91))!;
            var previous = vault.Read()!;
            var helloKey = RandomNumberGenerator.GetBytes(32);
            var reference = new VaultHelloCredential("test credential", RandomNumberGenerator.GetBytes(32), []);
            try
            {
                check("Changing PIN preserves entries", vault.Entries(previous, false, key)[0].Password == "sentinel-password");
                vault.ConfigureHello(previous, key, reference, helloKey);
                var enrolled = vault.Read()!;
                vault.AddLocked(enrolled, false, new("Added while locked", "second-account", "sealed-secret", null));
                enrolled = vault.Read()!;
                check("A locked vault lists metadata and accepts a sealed addition",
                    vault.Listings(enrolled, false)[1] is { Account: "second-account", Password.Length: 0 } &&
                    Throws<InvalidOperationException>(() => vault.Entries(enrolled, false, null)) &&
                    vault.Entries(enrolled, false, helloKey)[1].Password == "sealed-secret");
                check("Hello protects both sections and removes the previous PIN verifier",
                    enrolled.Hello?.Name == reference.Name && enrolled.Protected(false) && enrolled.Protected(true) && enrolled.PinCheck.Length == 0);
                check("The former PIN cannot unlock the Hello vault", vault.Unlock(enrolled, "9876", now.AddSeconds(92)) is null);
                check("The former PIN key cannot decrypt Hello ciphertext",
                    Throws<CryptographicException>(() => vault.Entries(enrolled, false, key)));
                check("A stale editor cannot overwrite Hello encryption",
                    Throws<IOException>(() => vault.Save(previous, false, key, [])));
                check("Hello key decrypts entries after the encrypted store is reopened",
                    new VaultStore(path).Entries(enrolled, false, helloKey)[0].Password == "sentinel-password");
                var updated = vault.Entries(enrolled, false, helloKey);
                updated.Add(new("After enrollment", "test", "new-password", null));
                var oldCiphertext = enrolled.Passwords;
                vault.Save(enrolled, false, helloKey, updated);
                enrolled = vault.Read()!;
                check("Hello writes use fresh nonces and require the Hello key",
                    !oldCiphertext.AsSpan(0, 12).SequenceEqual(enrolled.Passwords.AsSpan(0, 12)) &&
                    Throws<CryptographicException>(() => vault.Entries(enrolled, false, key)));
                check("Hello ciphertext is authenticated to its vault section",
                    Throws<CryptographicException>(() => vault.Entries(enrolled with { Codes = enrolled.Passwords }, true, helloKey)));
                vault.Configure(enrolled, helloKey, "2468", pinPasswords: true, pinCodes: false);
                var restored = vault.Read()!;
                var restoredKey = vault.Unlock(restored, "2468", now.AddSeconds(93))!;
                try { check("Switching back from Hello preserves entries and PIN settings",
                    restored.Hello is null && !restored.Protected(true) && vault.Entries(restored, false, restoredKey).Count == 3); }
                finally { CryptographicOperations.ZeroMemory(restoredKey); }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
                CryptographicOperations.ZeroMemory(helloKey);
            }
            vault.Reset();
            check("Vault reset removes the encrypted store", vault.Read() is null);
        }
        finally { File.Delete(path); }
    }

    private static bool Throws<T>(Action action) where T : Exception
    {
        try { action(); return false; }
        catch (T) { return true; }
    }
}
