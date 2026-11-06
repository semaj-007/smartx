namespace SmartX.Desktop.Dtos;

public sealed class MeshTopologyDto
{
    public List<MeshNodeDto> Nodes { get; set; } = [];
    public List<MeshLinkDto> Links { get; set; } = [];
    public MeshSpanningTreeDto SpanningTree { get; set; } = new();
    public DateTime TimestampUtc { get; set; }
    public bool IsSimulated { get; set; }
}

public sealed class MeshNodeDto
{
    public string DeviceId { get; set; } = "";
    public string MacAddress { get; set; } = "";
    public string Location { get; set; } = "";
    public string Category { get; set; } = "";
    public bool IsConnected { get; set; }
    public bool IsGateway { get; set; }
    public DateTime? LastSeenUtc { get; set; }
    public string[] ActiveAlerts { get; set; } = [];
}

public sealed class MeshLinkDto
{
    public string SourceId { get; set; } = "";
    public string TargetId { get; set; } = "";
    public double Cost { get; set; }
    public bool Enabled { get; set; }
    public bool IsAvailable { get; set; }
}

public sealed class MeshSpanningTreeDto
{
    public List<MeshLinkDto> Links { get; set; } = [];
    public double TotalCost { get; set; }
    public int ComponentCount { get; set; }
    public bool IsConnected { get; set; }
    public int ActiveNodeCount { get; set; }
}

public sealed class MeshRouteDto
{
    public string SourceId { get; set; } = "";
    public string TargetId { get; set; } = "";
    public string Algorithm { get; set; } = "";
    public bool Found { get; set; }
    public List<string> Path { get; set; } = [];
    public double TotalCost { get; set; }
    public int Hops { get; set; }
    public int VisitedCount { get; set; }
    public string Message { get; set; } = "";
}

public sealed class MeshHistoryDto
{
    public List<RealtimeTelemetryRow> Readings { get; set; } = [];
    public int RetainedCount { get; set; }
    public int VisitedNodes { get; set; }
    public int TreeHeight { get; set; }
    public bool Truncated { get; set; }
}

public sealed class MeshTraversalDto
{
    public string SourceId { get; set; } = "";
    public string[] ReachableDeviceIds { get; set; } = [];
    public string[] OfflineDeviceIds { get; set; } = [];
    public string[] UnreachableDeviceIds { get; set; } = [];
    public int VisitedCount { get; set; }
}
