using System.Text.Json.Serialization;
using SmartX.Core.Services;

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
        "GET  /api/deployment/validate"
    }
}));

// Heavily seed the in-memory store on startup so the dashboard has data
// to display immediately, per the assignment's data-seeding requirement.
var store = app.Services.GetRequiredService<TelemetryStore>();
store.SeedAll();

app.Run();
