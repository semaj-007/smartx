using System.Net.Http.Json;
using SmartX.Desktop.Dtos;

namespace SmartX.Desktop;

public partial class ApiClient
{
    public static string DefaultBaseUrl => Environment.GetEnvironmentVariable("SMARTX_API_URL") is { Length: > 0 } url
        ? url.TrimEnd('/') + "/"
        : "http://localhost:5080/";

    public Task<RealtimeDashboard> GetDashboardAsync(string query, string? category, bool alertsOnly, CancellationToken token)
    {
        var path = $"api/realtime/dashboard?query={Uri.EscapeDataString(query)}&alertsOnly={alertsOnly.ToString().ToLowerInvariant()}";
        if (!string.IsNullOrEmpty(category)) path += $"&category={Uri.EscapeDataString(category)}";
        return GetRealtimeAsync<RealtimeDashboard>(path, token);
    }

    public Task<List<RealtimeTelemetryRow>> GetTimelineAsync(string deviceId, CancellationToken token) =>
        GetRealtimeAsync<List<RealtimeTelemetryRow>>($"api/realtime/devices/{Uri.EscapeDataString(deviceId)}/timeline?count=200", token);

    public Task RecordSearchAsync(string query, CancellationToken token) =>
        PostRealtimeAsync("api/realtime/searches", new { query }, token);

    public Task SendCommandAsync(string deviceId, string action, CancellationToken token) =>
        PostRealtimeAsync("api/realtime/commands", new { deviceId, action }, token);

    public Task UndoCommandAsync(CancellationToken token) =>
        PostRealtimeAsync("api/realtime/commands/undo", new { }, token);

    public Task<SimulationStatus> GetSimulationAsync(CancellationToken token) =>
        GetRealtimeAsync<SimulationStatus>("api/realtime/simulation", token);

    public Task SetSimulationAsync(bool running, CancellationToken token) =>
        PostRealtimeAsync($"api/realtime/simulation/{(running ? "start" : "stop")}", new { }, token);

    private async Task<T> GetRealtimeAsync<T>(string path, CancellationToken token)
    {
        using var response = await _http.GetAsync(path, token);
        await EnsureRealtimeSuccessAsync(response, token);
        return await response.Content.ReadFromJsonAsync<T>(JsonOpts, token)
            ?? throw new InvalidOperationException("The API returned an empty response.");
    }

    private async Task PostRealtimeAsync(string path, object payload, CancellationToken token)
    {
        using var response = await _http.PostAsJsonAsync(path, payload, token);
        await EnsureRealtimeSuccessAsync(response, token);
    }

    private static async Task EnsureRealtimeSuccessAsync(HttpResponseMessage response, CancellationToken token)
    {
        if (response.IsSuccessStatusCode) return;
        var detail = await response.Content.ReadAsStringAsync(token);
        if (detail.Length > 400) detail = detail[..400];
        throw new HttpRequestException($"API {(int)response.StatusCode}: {detail}", null, response.StatusCode);
    }
}
