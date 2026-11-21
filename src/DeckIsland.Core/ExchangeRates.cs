using System.Globalization;
using System.Text.Json;

namespace DeckIsland.Core;

public sealed record CurrencyRate(string Code, decimal Rate, DateOnly Date);

// The Converter's currencies: the central banks' reference rates through Frankfurter, against the base currency. The banks publish
// on different days, so each rate keeps its own date. Rates are fetched at most once a day (or when the base changes), cached in
// exchange-rates.json, and kept as they were when a fetch fails.
public sealed class ExchangeRates : IDisposable
{
    public const string RatesSource = "https://api.frankfurter.dev/v2/rates";
    public const string CurrenciesSource = "https://api.frankfurter.dev/v2/currencies";
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromDays(1);
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly string _cacheFile;
    private readonly Func<DateTimeOffset> _now;
    private readonly SemaphoreSlim _gate = new(1);
    private readonly object _sync = new();
    private Dictionary<string, CurrencyRate> _rates = new(StringComparer.Ordinal);
    private Dictionary<string, string> _names = new(StringComparer.Ordinal);
    // A failed fetch is tried again after this, not on every call.
    public static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(15);
    private DateTimeOffset? _lastAttempt;
    private string? _lastAttemptBase;

    public string Base { get; private set; } = "";
    public DateTimeOffset? UpdatedAt { get; private set; }
    public string? LastError { get; private set; }
    public event Action? Changed;

