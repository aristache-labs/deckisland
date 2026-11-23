using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;

namespace DeckIsland.Core;

public sealed record VaultOtp(string Key, string Algorithm = "SHA1", int Digits = 6, int Period = 30)
{
    public static (VaultOtp Otp, string Name, string Account)? Parse(string value)
    {
        value = value.Trim();
        if (value.Length > 4096) return null;
        string name = "", account = "", algorithm = "SHA1";
        int digits = 6, period = 30;
        if (value.StartsWith("otpauth:", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Host != "totp") return null;
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in uri.Query.TrimStart('?').Split('&'))
            {
                var pair = field.Split('=', 2);
                if (pair.Length == 2 && !fields.TryAdd(Uri.UnescapeDataString(pair[0]), Uri.UnescapeDataString(pair[1]))) return null;
            }
            if (!fields.TryGetValue("secret", out var secret)) return null;
            value = secret;
            algorithm = fields.GetValueOrDefault("algorithm", algorithm).ToUpperInvariant();
            if (!int.TryParse(fields.GetValueOrDefault("digits", "6"), out digits) || digits is not (6 or 8) ||
                !int.TryParse(fields.GetValueOrDefault("period", "30"), out period) || period is < 5 or > 300 ||
                algorithm is not ("SHA1" or "SHA256" or "SHA512")) return null;
            var label = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')).Split(':', 2);
            name = fields.GetValueOrDefault("issuer", label.Length == 2 ? label[0].Trim() : "");
            account = label[^1].Trim();
        }
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = new List<byte>();
        int bits = 0, buffer = 0;
        foreach (char ch in value.TrimEnd('=').ToUpperInvariant())
        {
            if (char.IsWhiteSpace(ch)) continue;
            int index = alphabet.IndexOf(ch);
            if (index < 0) return null;
            buffer = (buffer << 5) | index;
            bits += 5;
            if (bits < 8) continue;
            bits -= 8;
            bytes.Add((byte)(buffer >> bits));
            buffer &= (1 << bits) - 1;
        }
        return bytes.Count is >= 1 and <= 128 && bits < 5 && buffer == 0
            ? (new VaultOtp(Convert.ToBase64String(bytes.ToArray()), algorithm, digits, period), name, account)
            : null;
    }

    public string Code(DateTimeOffset now)
    {
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, now.ToUnixTimeSeconds() / Period);
        byte[] key = Convert.FromBase64String(Key);
        try
        {
            byte[] hash = Algorithm switch
            {
                "SHA256" => HMACSHA256.HashData(key, counter),
                "SHA512" => HMACSHA512.HashData(key, counter),
                _ => HMACSHA1.HashData(key, counter)
            };
            int number = BinaryPrimitives.ReadInt32BigEndian(hash.AsSpan(hash[^1] & 15, 4)) & int.MaxValue;
            CryptographicOperations.ZeroMemory(hash);
            return (number % (Digits == 8 ? 100_000_000 : 1_000_000)).ToString($"D{Digits}", CultureInfo.InvariantCulture);
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    public override string ToString() => nameof(VaultOtp);
}

public static class VaultRules
{
    public static bool ValidPin(string pin) => pin.Length == 4 && pin.All(char.IsAsciiDigit);

    public static TimeSpan Delay(int failures) =>
        failures < 5 ? TimeSpan.Zero : TimeSpan.FromSeconds(30L << Math.Min(failures - 5, 20));

    public static string Generate(int length, bool digits, bool symbols)
    {
        const string lower = "abcdefghijkmnopqrstuvwxyz", upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        string[] groups = [lower, upper, .. digits ? new[] { "23456789" } : [], .. symbols ? new[] { "!@#$%^&*_-+=" } : []];
        string alphabet = string.Concat(groups);
        return string.Create(Math.Clamp(length, 12, 64), (groups, alphabet), (result, state) =>
        {
            for (int i = 0; i < result.Length; i++)
            {
                string source = i < state.groups.Length ? state.groups[i] : state.alphabet;
                result[i] = source[RandomNumberGenerator.GetInt32(source.Length)];
            }
            RandomNumberGenerator.Shuffle(result);
        });
    }
}
