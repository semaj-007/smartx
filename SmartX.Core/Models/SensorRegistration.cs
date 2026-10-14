namespace SmartX.Core.Models;

/// <summary>A registered sensor / ESP32 node in the Smart-X gateway.</summary>
public class SensorRegistration
{
    public string DeviceId { get; set; } = Guid.NewGuid().ToString("N")[..8].ToUpper();
    public string MacAddress { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public SensorCategory Category { get; set; }
    public DateTime RegisteredUtc { get; set; } = DateTime.UtcNow;
    public string? AttachmentFileName { get; set; }
    public bool IsMock { get; set; }
}
