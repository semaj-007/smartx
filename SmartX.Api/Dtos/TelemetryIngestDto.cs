using SmartX.Core.Models;

namespace SmartX.Api.Dtos;

/// <summary>Inbound payload for POST /api/telemetry/ingest.</summary>
public class TelemetryIngestDto
{
    public string DeviceId { get; set; } = string.Empty;
    public SensorCategory Category { get; set; }

    /// <summary>"float" | "int" | "bool" - selects which TelemetryPacket&lt;T&gt; is constructed.</summary>
    public string ValueType { get; set; } = "float";
    public string Value { get; set; } = "0";
    public string? Unit { get; set; }
}
