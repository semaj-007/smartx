using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using SmartX.Core.Services;

namespace SmartX.Api.Controllers;

[ApiController]
[Route("api/mesh")]
public sealed class MeshController(MeshNetworkService mesh, RealTimeEngine engine) : ControllerBase
{
    [HttpGet("topology")]
    public IActionResult Topology() => Ok(mesh.GetTopology());

    [HttpGet("traversal")]
    public IActionResult Traversal([FromQuery, StringLength(100)] string from = "GATEWAY")
    {
        try { return Ok(mesh.Traverse(from)); }
        catch (KeyNotFoundException ex) { return NotFound(ex.Message); }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
    }

    [HttpGet("route")]
    public IActionResult Route([FromQuery, Required, StringLength(100)] string from,
        [FromQuery, StringLength(100)] string to = "GATEWAY", [FromQuery] string algorithm = "bfs")
    {
        try { return Ok(mesh.FindRoute(from, to, algorithm)); }
        catch (KeyNotFoundException ex) { return NotFound(ex.Message); }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
    }

    // Bound dates are DateTimeOffset so a local offset is unambiguously converted to UTC.
    // Model binding and validation reject malformed request values (Microsoft, n.d.-p).
    [HttpGet("history")]
    public IActionResult History([FromQuery, Required, StringLength(100)] string device,
        [FromQuery, Required] DateTimeOffset? fromUtc, [FromQuery, Required] DateTimeOffset? toUtc,
        [FromQuery, Range(1, 1000)] int limit = 1000)
    {
        if (!fromUtc.HasValue || !toUtc.HasValue) return BadRequest("Both range endpoints are required.");
        if (fromUtc > toUtc) return BadRequest("The start timestamp must not follow the end timestamp.");
        try
        {
            var id = mesh.ResolveDeviceId(device);
            if (id == "GATEWAY") return BadRequest("Choose a sensor device to query telemetry history.");
            return Ok(engine.GetHistoryRange(id, fromUtc.Value.UtcDateTime, toUtc.Value.UtcDateTime, limit));
        }
        catch (KeyNotFoundException ex) { return NotFound(ex.Message); }
        catch (InvalidOperationException ex) { return NotFound(ex.Message); }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
    }

    [HttpPut("links")]
    public IActionResult UpsertLink(MeshLinkRequest request)
    {
        try
        {
            mesh.UpsertLink(request.Source, request.Target, request.Cost, request.Enabled);
            return Ok(mesh.GetTopology());
        }
        catch (KeyNotFoundException ex) { return NotFound(ex.Message); }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
    }

    [HttpDelete("links")]
    public IActionResult DeleteLink([FromQuery, Required, StringLength(100)] string source,
        [FromQuery, Required, StringLength(100)] string target)
    {
        try { mesh.RemoveLink(source, target); return NoContent(); }
        catch (KeyNotFoundException ex) { return NotFound(ex.Message); }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
    }
}

public sealed class MeshLinkRequest
{
    [Required, StringLength(100)] public string Source { get; set; } = string.Empty;
    [Required, StringLength(100)] public string Target { get; set; } = string.Empty;
    [Range(0.000001, 100000)] public double Cost { get; set; } = 1;
    public bool Enabled { get; set; } = true;
}
