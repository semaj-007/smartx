namespace SmartX.Core.Models;

public sealed record DashboardSnapshot(
    IReadOnlyList<DeviceSnapshot> Devices,
    IReadOnlyList<TelemetryLog> RecentTelemetry,
    IReadOnlyList<CommandRecord> Commands,
    IReadOnlyList<SuggestedAction> SuggestedActions,
    StreamMetrics Metrics);

public sealed record DeviceSnapshot(
    string DeviceId, string MacAddress, string Location, SensorCategory Category,
    string Value, string Unit, DateTime? LastSeenUtc, bool IsConnected, bool IsOn, string[] ActiveAlerts);

public sealed record TelemetryLog(
    string PacketId, string DeviceId, SensorCategory Category,
    DateTime TimestampUtc, DateTime ProcessedUtc, long Sequence,
    string Value, string Unit, bool IsCritical, double? NumericValue);

/// <summary>Commands alter simulated actuator state; physical device delivery is a future integration.</summary>
public sealed record CommandRecord(
    string CommandId, string DeviceId, string Action, DateTime IssuedUtc, bool IsUndone,
    string[] AffectedDeviceIds, bool IsSimulated = true);

public sealed record SuggestedAction(
    string Id, string Title, string Detail, string DeviceId, string Action,
    double Confidence, int EvidenceCount);

public sealed record StreamMetrics(
    int StandardQueueDepth, int CriticalQueueDepth, long ProcessedPackets,
    long CriticalPackets, int UniqueActiveAlerts, long DroppedPackets, int RegisteredDevices);
