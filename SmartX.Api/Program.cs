using System.Text.Json.Serialization;
using SmartX.Core.Services;
using SmartX.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Fixed, predictable port so the WinForms dashboard can point at it directly.
builder.WebHost.UseUrls("http://localhost:5080");

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Serialise/deserialise SensorCategory (and any other enum) as its
        // name ("SoilMoisture") rather than its numeric value - friendlier
        // for the dashboard and for manual testing via curl/Postman.
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.AddSingleton<TelemetryStore>();
builder.Services.AddSingleton<RealTimeEngine>();
builder.Services.AddHostedService<TelemetryProcessor>();
builder.Services.AddSingleton<DeviceSimulator>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DeviceSimulator>());

var app = builder.Build();

app.MapControllers();

app.MapGet("/", () => Results.Ok(new
{
    Service = "Smart-X Ingestion Gateway",
    Status = "Online",
    Endpoints = new[]
    {
        "GET  /api/sensors",
        "POST /api/sensors/register",
        "POST /api/sensors/{deviceId}/attachment",
        "GET  /api/telemetry",
        "POST /api/telemetry/ingest",
        "GET  /api/telemetry/history/{deviceId}",
        "GET  /api/telemetry/aggregate",
        "GET  /api/deployment/tree",
        "GET  /api/deployment/validate",
        "GET  /api/realtime/dashboard",
        "GET  /api/realtime/devices/{deviceId}/timeline",
        "POST /api/realtime/commands",
        "POST /api/realtime/commands/undo",
        "POST /api/realtime/searches",
        "POST /api/realtime/simulation/start",
        "POST /api/realtime/simulation/stop"
    }
}));

// Seed registration metadata only. Live readings, including simulated
// readings, must enter through POST /api/telemetry/ingest.
var store = app.Services.GetRequiredService<TelemetryStore>();
store.SeedAll();

app.Run();
