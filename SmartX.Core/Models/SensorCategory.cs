namespace SmartX.Core.Models;

/// <summary>
/// The sensor categories Smart-X gateways ingest from ESP32 field nodes.
/// Each category maps to a distinct native CLR value type downstream
/// (see MockTelemetrySeeder / TelemetryController) so no type-coercion
/// or string-parsing overhead is needed on the hot ingestion path.
/// </summary>
public enum SensorCategory
{
    SoilMoisture,
    PowerMeter,
    ValveState,
    Temperature,
    Humidity,
    LightLevel
}
