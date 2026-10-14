using Microsoft.AspNetCore.Mvc;
using SmartX.Core.Models;
using SmartX.Core.Services;

namespace SmartX.Api.Controllers;

[ApiController]
[Route("api/sensors")]
public class SensorsController : ControllerBase
{
    private readonly TelemetryStore _store;
    private readonly IWebHostEnvironment _env;

    public SensorsController(TelemetryStore store, IWebHostEnvironment env)
    {
        _store = store;
        _env = env;
    }

    /// <summary>GET /api/sensors - every registered sensor (mock-seeded + user-registered).</summary>
    [HttpGet]
    public IActionResult GetAll() => Ok(_store.Sensors.OrderByDescending(s => s.RegisteredUtc));

    /// <summary>POST /api/sensors/register - registers a sensor's deployment record (MAC, location, category).</summary>
    [HttpPost("register")]
    public IActionResult Register([FromBody] SensorRegistration sensor)
    {
        if (string.IsNullOrWhiteSpace(sensor.MacAddress))
            return BadRequest("MAC Address is required.");
        if (string.IsNullOrWhiteSpace(sensor.Location))
            return BadRequest("Deployment location is required.");

        sensor.RegisteredUtc = DateTime.UtcNow;
        sensor.IsMock = false;
        _store.AddSensor(sensor);

        return Created(string.Empty, sensor);
    }

    /// <summary>
    /// POST /api/sensors/{deviceId}/attachment - optimised multipart file
    /// upload for a device configuration file, deployment photo, or hardware
    /// log, attached directly to that sensor's profile.
    /// </summary>
    [HttpPost("{deviceId}/attachment")]
    [RequestSizeLimit(20_000_000)]
    public async Task<IActionResult> UploadAttachment(string deviceId, IFormFile file)
    {
        var sensor = _store.FindSensor(deviceId);
        if (sensor is null)
            return NotFound($"Sensor '{deviceId}' is not registered.");

        if (file is null || file.Length == 0)
            return BadRequest("No file was uploaded.");

        var uploadsDir = Path.Combine(_env.ContentRootPath, "App_Data", "uploads");
        Directory.CreateDirectory(uploadsDir);

        var safeFileName = $"{deviceId}_{Path.GetFileName(file.FileName)}";
        var fullPath = Path.Combine(uploadsDir, safeFileName);

        await using (var stream = new FileStream(fullPath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        sensor.AttachmentFileName = safeFileName;

        return Ok(new { sensor.DeviceId, FileName = safeFileName, SizeBytes = file.Length });
    }
}
