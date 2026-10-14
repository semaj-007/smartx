using SmartX.Core.Models;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace SmartX.Api.Dtos;

/// <summary>Inbound payload for POST /api/telemetry/ingest.</summary>
public class TelemetryIngestDto
{
    [Required, StringLength(64)]
    public string DeviceId { get; set; } = string.Empty;
    [JsonRequired]
    public SensorCategory Category { get; set; }

    /// <summary>"float" | "int" | "bool" - selects which TelemetryPacket&lt;T&gt; is constructed.</summary>
    [JsonRequired, Required, StringLength(10)]
    public string ValueType { get; set; } = string.Empty;
    [JsonRequired, Required, StringLength(64)]
    public string Value { get; set; } = string.Empty;
    public string? Unit { get; set; }
}
