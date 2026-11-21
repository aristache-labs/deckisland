using System.Globalization;
using System.Text.RegularExpressions;
using System.Drawing;

namespace DeckIsland.Core;

public readonly record struct CssColour(byte R, byte G, byte B, byte A = 255)
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private static readonly Regex Function = new(@"\A(rgb|rgba|hsl|hsla|oklch)\(([^()]*)\)\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool TryHex(ReadOnlySpan<char> digits, out CssColour colour)
    {
        colour = default;
        if (digits.Length is not (3 or 4 or 6 or 8)
            || !uint.TryParse(digits, NumberStyles.AllowHexSpecifier, Invariant, out uint hex)) return false;
        if (digits.Length is 3 or 4)
        {
            uint expanded = 0;
            for (int shift = (digits.Length - 1) * 4; shift >= 0; shift -= 4)
                expanded = (expanded << 8) | ((hex >> shift) & 15) * 17;
            hex = expanded;
        }
        if (digits.Length is 3 or 6) hex = (hex << 8) | 255;
        colour = new((byte)(hex >> 24), (byte)(hex >> 16), (byte)(hex >> 8), (byte)hex);
        return true;
    }

    public static bool TryParse(string text, out CssColour colour)
    {
        colour = default;
        if (text.Length > 256) return false;
        text = text.Trim().ToLowerInvariant();
        if (text.StartsWith('#')) return TryHex(text.AsSpan(1), out colour);
        if (TryHex(text, out colour)) return true;
        if (text == "rebeccapurple") { colour = new(102, 51, 153); return true; }
        if (text == "transparent") { colour = new(0, 0, 0, 0); return true; }
        string name = text.Replace("grey", "gray", StringComparison.Ordinal);
        if (name.All(char.IsAsciiLetter) && Enum.TryParse(name, true, out KnownColor known)
            && known is >= KnownColor.AliceBlue and <= KnownColor.YellowGreen)
        {
            var named = Color.FromKnownColor(known);
            colour = new(named.R, named.G, named.B);
            return true;
        }

        var match = Function.Match(text);
        if (!match.Success) return false;
        string fn = match.Groups[1].Value;
        string args = match.Groups[2].Value;
        string[] slash = args.Split('/');
        if (slash.Length > 2 || args.Contains(',') && (slash.Length == 2 || fn == "oklch")) return false;
        string[] parts = args.Contains(',') ? args.Split(',', StringSplitOptions.TrimEntries)
            : slash[0].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        string? alpha = slash.Length == 2 ? slash[1].Trim() : parts.Length == 4 && args.Contains(',') ? parts[3] : null;
        if (parts.Length != (alpha is not null && args.Contains(',') ? 4 : 3)) return false;
        double opacity = 1;
        if (alpha is not null && !Number(alpha, 1, out opacity)) return false;
        byte a = Byte(opacity);
        if (fn is "rgb" or "rgba")
        {
            if (!Number(parts[0], 255, out double r) || !Number(parts[1], 255, out double g) || !Number(parts[2], 255, out double b)) return false;
            colour = new(Byte(r), Byte(g), Byte(b), a);
        }
        else if (fn is "hsl" or "hsla")
        {
            if (!Hue(parts[0], out double h) || !parts[1].EndsWith('%') || !parts[2].EndsWith('%')
                || !Number(parts[1], 1, out double s) || !Number(parts[2], 1, out double l)) return false;
            colour = FromHsl(h, Math.Clamp(s, 0, 1), Math.Clamp(l, 0, 1), a);
        }
        else
        {
            if (!Number(parts[0], 1, out double l) || !Number(parts[1], 1, out double c, percentScale: .4) || !Hue(parts[2], out double h)) return false;
            colour = FromOklch(Math.Clamp(l, 0, 1), Math.Clamp(c, 0, .5), h, a);
        }
        return true;
    }

    private static bool Scalar(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, Invariant, out value) && double.IsFinite(value);

    private static bool Number(string text, double scale, out double value, double percentScale = 1)
    {
        bool percent = text.EndsWith('%');
        if (!Scalar(percent ? text[..^1] : text, out value)) return false;
        value = percent ? value / 100 * percentScale : value / scale;
        return true;
    }

    private static bool Hue(string text, out double h)
    {
        foreach (var (suffix, factor) in new[] { ("turn", 360d), ("grad", .9), ("rad", 180 / Math.PI), ("deg", 1d) })
            if (text.EndsWith(suffix, StringComparison.Ordinal))
            {
                if (!Scalar(text[..^suffix.Length], out h)) return false;
                h = Wrap((h % (360 / factor)) * factor);
                return true;
            }
        if (!Scalar(text, out h)) return false;
        h = Wrap(h);
        return true;
    }

    private static double Wrap(double h) => (h % 360 + 360) % 360;
    private static byte Byte(double channel) => (byte)Math.Round(Math.Clamp(channel, 0, 1) * 255);
    private static string Format(FormattableString text) => text.ToString(Invariant);
    private string Alpha => A == 255 ? "" : Format($" / {A / 255d:0.####}");
    public string Hex => $"#{R:X2}{G:X2}{B:X2}" + (A == 255 ? "" : $"{A:X2}");
    public string Rgb => $"rgb({R} {G} {B}{Alpha})";
    public string Hsl
    {
        get
        {
            var (h, s, l) = HslChannels();
            return Format($"hsl({h:0.##} {s * 100:0.##}% {l * 100:0.##}%{Alpha})");
        }
    }

    private (double H, double S, double L) HslChannels()
    {
        double r = R / 255d, g = G / 255d, b = B / 255d;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double l = (max + min) / 2, d = max - min;
        if (d == 0) return (0, 0, l);
        double h = max == r ? (g - b) / d : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        return (Wrap(h * 60), d / (1 - Math.Abs(2 * l - 1)), l);
    }

    private static CssColour FromHsl(double h, double s, double l, byte a)
    {
        double amplitude = s * Math.Min(l, 1 - l);
        byte Channel(double n)
        {
            double k = (n + h / 30) % 12;
            return Byte(l - amplitude * Math.Max(-1, Math.Min(k - 3, Math.Min(9 - k, 1))));
        }
        return new(Channel(0), Channel(8), Channel(4), a);
    }

    public CssColour RotateHue(double degrees)
    {
        var (h, s, l) = HslChannels();
        return FromHsl(Wrap(h + degrees), s, l, A);
    }

    private static double Linear(double c) => c <= .04045 ? c / 12.92 : Math.Pow((c + .055) / 1.055, 2.4);
    private static double Gamma(double c) => c <= .0031308 ? 12.92 * c : 1.055 * Math.Pow(c, 1 / 2.4) - .055;
    private static double Luminance(double r, double g, double b) => .2126 * Linear(r) + .7152 * Linear(g) + .0722 * Linear(b);

    public double Contrast(CssColour background)
    {
        double opacity = background.A / 255d, foreground = A / 255d;
        double r = 1 - opacity + background.R / 255d * opacity;
        double g = 1 - opacity + background.G / 255d * opacity;
        double b = 1 - opacity + background.B / 255d * opacity;
        double bg = Luminance(r, g, b);
        double fg = Luminance(r + (R / 255d - r) * foreground, g + (G / 255d - g) * foreground, b + (B / 255d - b) * foreground);
        return (Math.Max(fg, bg) + .05) / (Math.Min(fg, bg) + .05);
    }

    public CssColour? AaSuggestion(CssColour background)
    {
        if (Contrast(background) >= 4.5) return null;
        ReadOnlySpan<byte> targets = [0, 255];
        for (int step = 0; step <= 255; step++)
        {
            foreach (byte target in targets)
            {
                byte Mix(byte channel) => (byte)Math.Round(channel + (target - channel) * step / 255d);
                var candidate = new CssColour(Mix(R), Mix(G), Mix(B));
                if (candidate.Contrast(background) >= 4.5) return candidate;
            }
        }
        return null;
    }

    public string Oklch
    {
        get
        {
            double r = Linear(R / 255d), g = Linear(G / 255d), b = Linear(B / 255d);
            double l = Math.Cbrt(.4122214708 * r + .5363325363 * g + .0514459929 * b);
            double m = Math.Cbrt(.2119034982 * r + .6806995451 * g + .1073969566 * b);
            double s = Math.Cbrt(.0883024619 * r + .2817188376 * g + .6299787005 * b);
            double lightness = .2104542553 * l + .7936177850 * m - .0040720468 * s;
            double x = 1.9779984951 * l - 2.4285922050 * m + .4505937099 * s;
            double y = .0259040371 * l + .7827717662 * m - .8086757660 * s;
            double c = Math.Sqrt(x * x + y * y), h = c < .000001 ? 0 : Wrap(Math.Atan2(y, x) * 180 / Math.PI);
            return Format($"oklch({lightness:0.#####} {c:0.#####} {h:0.##}{Alpha})");
        }
    }

    private static CssColour FromOklch(double lightness, double chroma, double hue, byte alpha)
    {
        double angle = hue * Math.PI / 180;
        double a = chroma * Math.Cos(angle), b = chroma * Math.Sin(angle);
        double l = Math.Pow(lightness + .3963377774 * a + .2158037573 * b, 3);
        double m = Math.Pow(lightness - .1055613458 * a - .0638541728 * b, 3);
        double s = Math.Pow(lightness - .0894841775 * a - 1.2914855480 * b, 3);
        return new(Byte(Gamma(4.0767416621 * l - 3.3077115913 * m + .2309699292 * s)),
            Byte(Gamma(-1.2684380046 * l + 2.6097574011 * m - .3413193965 * s)),
            Byte(Gamma(-.0041960863 * l - .7034186147 * m + 1.7076147010 * s)), alpha);
    }
}
