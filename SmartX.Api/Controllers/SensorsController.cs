using Microsoft.AspNetCore.Mvc;
using SmartX.Core.Models;
using SmartX.Core.Services;
using System.Text.RegularExpressions;

namespace SmartX.Api.Controllers;

[ApiController]
[Route("api/sensors")]
public class SensorsController : ControllerBase
{
    private readonly TelemetryStore _store;
    private readonly IWebHostEnvironment _env;
    private readonly RealTimeEngine _engine;

    public SensorsController(TelemetryStore store, IWebHostEnvironment env, RealTimeEngine engine)
    {
        _store = store;
        _env = env;
        _engine = engine;
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
        if (!Regex.IsMatch(sensor.DeviceId ?? "", "^[A-Za-z0-9_-]{1,64}$") || !Enum.IsDefined(sensor.Category))
            return BadRequest("Use a valid category and a device ID containing 1–64 letters, digits, underscores or hyphens.");
        if (!Regex.IsMatch(sensor.MacAddress.Trim(), "^([0-9A-Fa-f]{2}:){5}[0-9A-Fa-f]{2}$") || sensor.Location.Length > 200)
            return BadRequest("Use a colon-separated MAC address and a location of at most 200 characters.");

        sensor.RegisteredUtc = DateTime.UtcNow;
        sensor.IsMock = false;
        try { _engine.RegisterDevice(sensor); }
        catch (ArgumentException ex) { return Conflict(ex.Message); }

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

        _store.SetAttachment(sensor.DeviceId, safeFileName);

        return Ok(new { sensor.DeviceId, FileName = safeFileName, SizeBytes = file.Length });
    }
}
