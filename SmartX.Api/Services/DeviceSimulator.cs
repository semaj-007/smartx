using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using SmartX.Core.Models;

namespace SmartX.Api.Services;

/// <summary>An opt-in virtual ESP32 fleet. Every reading crosses the HTTP API boundary.</summary>
public sealed class DeviceSimulator : BackgroundService
{
    private readonly object _gate = new();
    private bool _running;
    private long _sent;
    private long _failures;
    private string? _lastError;
    private DateTime? _lastSentUtc;
    private int _step;
    private CancellationTokenSource? _cycle;
    private readonly HttpClient _http = new()
    {
        BaseAddress = new Uri("http://localhost:5080/"),
        Timeout = TimeSpan.FromSeconds(5)
    };
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public void Start() { lock (_gate) _running = true; }
    public void Stop()
    {
        lock (_gate)
        {
            _running = false;
            _cycle?.Cancel();
        }
    }
    public object GetStatus()
    {
        lock (_gate) return new { Running = _running, SentPackets = _sent, FailedRequests = _failures,
            LastError = _lastError, LastSentUtc = _lastSentUtc, IntervalMs = 1000 };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            CancellationTokenSource cycle;
            lock (_gate)
            {
                if (!_running) continue;
                cycle = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                _cycle = cycle;
            }
            try
            {
                // Fetch registration and desired actuator states over HTTP as a real fleet would.
                var dashboard = await _http.GetFromJsonAsync<DashboardSnapshot>("api/realtime/dashboard", JsonOptions, cycle.Token);
                foreach (var device in dashboard?.Devices ?? [])
                {
                    cycle.Token.ThrowIfCancellationRequested();
                    var phase = _step % 40;
                    var (type, value, unit) = device.Category switch
                    {
                        // Repeating downward trend crosses 20%, then recovers. No recommendations are fabricated.
                        SensorCategory.SoilMoisture => ("float", (45 - phase).ToString(CultureInfo.InvariantCulture), "%"),
                        SensorCategory.PowerMeter => ("int", (phase is >= 30 and <= 32 ? 2400 : device.IsOn ? 650 : 0).ToString(CultureInfo.InvariantCulture), "W"),
                        SensorCategory.ValveState => ("bool", device.IsOn ? "true" : "false", ""),
                        SensorCategory.Temperature => ("float", (24 + 2 * Math.Sin(_step / 4.0)).ToString("F1", CultureInfo.InvariantCulture), "C"),
                        SensorCategory.Humidity => ("float", (55 + 8 * Math.Sin(_step / 5.0)).ToString("F1", CultureInfo.InvariantCulture), "%RH"),
                        _ => ("int", (5000 + _step % 100).ToString(CultureInfo.InvariantCulture), "lux")
                    };
                    using var response = await _http.PostAsJsonAsync("api/telemetry/ingest",
                        new { deviceId = device.DeviceId, category = device.Category.ToString(), valueType = type, value, unit }, cycle.Token);
                    response.EnsureSuccessStatusCode();
                    lock (_gate) { _sent++; _lastSentUtc = DateTime.UtcNow; _lastError = null; }
                }
                _step++;
            }
            catch (OperationCanceledException) when (cycle.IsCancellationRequested) { }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException)
            {
                lock (_gate) { _failures++; _lastError = ex.Message; }
            }
            finally
            {
                lock (_gate) { if (ReferenceEquals(_cycle, cycle)) _cycle = null; }
                cycle.Dispose();
            }
        }
    }

    public override void Dispose()
    {
        Stop();
        _http.Dispose();
        base.Dispose();
    }
}
