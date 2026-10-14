using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using SmartX.Api.Services;
using SmartX.Core.Models;
using SmartX.Core.Services;

namespace SmartX.Api.Controllers;

[ApiController]
[Route("api/realtime")]
public sealed class RealTimeController(RealTimeEngine engine, DeviceSimulator simulator) : ControllerBase
{
    [HttpGet("dashboard")]
    public IActionResult Dashboard([FromQuery] string query = "", [FromQuery] SensorCategory? category = null,
        [FromQuery] bool alertsOnly = false)
    {
        if (query.Length > 100 || (category.HasValue && !Enum.IsDefined(category.Value)))
            return BadRequest("Use a query of at most 100 characters and a valid category.");
        return Ok(engine.GetSnapshot(query, category, alertsOnly));
    }

    [HttpGet("devices/{deviceId}/timeline")]
    public IActionResult Timeline(string deviceId, [FromQuery] int count = 200)
    {
        if (count is < 1 or > 1000) return BadRequest("Count must be between 1 and 1000.");
        if (!engine.GetSnapshot(query: deviceId).Devices.Any(d => d.DeviceId.Equals(deviceId, StringComparison.OrdinalIgnoreCase)))
            return NotFound("Device is not registered.");
        return Ok(engine.GetTimeline(deviceId, count));
    }

    // Explicit search intent is recorded once; dashboard polling never trains the model.
    [HttpPost("searches")]
    public IActionResult Search(SearchRequest request)
    {
        engine.RecordSearch(request.Query.Trim());
        return NoContent();
    }

    [HttpPost("commands")]
    public IActionResult Command(CommandRequest request)
    {
        try { return Ok(engine.ExecuteCommand(request.DeviceId, request.Action)); }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
        catch (InvalidOperationException ex) { return Conflict(ex.Message); }
    }

    [HttpPost("commands/undo")]
    public IActionResult Undo()
    {
        try { return Ok(engine.UndoCommand()); }
        catch (InvalidOperationException ex) { return Conflict(ex.Message); }
    }

    [HttpGet("simulation")]
    public IActionResult SimulationStatus() => Ok(simulator.GetStatus());

    [HttpPost("simulation/start")]
    public IActionResult StartSimulation() { simulator.Start(); return Ok(simulator.GetStatus()); }

    [HttpPost("simulation/stop")]
    public IActionResult StopSimulation() { simulator.Stop(); return Ok(simulator.GetStatus()); }
}

public sealed class SearchRequest
{
    [Required, StringLength(100)] public string Query { get; set; } = string.Empty;
}

public sealed class CommandRequest
{
    [Required, StringLength(64)] public string DeviceId { get; set; } = string.Empty;
    [Required, RegularExpression("^(TurnOn|TurnOff|AllValvesOff)$")]
    public string Action { get; set; } = string.Empty;
}
