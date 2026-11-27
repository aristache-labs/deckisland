using System.Net.Http;
using System.Text.Json;
using System.Windows.Threading;
using DeckIsland.Core;
using Windows.Devices.Geolocation;

namespace DeckIsland;

public partial class App
{
    // The panels showing weather, and the dock while it shows a weather widget.
    private readonly HashSet<object> _weatherViewers = [];
    private readonly DispatcherTimer _weatherTimer = new() { Interval = TimeSpan.FromMinutes(30) };
    private (bool Automatic, WeatherCity? City)? _weatherKey;
    private CancellationTokenSource? _weatherRequest;
    private bool _weatherPermissionPending;
    private bool _weatherDenied;
    private (double Latitude, double Longitude, string Label)? _weatherLocality;
    internal WeatherReading? CurrentWeather { get; private set; }
    internal bool WeatherLoading { get; private set; }
    internal bool WeatherStale { get; private set; }
    internal bool WeatherNeedsLocation { get; private set; }
    internal event Action? WeatherChanged;

    internal void PresentWeather(object deck, bool wanted)
    {
        if (wanted) _weatherViewers.Add(deck);
        else _weatherViewers.Remove(deck);
        var key = (!Settings.ManualWeatherLocation, Settings.WeatherCity);
        if (!Settings.WeatherEnabled || _weatherViewers.Count == 0)
        {
            _weatherTimer.Stop();
            CancelWeather();
            WeatherLoading = false;
            if (Settings.WeatherEnabled && _weatherKey == key) return;
            _weatherKey = null;
            CurrentWeather = null;
            WeatherStale = WeatherNeedsLocation = false;
            return;
        }
        bool resumed = !_weatherTimer.IsEnabled;
        if (resumed)
        {
            _weatherTimer.Tick -= WeatherTick;
            _weatherTimer.Tick += WeatherTick;
            _weatherTimer.Start();
        }
        if (_weatherKey != key)
        {
            _weatherKey = key;
            CurrentWeather = null;
            WeatherStale = false;
        }
        else if (!resumed) return;
        StartWeatherRefresh();
    }

    private void WeatherTick(object? sender, EventArgs e) => StartWeatherRefresh();

    internal async Task RequestWeatherLocation()
    {
        if (_weatherPermissionPending) return;
        _weatherPermissionPending = true;
        CancelWeather();
        try { _weatherDenied = await Geolocator.RequestAccessAsync() != GeolocationAccessStatus.Allowed; }
        catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        { _weatherDenied = true; }
        finally { _weatherPermissionPending = false; }
        StartWeatherRefresh();
    }

    private void StartWeatherRefresh()
    {
        if (_weatherKey is not { } key || _weatherPermissionPending || Exiting) return;
        CancelWeather();
        _weatherRequest = new();
        _ = RefreshWeather(key.Automatic, key.City, _weatherRequest.Token);
    }

    private void CancelWeather()
    {
        _weatherRequest?.Cancel();
        _weatherRequest?.Dispose();
        _weatherRequest = null;
    }

    private async Task RefreshWeather(bool automatic, WeatherCity? city, CancellationToken token)
    {
        WeatherLoading = true;
        WeatherNeedsLocation = false;
        NotifyWeather();
        var point = automatic && !_weatherDenied ? await WindowsWeatherPoint(token) : null;
        if (token.IsCancellationRequested) return;
        if (point is not null)
        {
            if (_weatherLocality is { } locality && locality.Latitude == point.Latitude && locality.Longitude == point.Longitude)
                point = point with { Name = locality.Label };
            else
            {
                try
                {
                    var label = await Weather.ResolveDeviceLocation(point, token);
                    if (token.IsCancellationRequested) return;
                    if (label is not null)
                    {
                        _weatherLocality = (point.Latitude, point.Longitude, label);
                        point = point with { Name = label };
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException) { }
            }
        }
        else if (city is not null) point = new WeatherPoint(city.Label, city.Latitude, city.Longitude);
        if (token.IsCancellationRequested) return;
        WeatherReading? reading = null;
        try { if (point is not null) reading = await Weather.Fetch(point, token); }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException) { }
        if (token.IsCancellationRequested) return;
        var result = new WeatherUpdate(CurrentWeather, WeatherStale).Complete(point, reading, token);
        CurrentWeather = result.Reading;
        WeatherStale = result.Stale;
        WeatherNeedsLocation = point is null;
        WeatherLoading = false;
        NotifyWeather();
    }

    private void NotifyWeather() => Dispatcher.BeginInvoke(new Action(() => WeatherChanged?.Invoke()));

    private static async Task<WeatherPoint?> WindowsWeatherPoint(CancellationToken token)
    {
        try
        {
            var position = await new Geolocator { DesiredAccuracy = PositionAccuracy.Default }
                .GetGeopositionAsync(TimeSpan.FromMinutes(15), TimeSpan.FromSeconds(10)).AsTask(token);
            var point = position.Coordinate.Point.Position;
            if (WeatherPoint.Valid(point.Latitude, point.Longitude))
                return new("", Math.Round(point.Latitude, 2), Math.Round(point.Longitude, 2));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException or TimeoutException or OperationCanceledException or System.Runtime.InteropServices.COMException) { }
        return null;
    }
}
