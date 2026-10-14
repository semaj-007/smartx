namespace SmartX.Desktop.Dtos;

/// <summary>Client-side view of a sensor, deserialised from the API's JSON response.</summary>
public class SensorRow
{
    public string DeviceId { get; set; } = string.Empty;
    public string MacAddress { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public DateTime RegisteredUtc { get; set; }
    public string? AttachmentFileName { get; set; }
    public bool IsMock { get; set; }
}