    public ExchangeRates(string directory, HttpClient? http = null, Func<DateTimeOffset>? now = null)
    {
        _cacheFile = Path.Combine(directory, "exchange-rates.json");
        _http = http ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15) };
        _ownsHttp = http is null; _now = now ?? (() => DateTimeOffset.UtcNow);
        try
        {
            if (!File.Exists(_cacheFile)) return;
            var cache = JsonSerializer.Deserialize<Cache>(File.ReadAllText(_cacheFile));
            if (cache is null || NormalizeCode(cache.Base) is not { } baseCode || cache.Rates is null || cache.UpdatedAt > _now().AddMinutes(5)) throw new InvalidDataException();
            Set(baseCode, cache.Rates, cache.Names ?? [], cache.UpdatedAt);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or InvalidDataException)
        { LastError = Loc.T("汇率缓存不可用", "The rate cache is unavailable"); }
    }

    // Three letters A-Z, upper-cased; anything else is not a currency code.
    public static string? NormalizeCode(string? code)
    {
        code = (code ?? "").Trim().ToUpperInvariant();
        return code.Length == 3 && code.All(c => c is >= 'A' and <= 'Z') ? code : null;
    }

    // Every known currency (the base included), by code.
    public IReadOnlyList<string> Codes
    {
        get { lock (_sync) return [.. _rates.Keys.Append(Base).Where(code => code.Length > 0).Distinct().Order(StringComparer.Ordinal)]; }
    }

    public string NameOf(string code) { lock (_sync) return _names.TryGetValue(code, out var name) ? name : code; }

    private CurrencyRate? RateOf(string code)
    {
        lock (_sync) return code == Base ? null : _rates.GetValueOrDefault(code);
    }

    private bool Known(string code) { lock (_sync) return code == Base && Base.Length > 0 || _rates.ContainsKey(code); }

    // amount in from, in to: through the base currency (from -> base -> to). Null while either currency has no rate.
    public decimal? Convert(decimal amount, string from, string to)
    {
        if (!Known(from) || !Known(to)) return null;
        decimal fromRate = RateOf(from)?.Rate ?? 1m, toRate = RateOf(to)?.Rate ?? 1m;
        if (fromRate <= 0) return null;
        return amount / fromRate * toRate;
    }

    // The date the pair's conversion is good for: the older of the two rates (the base itself has no date of its own).
    public DateOnly? DateFor(string from, string to)
    {
        DateOnly?[] dates = [RateOf(from)?.Date, RateOf(to)?.Date];
        return dates.Where(date => date is not null).Min();
    }

    public bool IsStale(string baseCode) { lock (_sync) return Base != baseCode || UpdatedAt is null || _now() - UpdatedAt >= RefreshInterval; }

    // Fetches the rates against baseCode when they are a day old or the base changed (or always, with force). True when they changed.
    public async Task<bool> RefreshAsync(string baseCode, bool force = false, CancellationToken cancellation = default)
    {
        baseCode = NormalizeCode(baseCode) ?? "EUR";
        await _gate.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            var now = _now();
            if (!force && (!IsStale(baseCode) || _lastAttemptBase == baseCode && _lastAttempt is { } previous && now - previous < RetryInterval)) return false;
            _lastAttempt = now; _lastAttemptBase = baseCode;
            try
            {
                var rates = ParseRates(await GetAsync($"{RatesSource}?base={baseCode}", cancellation).ConfigureAwait(false), baseCode);
                if (rates.Count == 0) throw new InvalidDataException();
                Dictionary<string, string> names;
                try { names = ParseNames(await GetAsync(CurrenciesSource, cancellation).ConfigureAwait(false)); }
                catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidDataException || ex is TaskCanceledException && !cancellation.IsCancellationRequested)
                { lock (_sync) names = new(_names, StringComparer.Ordinal); }
                // Without the names the crypto in the rates list cannot be told from the currencies: no rates rather than wrong ones.
                if (names.Count == 0) throw new InvalidDataException();
                // Only real currencies are kept: the names list marks crypto, which the rates list mixes in.
                if (names.Count > 0) rates = rates.Where(rate => names.ContainsKey(rate.Code)).ToList();
                var updated = _now();
                Set(baseCode, rates, names, updated);
                LastError = null;
                Save(baseCode, rates, names, updated);
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidDataException || ex is TaskCanceledException && !cancellation.IsCancellationRequested)
            {
                LastError = Loc.T("无法获取汇率", "The rates could not be fetched");
                // The page says so instead of waiting on rates that are not coming.
                Changed?.Invoke();
                return false;
            }
            Changed?.Invoke();
            return true;
        }
        finally { _gate.Release(); }
    }

    private async Task<string> GetAsync(string url, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("DeckIsland/0.1.0");
        using var response = await _http.SendAsync(request, cancellation).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);
    }

    // [{"date":"2026-11-20","base":"EUR","quote":"USD","rate":1.05}, ...]
    internal static List<CurrencyRate> ParseRates(string json, string baseCode)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidDataException();
        var rates = new List<CurrencyRate>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            if (!item.TryGetProperty("quote", out var quote) || NormalizeCode(quote.GetString()) is not { } code || code == baseCode) continue;
            if (!item.TryGetProperty("rate", out var rate) || rate.ValueKind != JsonValueKind.Number || !rate.TryGetDecimal(out var value) || value <= 0) continue;
            if (!item.TryGetProperty("date", out var date) || !DateOnly.TryParseExact(date.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)) continue;
            rates.Add(new CurrencyRate(code, value, day));
        }
        return rates;
    }

    // [{"iso_code":"USD","name":"United States Dollar","type":"currency"}, ...]: the currencies only, not crypto.
    internal static Dictionary<string, string> ParseNames(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidDataException();
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            if (item.TryGetProperty("type", out var type) && type.GetString() != "currency") continue;
            if (!item.TryGetProperty("iso_code", out var iso) || NormalizeCode(iso.GetString()) is not { } code) continue;
            names[code] = item.TryGetProperty("name", out var name) && name.GetString() is { Length: > 0 } text ? text : code;
        }
        return names;
    }

    private void Set(string baseCode, IEnumerable<CurrencyRate> rates, Dictionary<string, string> names, DateTimeOffset updated)
    {
        lock (_sync)
        {
            Base = baseCode;
            _rates = rates.Where(rate => NormalizeCode(rate.Code) == rate.Code && rate.Rate > 0 && rate.Code != baseCode)
                .GroupBy(rate => rate.Code).ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
            _names = new Dictionary<string, string>(names, StringComparer.Ordinal);
            UpdatedAt = updated;
        }
    }

    private void Save(string baseCode, List<CurrencyRate> rates, Dictionary<string, string> names, DateTimeOffset updated)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_cacheFile)!);
            var temporary = _cacheFile + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(new Cache(baseCode, updated, rates, names)));
            File.Move(temporary, _cacheFile, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private sealed record Cache(string Base, DateTimeOffset UpdatedAt, List<CurrencyRate>? Rates, Dictionary<string, string>? Names);

    public void Dispose()
    {
        // The gate is left to the collector: a refresh still under way at exit releases it after this.
        if (_ownsHttp) _http.Dispose();
    }
}
