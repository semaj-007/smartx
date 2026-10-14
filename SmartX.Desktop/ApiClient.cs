using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SmartX.Desktop.Dtos;

namespace SmartX.Desktop;

/// <summary>Thin HttpClient wrapper for the SmartX ingestion API, using plain System.Text.Json.</summary>
// HttpClient GET/POST requests (GeeksforGeeks, 2022)
public partial class ApiClient : IDisposable
{
    private readonly HttpClient _http;

    // JSON serialization (Microsoft, n.d.)
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ApiClient(string baseUrl)
    {
        _http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(15) };
    }

    public async Task<List<SensorRow>> GetSensorsAsync(CancellationToken cancellationToken = default)
    {
        var json = await _http.GetStringAsync("api/sensors", cancellationToken);
        return JsonSerializer.Deserialize<List<SensorRow>>(json, JsonOpts) ?? new List<SensorRow>();
    }

    public async Task<List<TelemetryRow>> GetTelemetryAsync(int count = 60, CancellationToken cancellationToken = default)
    {
        var json = await _http.GetStringAsync($"api/telemetry?count={count}", cancellationToken);
        return JsonSerializer.Deserialize<List<TelemetryRow>>(json, JsonOpts) ?? new List<TelemetryRow>();
    }

    public async Task<SensorRow?> RegisterSensorAsync(string mac, string location, string category)
    {
        var payload = new { macAddress = mac, location, category };
        using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync("api/sensors/register", content);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<SensorRow>(json, JsonOpts);
    }

    public async Task UploadAttachmentAsync(string deviceId, string filePath)
    {
        using var form = new MultipartFormDataContent();
        var bytes = await File.ReadAllBytesAsync(filePath);
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(fileContent, "file", Path.GetFileName(filePath));

        using var response = await _http.PostAsync($"api/sensors/{Uri.EscapeDataString(deviceId)}/attachment", form);
        response.EnsureSuccessStatusCode();
    }

    public async Task IngestTelemetryAsync(string deviceId, string category, string valueType, string value, string unit)
    {
        var payload = new { deviceId, category, valueType, value, unit };
        using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync("api/telemetry/ingest", content);
        response.EnsureSuccessStatusCode();
    }

    public async Task<string> ValidateDeploymentAsync() => await _http.GetStringAsync("api/deployment/validate");

    public async Task<string> AggregateAsync() => await _http.GetStringAsync("api/telemetry/aggregate");

    public void Dispose() => _http.Dispose();
}
