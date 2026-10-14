using SmartX.Core.Models;

namespace SmartX.Core.Services;

/// <summary>
/// Thread-safe in-memory store standing in for a real time-series
/// database / message broker in this simulation. Seeded heavily on
/// API startup via MockTelemetrySeeder.
/// </summary>
public class TelemetryStore
{
    private readonly object _lock = new();

    public List<SensorRegistration> Sensors { get; } = new();
    public List<ITelemetryPacket> Packets { get; } = new();
    public Dictionary<string, TelemetryHistoryStore> HistoryBySensor { get; } = new();
    public DeviceNode DeploymentTree { get; private set; } = new("Unseeded", "Facility");

    public void SeedAll()
    {
        lock (_lock)
        {
            Sensors.AddRange(MockTelemetrySeeder.SeedSensors(14));
            Packets.AddRange(MockTelemetrySeeder.SeedTelemetryStream(Sensors, 25));
            DeploymentTree = MockTelemetrySeeder.SeedDeploymentTree();

            foreach (var sensor in Sensors)
                HistoryBySensor[sensor.DeviceId] = new TelemetryHistoryStore(7);
        }
    }

    public void AddSensor(SensorRegistration sensor)
    {
        lock (_lock)
        {
            Sensors.Add(sensor);
            HistoryBySensor[sensor.DeviceId] = new TelemetryHistoryStore(7);
        }
    }

    public void AddPacket(ITelemetryPacket packet)
    {
        lock (_lock)
        {
            Packets.Add(packet);
        }
    }

    public List<ITelemetryPacket> GetRecent(int count)
    {
        lock (_lock)
        {
            return Packets.OrderByDescending(p => p.TimestampUtc).Take(count).ToList();
        }
    }

    public SensorRegistration? FindSensor(string deviceId)
    {
        lock (_lock)
        {
            return Sensors.FirstOrDefault(s => s.DeviceId == deviceId);
        }
    }
}
