using System.Globalization;
using System.Text;
using System.Text.Json;

namespace DeckIsland.Core;

public sealed record DecodedJwt(string Header, string Payload, string Expiry);

public sealed record ClipboardDecode(string Type, string Result, bool Tracking = false)
{
    public DecodedJwt? Token { get; init; }

    private const int MaxBytes = 100 * 1024;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly JsonSerializerOptions Indent = new() { WriteIndented = true };
    private static readonly HashSet<string> Trackers = new(StringComparer.OrdinalIgnoreCase)
        { "fbclid", "gclid", "dclid", "msclkid", "ttclid", "twclid", "igshid", "mc_cid", "mc_eid" };

    public static ClipboardDecode? Detect(string input)
    {
        if (input.Length > MaxBytes || Encoding.UTF8.GetByteCount(input) > MaxBytes) return null;
        string value = input.Trim();
        if (value.Length == 0) return null;

        if (Shelf.IsLink(value)) return CleanLink(value);

        if (value[0] == '#' && value.Length is 4 or 5 or 7 or 9
            && !Shelf.IsIssueNumberHex(value)
            && CssColour.TryHex(value.AsSpan(1), out var colour))
            return new("Hex colour", $"{colour.Rgb}\n{colour.Hsl}");

        if (Guid.TryParseExact(value, "D", out var uuid))
        {
            int variant = Convert.ToInt32(value[19].ToString(), 16);
            int version = Convert.ToInt32(value[14].ToString(), 16);
            return variant is >= 8 and <= 11 && version is >= 1 and <= 8
                ? new("UUID", $"Version {version}") : new("UUID", uuid == Guid.Empty ? "Nil UUID" : "Version unavailable");
        }

        if (value.Length is 9 or 10 or 12 or 13 && value.All(char.IsAsciiDigit)
            && long.TryParse(value, out long unix))
        {
            long milliseconds = value.Length <= 10 ? unix * 1000 : unix;
            if (milliseconds >= 946684800000 && milliseconds < 4133980800000)
            {
                var utc = DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
                return new("Unix timestamp", $"Local: {utc.ToLocalTime():yyyy-MM-dd HH:mm:ss.fff zzz}\nUTC: {utc:yyyy-MM-dd HH:mm:ss.fff} UTC");
            }
        }

        if (value[0] is '{' or '[')
        {
            try { using var json = JsonDocument.Parse(value); return new("JSON", Pretty(json.RootElement)); }
            catch (JsonException) { return null; }
        }

        if (value.Contains('.')) return Jwt(value);
        string? decoded = value.Length >= 8 && value.Length % 4 == 0 ? DecodeText(value) : null;
        return decoded is not null && decoded.Any(char.IsLetterOrDigit) ? new("Base64", decoded) : null;
    }

    private static ClipboardDecode? CleanLink(string value)
    {
        int fragment = value.IndexOf('#');
        if (fragment < 0) fragment = value.Length;
        int query = value.IndexOf('?');
        if (query < 0 || query >= fragment) return null;

        string[] parts = value[(query + 1)..fragment].Split('&');
        var kept = parts.Where(part =>
        {
            string key = Uri.UnescapeDataString(part.Split('=', 2)[0]);
            return !key.StartsWith("utm_", StringComparison.OrdinalIgnoreCase) && !Trackers.Contains(key);
        }).ToArray();
        if (kept.Length == parts.Length) return null;
        return new("Tracking link", value[..query] + (kept.Length == 0 ? "" : "?" + string.Join('&', kept)) + value[fragment..], true);
    }

    private static ClipboardDecode? Jwt(string value)
    {
        string[] parts = value.Split('.', 4);
        if (parts.Length != 3 || parts.Any(part => part.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
            || parts[2].Length % 4 == 1) return null;
        string? header = UrlSegment(parts[0]), payload = UrlSegment(parts[1]);
        if (header is null || payload is null) return null;
        try
        {
            using var head = JsonDocument.Parse(header);
            using var body = JsonDocument.Parse(payload);
            if (head.RootElement.ValueKind != JsonValueKind.Object || body.RootElement.ValueKind != JsonValueKind.Object
                || !head.RootElement.TryGetProperty("alg", out var alg) || alg.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(alg.GetString()) || (parts[2].Length == 0) != (alg.GetString() == "none")) return null;
            string expires = "Unavailable";
            if (body.RootElement.TryGetProperty("exp", out var exp) && exp.ValueKind == JsonValueKind.Number
                && exp.TryGetInt64(out long seconds) && seconds is >= -62135596800 and <= 253402300799)
                expires = DateTimeOffset.FromUnixTimeSeconds(seconds).ToString("u", CultureInfo.InvariantCulture);
            var token = new DecodedJwt(Pretty(head.RootElement), Pretty(body.RootElement), expires);
            return new("JWT", $"Signature has not been verified.\nExpiry: {expires}\n\nHeader\n{token.Header}\n\nPayload\n{token.Payload}") { Token = token };
        }
        catch (JsonException) { return null; }
    }

    private static string Pretty(JsonElement value) => JsonSerializer.Serialize(value, Indent);

    private static string? UrlSegment(string value) =>
        DecodeText(value.Replace('-', '+').Replace('_', '/').PadRight((value.Length + 3) / 4 * 4, '='));

    private static string? DecodeText(string value)
    {
        if (value.Any(char.IsWhiteSpace)) return null;
        try
        {
            byte[] bytes = Convert.FromBase64String(value);
            if (Convert.ToBase64String(bytes) != value) return null;
            string decoded = Utf8.GetString(bytes);
            return decoded.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')) ? null : decoded;
        }
        catch (FormatException) { return null; }
        catch (DecoderFallbackException) { return null; }
    }
}
