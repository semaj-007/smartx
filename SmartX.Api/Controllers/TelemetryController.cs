using Microsoft.AspNetCore.Mvc;
using SmartX.Api.Dtos;
using SmartX.Core.Models;
using SmartX.Core.Services;

namespace SmartX.Api.Controllers;

[ApiController]
[Route("api/telemetry")]
public class TelemetryController : ControllerBase
{
    private readonly TelemetryStore _store;

    public TelemetryController(TelemetryStore store)
    {
        _store = store;
    }

    /// <summary>GET /api/telemetry?count=50 - most recent ingested packets, newest first.</summary>
    [HttpGet]
    public IActionResult GetRecent([FromQuery] int count = 50)
    {
        var packets = _store.GetRecent(count).Select(p => new
        {
            p.PacketId,
            p.DeviceId,
            p.Category,
            p.TimestampUtc,
            p.Unit,
            Value = p.FormattedValue
        });

        return Ok(packets);
    }

    /// <summary>
    /// POST /api/telemetry/ingest - the primary receiver. Builds the correct
    /// closed TelemetryPacket&lt;T&gt; (float / int / bool) for the declared
    /// ValueType, so each sensor category is stored using its native CLR
    /// type instead of a generic stringly-typed envelope.
    /// </summary>
    [HttpPost("ingest")]
    public IActionResult Ingest([FromBody] TelemetryIngestDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.DeviceId))
            return BadRequest("DeviceId is required.");

        try
        {
            ITelemetryPacket packet = dto.ValueType.Trim().ToLowerInvariant() switch
            {
                "float" => new TelemetryPacket<float>(dto.DeviceId, dto.Category, float.Parse(dto.Value), dto.Unit ?? string.Empty),
                "int" => new TelemetryPacket<int>(dto.DeviceId, dto.Category, int.Parse(dto.Value), dto.Unit ?? string.Empty),
                "bool" => new TelemetryPacket<bool>(dto.DeviceId, dto.Category, bool.Parse(dto.Value), dto.Unit ?? string.Empty),
                _ => throw new ArgumentException("ValueType must be 'float', 'int' or 'bool'.")
            };

            _store.AddPacket(packet);

            return Created(string.Empty, new
            {
                packet.PacketId,
                packet.DeviceId,
                packet.Category,
                packet.TimestampUtc,
                packet.Unit,
                Value = packet.FormattedValue
            });
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>GET /api/telemetry/history/{deviceId} - jagged-array batches transferred into List&lt;T&gt;, with per-batch averages.</summary>
    [HttpGet("history/{deviceId}")]
    public IActionResult GetHistory(string deviceId)
    {
        if (!_store.HistoryBySensor.TryGetValue(deviceId, out var history))
            return NotFound($"No history recorded for device '{deviceId}'.");

        var batches = history.OptimisedBatches.Select((batch, index) => new
        {
            Batch = index,
            Readings = batch,
            Average = Math.Round(batch.Count == 0 ? 0 : batch.Average(), 2)
        });

        return Ok(batches);
    }

    /// <summary>GET /api/telemetry/aggregate - demonstrates SensorReading operator overloading (+, -, &gt;).</summary>
    [HttpGet("aggregate")]
    public IActionResult AggregatePower()
    {
        var meters = _store.Sensors.Where(s => s.Category == SensorCategory.PowerMeter).Take(2).ToList();

        if (meters.Count < 2)
            return Ok(new { Message = "Need at least two registered power meters to demonstrate aggregation." });

        var r1 = new SensorReading(meters[0].DeviceId, Random.Shared.Next(200, 800), "W");
        var r2 = new SensorReading(meters[1].DeviceId, Random.Shared.Next(200, 800), "W");

        var aggregateLoad = r1 + r2;
        var delta = r1 - r2;

        return Ok(new
        {
            Meter1 = r1.ToString(),
            Meter2 = r2.ToString(),
            AggregateLoad = aggregateLoad.ToString(),
            Delta = delta.ToString(),
            Meter1ExceedsMeter2 = r1 > r2
        });
    }
}
