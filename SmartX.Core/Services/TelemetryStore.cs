using SmartX.Core.Models;

namespace SmartX.Core.Services;

/// <summary>Thread-safe bounded compatibility store for Part 1 ingestion endpoints.</summary>
public class TelemetryStore
{
    private readonly object _gate = new();
    // Dictionary lookup replaces a linear scan for each device ingestion.
    private readonly Dictionary<string, SensorRegistration> _sensors = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _deviceByMac = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<ITelemetryPacket> _packets = new();
    private readonly Dictionary<string, TelemetryHistoryStore> _history = new(StringComparer.OrdinalIgnoreCase);
    private const int PacketRetention = 4096;

    public List<SensorRegistration> Sensors
    {
        get { lock (_gate) return _sensors.Values.Select(CopyRegistration).ToList(); }
    }

    public List<ITelemetryPacket> Packets
    {
        get { lock (_gate) return _packets.ToList(); }
    }

    public Dictionary<string, TelemetryHistoryStore> HistoryBySensor
    {
        get { lock (_gate) return new Dictionary<string, TelemetryHistoryStore>(_history, StringComparer.OrdinalIgnoreCase); }
    }

    public DeviceNode DeploymentTree { get; private set; } = new("Unseeded", "Facility");

    public void SeedAll()
    {
        lock (_gate)
        {
            if (_sensors.Count != 0)
                return;

            // Metadata may be seeded, but every reading must arrive through the HTTP ingestion API.
            var definitions = new (string Id, string Location, SensorCategory Category)[]
            {
                ("MOISTURE-B", "Hydroponic Bay 1 / Moisture Sensor B", SensorCategory.SoilMoisture),
                ("PUMP-A", "Hydroponic Bay 1 / Pump A smart plug", SensorCategory.PowerMeter),
                ("VALVE-A", "Hydroponic Bay 1 / Irrigation Valve A", SensorCategory.ValveState),
                ("TEMP-A", "Hydroponic Bay 1 / Temperature", SensorCategory.Temperature),
                ("HUMIDITY-A", "Hydroponic Bay 1 / Humidity", SensorCategory.Humidity),
                ("LIGHT-A", "Hydroponic Bay 1 / Grow Light", SensorCategory.LightLevel),
                ("MOISTURE-C", "Greenhouse Zone 3 / Moisture Sensor C", SensorCategory.SoilMoisture),
                ("PUMP-C", "Greenhouse Zone 3 / Pump C smart plug", SensorCategory.PowerMeter),
                ("VALVE-C", "Greenhouse Zone 3 / Irrigation Valve C", SensorCategory.ValveState),
                ("TEMP-C", "Greenhouse Zone 3 / Temperature", SensorCategory.Temperature),
                ("HUMIDITY-C", "Greenhouse Zone 3 / Humidity", SensorCategory.Humidity),
                ("LIGHT-C", "Greenhouse Zone 3 / Grow Light", SensorCategory.LightLevel),
                ("POWER-GRID", "Rooftop Grid Node A / Power Meter", SensorCategory.PowerMeter),
                ("VALVE-SHED", "Borehole Pump Shed / Water Valve", SensorCategory.ValveState)
            };
            for (var index = 0; index < definitions.Length; index++)
            {
                var definition = definitions[index];
                AddSensor(new SensorRegistration
                {
                    DeviceId = definition.Id,
                    MacAddress = $"02:53:58:00:00:{index + 1:X2}",
                    Location = definition.Location,
                    Category = definition.Category,
                    IsMock = true
                });
            }
            DeploymentTree = MockTelemetrySeeder.SeedDeploymentTree();
        }
    }

    public void AddSensor(SensorRegistration sensor)
    {
        ArgumentNullException.ThrowIfNull(sensor);
        if (string.IsNullOrWhiteSpace(sensor.DeviceId) || string.IsNullOrWhiteSpace(sensor.MacAddress)
            || string.IsNullOrWhiteSpace(sensor.Location) || !Enum.IsDefined(sensor.Category))
            throw new ArgumentException("Device id, MAC address, location and a valid category are required.");
        lock (_gate)
        {
            if (_sensors.ContainsKey(sensor.DeviceId))
                throw new ArgumentException($"Device '{sensor.DeviceId}' is already registered.");
            var mac = NormaliseMac(sensor.MacAddress);
            if (_deviceByMac.ContainsKey(mac))
                throw new ArgumentException($"MAC address '{sensor.MacAddress}' is already registered.");
            var copy = CopyRegistration(sensor);
            copy.MacAddress = sensor.MacAddress.Trim().ToUpperInvariant();
            _sensors.Add(copy.DeviceId, copy);
            _deviceByMac.Add(mac, copy.DeviceId);
            _history.Add(copy.DeviceId, new TelemetryHistoryStore());
        }
    }

    public void AddPacket(ITelemetryPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        lock (_gate)
        {
            _packets.Enqueue(packet);
            if (_packets.Count > PacketRetention)
                _packets.Dequeue();
            if (_history.TryGetValue(packet.DeviceId, out var history))
            {
                if (packet is TelemetryPacket<float> floatPacket)
                    history.AddReading(floatPacket.Value);
                else if (packet is TelemetryPacket<int> integerPacket)
                    history.AddReading(integerPacket.Value);
            }
        }
    }

    public List<ITelemetryPacket> GetRecent(int count)
    {
        lock (_gate)
            return _packets.OrderByDescending(packet => packet.TimestampUtc)
                .Take(Math.Clamp(count, 0, PacketRetention)).ToList();
    }

    public SensorRegistration? FindSensor(string deviceId)
    {
        lock (_gate)
            return _sensors.TryGetValue(deviceId, out var sensor) ? CopyRegistration(sensor) : null;
    }

    public void SetAttachment(string deviceId, string fileName)
    {
        lock (_gate)
        {
            if (!_sensors.TryGetValue(deviceId, out var sensor))
                throw new InvalidOperationException($"Device '{deviceId}' is not registered.");
            sensor.AttachmentFileName = fileName;
        }
    }

    public TelemetryHistoryStore? GetHistory(string deviceId)
    {
        lock (_gate)
            return _history.GetValueOrDefault(deviceId);
    }

    private static string NormaliseMac(string mac) => mac.Replace(":", "").Replace("-", "").Trim();

    private static SensorRegistration CopyRegistration(SensorRegistration sensor) => new()
    {
        DeviceId = sensor.DeviceId,
        MacAddress = sensor.MacAddress,
        Location = sensor.Location,
        Category = sensor.Category,
        RegisteredUtc = sensor.RegisteredUtc,
        AttachmentFileName = sensor.AttachmentFileName,
        IsMock = sensor.IsMock
    };
}
