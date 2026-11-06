using System.Globalization;
using System.Net.Http.Json;
using SmartX.Desktop.Dtos;

namespace SmartX.Desktop;

public partial class ApiClient
{
    public Task<MeshTopologyDto> GetMeshTopologyAsync(CancellationToken token) =>
        GetRealtimeAsync<MeshTopologyDto>("api/mesh/topology", token);

    public Task<MeshRouteDto> GetMeshRouteAsync(string from, string to, string algorithm, CancellationToken token) =>
        GetRealtimeAsync<MeshRouteDto>($"api/mesh/route?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}&algorithm={Uri.EscapeDataString(algorithm)}", token);

    public Task<MeshTraversalDto> DiscoverMeshSubnetAsync(string from, CancellationToken token) =>
        GetRealtimeAsync<MeshTraversalDto>($"api/mesh/traversal?from={Uri.EscapeDataString(from)}", token);

    public Task<MeshHistoryDto> GetMeshHistoryAsync(string device, DateTime fromUtc, DateTime toUtc, CancellationToken token)
    {
        string from = Uri.EscapeDataString(fromUtc.ToString("O", CultureInfo.InvariantCulture));
        string to = Uri.EscapeDataString(toUtc.ToString("O", CultureInfo.InvariantCulture));
        return GetRealtimeAsync<MeshHistoryDto>($"api/mesh/history?device={Uri.EscapeDataString(device)}&fromUtc={from}&toUtc={to}&limit=1000", token);
    }

    public async Task SetMeshLinkAsync(string source, string target, double cost, bool enabled, CancellationToken token)
    {
        using var response = await _http.PutAsJsonAsync("api/mesh/links", new { source, target, cost, enabled }, token);
        await EnsureRealtimeSuccessAsync(response, token);
    }

    public async Task RemoveMeshLinkAsync(string source, string target, CancellationToken token)
    {
        using var response = await _http.DeleteAsync($"api/mesh/links?source={Uri.EscapeDataString(source)}&target={Uri.EscapeDataString(target)}", token);
        await EnsureRealtimeSuccessAsync(response, token);
    }
}
