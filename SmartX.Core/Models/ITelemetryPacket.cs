namespace SmartX.Core.Models;

/// <summary>
/// Common, non-generic contract implemented by every closed
/// TelemetryPacket&lt;T&gt;. This lets the ingestion pipeline hold float,
/// int and bool readings side-by-side in a single List&lt;ITelemetryPacket&gt;
/// and read their metadata uniformly, while the underlying value itself
/// stays strongly typed inside each concrete packet (see TelemetryPacket&lt;T&gt;).
/// </summary>
public interface ITelemetryPacket
{
    string PacketId { get; }
    string DeviceId { get; }
    SensorCategory Category { get; }
    DateTime TimestampUtc { get; }
    string Unit { get; }
    string FormattedValue { get; }
}
