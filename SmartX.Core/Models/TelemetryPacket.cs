namespace SmartX.Core.Models;

/// <summary>
/// Generic wrapper that carries a single strongly-typed telemetry value -
/// a float soil-moisture percentage, an int wattage reading, a bool valve
/// state, and so on - alongside its metadata.
///
/// Because T is constrained to struct, the value is stored inline as a
/// field of type T inside this (reference-type) wrapper: reading or
/// copying it never boxes or unboxes the value, even though many
/// different closed generic types (TelemetryPacket&lt;float&gt;,
/// TelemetryPacket&lt;int&gt;, TelemetryPacket&lt;bool&gt;, ...) can be
/// handled uniformly wherever ITelemetryPacket is used.
/// </summary>
public class TelemetryPacket<T> : ITelemetryPacket where T : struct
{
    public string PacketId { get; }
    public string DeviceId { get; }
    public SensorCategory Category { get; }
    public T Value { get; }
    public DateTime TimestampUtc { get; }
    public string Unit { get; }

    public TelemetryPacket(string deviceId, SensorCategory category, T value, string unit)
    {
        PacketId = Guid.NewGuid().ToString("N");
        DeviceId = deviceId;
        Category = category;
        Value = value;
        Unit = unit;
        TimestampUtc = DateTime.UtcNow;
    }

    /// <summary>Human-readable rendering of the strongly-typed value, used by the API and dashboard.</summary>
    public string FormattedValue => Value switch
    {
        bool b => b ? "OPEN" : "CLOSED",
        float f => f.ToString("0.0"),
        _ => Value!.ToString() ?? string.Empty
    };

    public override string ToString() => $"[{TimestampUtc:HH:mm:ss}] {DeviceId} ({Category}) = {FormattedValue}{Unit}";
}
