using Microsoft.AspNetCore.Mvc;
using SmartX.Api.Dtos;
using SmartX.Core.Models;
using SmartX.Core.Services;
using System.Globalization;

namespace SmartX.Api.Controllers;

[ApiController]
[Route("api/telemetry")]
public class TelemetryController : ControllerBase
{
    private readonly TelemetryStore _store;
    private readonly RealTimeEngine _engine;

    public TelemetryController(TelemetryStore store, RealTimeEngine engine)
    {
        _store = store;
        _engine = engine;
    }

    /// <summary>GET /api/telemetry?count=50 - most recent ingested packets, newest first.</summary>
    [HttpGet]
    public IActionResult GetRecent([FromQuery] int count = 50)
    {
        if (count is < 1 or > 1000) return BadRequest("Count must be between 1 and 1000.");
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
        var sensor = _store.FindSensor(dto.DeviceId);
        if (sensor is null) return NotFound("Register the device before sending telemetry.");
        if (!Enum.IsDefined(dto.Category) || sensor.Category != dto.Category)
            return BadRequest("Category must match the registered device.");
        var valueType = dto.ValueType?.Trim().ToLowerInvariant();
        var expectedType = dto.Category switch
        {
            SensorCategory.ValveState => "bool",
            SensorCategory.PowerMeter or SensorCategory.LightLevel => "int",
            _ => "float"
        };
        if (valueType != expectedType || string.IsNullOrWhiteSpace(dto.Value) || (dto.Unit?.Length ?? 0) > 32)
            return BadRequest($"This device requires {expectedType} telemetry and a unit of at most 32 characters.");

        try
        {
            ITelemetryPacket packet = valueType switch
            {
                "float" => new TelemetryPacket<float>(sensor.DeviceId, dto.Category, ParseFinite(dto.Value), dto.Unit ?? string.Empty),
                "int" => new TelemetryPacket<int>(sensor.DeviceId, dto.Category, int.Parse(dto.Value, CultureInfo.InvariantCulture), dto.Unit ?? string.Empty),
                "bool" => new TelemetryPacket<bool>(sensor.DeviceId, dto.Category, bool.Parse(dto.Value), dto.Unit ?? string.Empty),
                _ => throw new ArgumentException("ValueType must be 'float', 'int' or 'bool'.")
            };

            if (!_engine.Enqueue(packet))
            {
                Response.Headers.RetryAfter = "1";
                return StatusCode(StatusCodes.Status429TooManyRequests, "Telemetry queue is full; retry later.");
            }

            return Accepted(new
            {
                packet.PacketId,
                packet.DeviceId,
                packet.Category,
                packet.TimestampUtc,
                packet.Unit,
                Value = packet.FormattedValue
            });
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>GET /api/telemetry/history/{deviceId} - jagged-array batches transferred into List&lt;T&gt;, with per-batch averages.</summary>
    [HttpGet("history/{deviceId}")]
    public IActionResult GetHistory(string deviceId)
    {
        var history = _store.GetHistory(deviceId);
        if (history is null)
            return NotFound($"No history recorded for device '{deviceId}'.");

        var batches = history.OptimisedBatches.Select((batch, index) => new
        {
            Batch = index,
            Readings = batch,
            Average = Math.Round(batch.Count == 0 ? 0 : batch.Average(), 2)
        });

        return Ok(batches);
    }

    private static float ParseFinite(string value)
    {
        var result = float.Parse(value, CultureInfo.InvariantCulture);
        if (!float.IsFinite(result)) throw new FormatException("Reading must be finite.");
        return result;
    }

    /// <summary>GET /api/telemetry/aggregate - demonstrates SensorReading operator overloading (+, -, &gt;).</summary>
    [HttpGet("aggregate")]
    public IActionResult AggregatePower()
    {
        var meters = _store.Sensors.Where(s => s.Category == SensorCategory.PowerMeter)
            .Select(s => new { s.DeviceId, Reading = _engine.GetTimeline(s.DeviceId, 1).LastOrDefault() })
            .Where(m => m.Reading?.NumericValue is not null && m.Reading.Unit == "W").Take(2).ToList();

        if (meters.Count < 2)
            return Ok(new { Message = "Need live watt readings from at least two power meters to demonstrate aggregation." });

        var r1 = new SensorReading(meters[0].DeviceId, meters[0].Reading!.NumericValue!.Value, "W");
        var r2 = new SensorReading(meters[1].DeviceId, meters[1].Reading!.NumericValue!.Value, "W");

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
