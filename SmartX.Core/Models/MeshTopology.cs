namespace SmartX.Core.Models;

public sealed record MeshNode(
    string DeviceId, string MacAddress, string Location, string Category,
    bool IsConnected, bool IsGateway, DateTime? LastSeenUtc, string[] ActiveAlerts);

/// <summary>Undirected simulated wireless connection; cost is an illustrative routing weight.</summary>
public sealed record MeshLink(
    string SourceId, string TargetId, double Cost, bool Enabled, bool IsAvailable);

public sealed record MeshSpanningTree(
    IReadOnlyList<MeshLink> Links, double TotalCost, int ComponentCount,
    bool IsConnected, int ActiveNodeCount);

public sealed record MeshTopologySnapshot(
    IReadOnlyList<MeshNode> Nodes, IReadOnlyList<MeshLink> Links,
    MeshSpanningTree SpanningTree, DateTime TimestampUtc, bool IsSimulated = true);

public sealed record MeshRoute(
    string SourceId, string TargetId, string Algorithm, bool Found,
    IReadOnlyList<string> Path, double TotalCost, int Hops, int VisitedCount, string Message);

public sealed record MeshTraversal(
    string SourceId, IReadOnlyList<string> ReachableDeviceIds,
    IReadOnlyList<string> OfflineDeviceIds, IReadOnlyList<string> UnreachableDeviceIds, int VisitedCount);
