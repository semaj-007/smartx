using SmartX.Core.Models;

namespace SmartX.Core.Services;

/// <summary>
/// Generates large volumes of realistic mock telemetry so the ingestion
/// pipeline, history batching and deployment-tree validation can be
/// proven under load without needing physical ESP32 hardware attached.
/// </summary>
public static class MockTelemetrySeeder
{
    private static readonly string[] Locations =
    {
        "Hydroponic Bay 1", "Hydroponic Bay 2", "Rooftop Grid Node A",
        "Utility Room B12", "Greenhouse Zone 3", "Substation Node 7",
        "Borehole Pump Shed", "Vertical Farm Rack C"
    };

    public static List<SensorRegistration> SeedSensors(int count = 12)
    {
        var sensors = new List<SensorRegistration>();
        var categories = Enum.GetValues<SensorCategory>();

        for (int i = 0; i < count; i++)
        {
            sensors.Add(new SensorRegistration
            {
                MacAddress = RandomMac(),
                Location = Locations[Random.Shared.Next(Locations.Length)],
                Category = categories[Random.Shared.Next(categories.Length)],
                IsMock = true
            });
        }

        return sensors;
    }

    public static string RandomMac()
    {
        var bytes = new byte[6];
        Random.Shared.NextBytes(bytes);
        return string.Join(":", bytes.Select(b => b.ToString("X2")));
    }

    /// <summary>
    /// Produces a heavily-seeded stream of TelemetryPacket&lt;T&gt; instances,
    /// exposed uniformly through ITelemetryPacket so the store can hold
    /// float, int and bool readings side by side in a single List&lt;T&gt;
    /// without any of them being boxed as plain object.
    /// </summary>
    public static List<ITelemetryPacket> SeedTelemetryStream(List<SensorRegistration> sensors, int packetsPerSensor = 20)
    {
        var stream = new List<ITelemetryPacket>();

        foreach (var sensor in sensors)
        {
            for (int i = 0; i < packetsPerSensor; i++)
            {
                ITelemetryPacket packet = sensor.Category switch
                {
                    SensorCategory.SoilMoisture => new TelemetryPacket<float>(sensor.DeviceId, sensor.Category, (float)Math.Round(Random.Shared.NextDouble() * 100, 1), "%"),
                    SensorCategory.PowerMeter => new TelemetryPacket<int>(sensor.DeviceId, sensor.Category, Random.Shared.Next(50, 2500), "W"),
                    SensorCategory.ValveState => new TelemetryPacket<bool>(sensor.DeviceId, sensor.Category, Random.Shared.Next(2) == 1, ""),
                    SensorCategory.Temperature => new TelemetryPacket<float>(sensor.DeviceId, sensor.Category, (float)Math.Round(15 + Random.Shared.NextDouble() * 20, 1), "°C"),
                    SensorCategory.Humidity => new TelemetryPacket<float>(sensor.DeviceId, sensor.Category, (float)Math.Round(Random.Shared.NextDouble() * 100, 1), "%RH"),
                    SensorCategory.LightLevel => new TelemetryPacket<int>(sensor.DeviceId, sensor.Category, Random.Shared.Next(0, 10000), "lux"),
                    _ => new TelemetryPacket<float>(sensor.DeviceId, sensor.Category, 0f, "")
                };

                stream.Add(packet);
            }
        }

        return stream;
    }

    /// <summary>
    /// Seeds a nested deployment tree: Facility -> Zone -> Sub-Zone -> Device,
    /// with one Sub-Zone deliberately left unconfigured so the recursive
    /// validator has something real to catch.
    /// </summary>
    public static DeviceNode SeedDeploymentTree()
    {
        var facility = new DeviceNode("Facility A", "Facility");
        var zone1 = new DeviceNode("Zone 1", "Zone");
        var zone2 = new DeviceNode("Zone 2", "Zone");
        var subZoneB = new DeviceNode("Sub-Zone B", "Sub-Zone");
        var subZoneC = new DeviceNode("Sub-Zone C", "Sub-Zone", isConfigured: false);

        subZoneB.Children.Add(new DeviceNode("ESP32-Soil-01", "Device"));
        subZoneB.Children.Add(new DeviceNode("ESP32-Valve-02", "Device"));
        subZoneC.Children.Add(new DeviceNode("ESP32-Power-03", "Device"));

        zone1.Children.Add(subZoneB);
        zone2.Children.Add(subZoneC);
        facility.Children.Add(zone1);
        facility.Children.Add(zone2);

        return facility;
    }
}
