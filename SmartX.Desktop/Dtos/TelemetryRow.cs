namespace SmartX.Desktop.Dtos;

/// <summary>Client-side view of a telemetry packet, deserialised from the API's JSON response.</summary>
public class TelemetryRow
{
    public string PacketId { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public DateTime TimestampUtc { get; set; }
    public string Unit { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
