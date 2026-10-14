namespace SmartX.Desktop.Dtos;

public sealed class RealtimeDashboard
{
    public List<LiveDeviceRow> Devices { get; set; } = [];
    public List<RealtimeTelemetryRow> RecentTelemetry { get; set; } = [];
    public List<CommandHistoryRow> Commands { get; set; } = [];
    public List<SuggestedActionRow> SuggestedActions { get; set; } = [];
    public RealtimeMetrics Metrics { get; set; } = new();
}

public sealed class LiveDeviceRow
{
    public string DeviceId { get; set; } = "";
    public string MacAddress { get; set; } = "";
    public string Location { get; set; } = "";
    public string Category { get; set; } = "";
    public string Value { get; set; } = "";
    public string Unit { get; set; } = "";
    public DateTime? LastSeenUtc { get; set; }
    public bool IsConnected { get; set; }
    public bool IsOn { get; set; }
    public string[] ActiveAlerts { get; set; } = [];
}

public sealed class RealtimeTelemetryRow
{
    public string PacketId { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string Category { get; set; } = "";
    public DateTime TimestampUtc { get; set; }
    public DateTime ProcessedUtc { get; set; }
    public long Sequence { get; set; }
    public string Value { get; set; } = "";
    public string Unit { get; set; } = "";
    public bool IsCritical { get; set; }
    public double? NumericValue { get; set; }
}

public sealed class CommandHistoryRow
{
    public string CommandId { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string Action { get; set; } = "";
    public DateTime IssuedUtc { get; set; }
    public bool IsUndone { get; set; }
    public bool IsSimulated { get; set; } = true;
    public string[] AffectedDeviceIds { get; set; } = [];
}

public sealed class SuggestedActionRow
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Detail { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string? Action { get; set; }
    public double Confidence { get; set; }
    public int EvidenceCount { get; set; }
}

public sealed class RealtimeMetrics
{
    public int StandardQueueDepth { get; set; }
    public int CriticalQueueDepth { get; set; }
    public long ProcessedPackets { get; set; }
    public long CriticalPackets { get; set; }
    public int UniqueActiveAlerts { get; set; }
    public long DroppedPackets { get; set; }
    public int RegisteredDevices { get; set; }
}

public sealed class SimulationStatus
{
    public bool Running { get; set; }
    public long SentPackets { get; set; }
    public long FailedRequests { get; set; }
    public DateTime? LastSentUtc { get; set; }
    public int IntervalMs { get; set; }
    public string? LastError { get; set; }
}
