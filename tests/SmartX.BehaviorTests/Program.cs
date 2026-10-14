using System.Diagnostics;
using SmartX.Core.Models;
using SmartX.Core.Services;

var tests = new (string Name, Action Run)[]
{
    ("Normal telemetry preserves FIFO processing across batches", Checks.Fifo),
    ("Critical telemetry bypasses waiting normal packets", Checks.CriticalBypass),
    ("Delayed normal telemetry cannot overwrite a newer critical state", Checks.LatestStateWins),
    ("Repeated critical alerts stay unique and recover", Checks.AlertRecovery),
    ("Power spike alerts process immediately and clear after recovery", Checks.PowerSpikeRecovery),
    ("Repeated disconnection checks stay unique and reconnect", Checks.DisconnectRecovery),
    ("Timeline keeps timestamp collisions and sorts delayed packets", Checks.TimestampCollisions),
    ("Device identifiers and MAC addresses cannot be registered twice", Checks.UniqueRegistration),
    ("Bulk command undo restores each valve's previous state", Checks.BulkUndo),
    ("Manual command undo follows LIFO order", Checks.CommandStack),
    ("Manual overrides and undo preserve desired actuator state", Checks.StickyOverride),
    ("Invalid packets never enter the processing queues", Checks.InvalidPackets),
    ("Unsupported commands leave device state and history intact", Checks.InvalidCommands),
    ("A saturated standard queue still accepts critical telemetry", Checks.QueueBounds),
    ("Telemetry and command histories stay bounded", Checks.HistoryBounds),
    ("Expired command history cannot remain secretly undoable", Checks.CommandChurn),
    ("Device search, category, and active alert filters combine", Checks.Filters),
    ("Repeated explicit searches teach a later suggested action", Checks.SearchLearning),
    ("Repeated manual commands teach a later suggested action", Checks.CommandLearning),
    ("Passive dashboard filtering does not teach recommendations", Checks.PassiveFiltering),
    ("Punctuation-only searches cannot train every actuator", Checks.PunctuationSearch),
    ("Related suggestions remain visible while filtering their source", Checks.FilteredSuggestions),
    ("Disconnected sources stop actionable and trend suggestions", Checks.DisconnectedSuggestions),
    ("Falling moisture produces an insight before the critical limit", Checks.TrendWarning),
    ("Flat readings do not produce an early trend warning", Checks.FlatTrend),
    ("Concurrent ingestion and snapshots preserve packet accounting", Checks.ConcurrentIngestion)
};

var failed = 0;
var elapsed = Stopwatch.StartNew();
foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception exception)
    {
        failed++;
        Console.Error.WriteLine($"FAIL {test.Name}\n{exception}");
    }
}

Console.WriteLine($"{tests.Length - failed}/{tests.Length} behavior checks passed; " +
                  $"{Checks.Assertions} assertions; {elapsed.Elapsed.TotalSeconds:0.00}s.");
return failed == 0 ? 0 : 1;

internal static class Checks
{
    private static int _assertions;
    public static int Assertions => _assertions;

    public static void Fifo()
    {
        var engine = Engine(("TempA", SensorCategory.Temperature));
        var packets = new[] { Temperature("TempA", 21), Temperature("TempA", 22), Temperature("TempA", 23) };
        foreach (var packet in packets) Require(engine.Enqueue(packet), "Normal packet was rejected.");
        Equal(3, engine.GetSnapshot().Metrics.StandardQueueDepth, "Queued packet count");
        Equal(0L, engine.GetSnapshot().Metrics.ProcessedPackets, "Normal packets must await processing");
        Equal(2, engine.ProcessPending(2), "First processing batch size");
        Equal(1, engine.GetSnapshot().Metrics.StandardQueueDepth, "Remaining queued packet count");
        Equal(22d, engine.GetTimeline("TempA").Last().NumericValue, "Latest value after first batch");
        Equal(1, engine.ProcessPending(2), "Second processing batch size");
        var history = engine.GetTimeline("TempA");
        SequenceEqual(packets.Select(packet => packet.PacketId), history.OrderBy(log => log.Sequence).Select(log => log.PacketId), "FIFO packet order");
        Require(history.Zip(history.Skip(1)).All(pair => pair.First.Sequence < pair.Second.Sequence), "Processing sequences must increase.");
        Equal(3L, engine.GetSnapshot().Metrics.ProcessedPackets, "Processed packet count");
    }

    public static void CriticalBypass()
    {
        var engine = Engine(("TempA", SensorCategory.Temperature), ("MoistureB", SensorCategory.SoilMoisture));
        var ordinary = Temperature("TempA", 24);
        var critical = Moisture(10);
        Require(engine.Enqueue(ordinary), "Normal packet was rejected.");
        Require(engine.Enqueue(critical), "Critical packet was rejected.");
        var snapshot = engine.GetSnapshot();
        Equal(1, snapshot.Metrics.StandardQueueDepth, "Waiting normal packets");
        Equal(1L, snapshot.Metrics.ProcessedPackets, "Critical packet must process immediately");
        Equal(1L, snapshot.Metrics.CriticalPackets, "Critical packet metric");
        Require(snapshot.RecentTelemetry.Single().IsCritical, "Only the critical packet should be visible before draining.");
        Equal(critical.PacketId, snapshot.RecentTelemetry.Single().PacketId, "Critical packet identity");
        Require(Device(engine, "MoistureB").ActiveAlerts.Length > 0, "Critical moisture should surface an active alert immediately.");
        engine.ProcessPending();
        Require(engine.GetTimeline("TempA").Single().Sequence > engine.GetTimeline("MoistureB").Single().Sequence,
            "Critical telemetry must process before the earlier normal packet.");
    }

    public static void AlertRecovery()
    {
        var engine = Engine(("MoistureB", SensorCategory.SoilMoisture));
        for (var index = 0; index < 5; index++) Require(engine.Enqueue(Moisture(10)), "Critical packet rejected.");
        Equal(1, engine.GetSnapshot().Metrics.UniqueActiveAlerts, "Duplicate critical alerts");
        Equal(1, Device(engine, "MoistureB").ActiveAlerts.Length, "Device active alert count");
        Equal(5L, engine.GetSnapshot().Metrics.CriticalPackets, "All packets still count despite alert deduplication");
        RecoverMoisture(engine);
        Equal(0, engine.GetSnapshot().Metrics.UniqueActiveAlerts, "Recovered alert count");
        Require(Device(engine, "MoistureB").ActiveAlerts.Length == 0, "Recovered moisture should clear its alert.");
    }

    public static void LatestStateWins()
    {
        var engine = Engine(("MoistureB", SensorCategory.SoilMoisture));
        var timestamp = DateTime.UtcNow.AddMinutes(-1);
        var ordinary = Moisture(60, timestamp);
        var critical = Moisture(10, timestamp.AddSeconds(1));
        Require(engine.Enqueue(ordinary), "Normal packet rejected.");
        Require(engine.Enqueue(critical), "Critical packet rejected.");
        Require(Device(engine, "MoistureB").ActiveAlerts.Contains("CriticalMoisture"), "Critical packet should immediately establish its alert.");
        Drain(engine);
        var device = Device(engine, "MoistureB");
        Equal(critical.TimestampUtc, device.LastSeenUtc, "Delayed processing must preserve the latest observation timestamp");
        Equal(critical.FormattedValue, device.Value, "Older normal telemetry must not replace the newer critical value");
        Require(device.ActiveAlerts.Contains("CriticalMoisture"), "Older healthy telemetry must not clear a newer critical alert.");
        SequenceEqual(new[] { ordinary.PacketId, critical.PacketId }, engine.GetTimeline("MoistureB").Select(log => log.PacketId),
            "Both observations must remain available in chronological history");
    }

    public static void DisconnectRecovery()
    {
        var engine = Engine(("TempA", SensorCategory.Temperature));
        Require(engine.Enqueue(Temperature("TempA", 24)), "Normal packet rejected.");
        engine.ProcessPending();
        var lastSeen = Device(engine, "TempA").LastSeenUtc!.Value;
        engine.MarkDisconnected(lastSeen.AddSeconds(31), TimeSpan.FromSeconds(30));
        engine.MarkDisconnected(lastSeen.AddSeconds(32), TimeSpan.FromSeconds(30));
        Require(!Device(engine, "TempA").IsConnected, "Timed-out device should disconnect.");
        Equal(1, engine.GetSnapshot().Metrics.UniqueActiveAlerts, "Repeated disconnect alert count");
        Require(engine.Enqueue(Temperature("TempA", 25)), "Reconnection telemetry rejected.");
        engine.ProcessPending();
        Require(Device(engine, "TempA").IsConnected, "Fresh telemetry should reconnect the device.");
        Equal(0, engine.GetSnapshot().Metrics.UniqueActiveAlerts, "Reconnection clears disconnect alert");
    }

    public static void PowerSpikeRecovery()
    {
        var engine = Engine(("RelayA", SensorCategory.PowerMeter));
        Require(engine.Enqueue(new TelemetryPacket<int>("RelayA", SensorCategory.PowerMeter, 2500, "W")), "Power spike packet rejected.");
        Require(engine.Enqueue(new TelemetryPacket<int>("RelayA", SensorCategory.PowerMeter, 2600, "W")), "Second power spike packet rejected.");
        Equal(0, engine.GetSnapshot().Metrics.StandardQueueDepth, "Power spikes must bypass the normal queue");
        Equal(2L, engine.GetSnapshot().Metrics.CriticalPackets, "Power spike critical packet count");
        Equal(1, engine.GetSnapshot().Metrics.UniqueActiveAlerts, "Power spike alert deduplication");
        Require(Device(engine, "RelayA").ActiveAlerts.Contains("PowerSpike"), "Power spike should have its specific active alert.");
        Require(engine.Enqueue(new TelemetryPacket<int>("RelayA", SensorCategory.PowerMeter, 400, "W")), "Recovered power packet rejected.");
        Drain(engine);
        Equal(0, engine.GetSnapshot().Metrics.UniqueActiveAlerts, "Recovered power clears its alert");
    }

    public static void TimestampCollisions()
    {
        var engine = Engine(("TempA", SensorCategory.Temperature));
        var timestamp = DateTime.UtcNow.AddMinutes(-1);
        var first = Temperature("TempA", 21, timestamp);
        var second = Temperature("TempA", 22, timestamp);
        var delayed = Temperature("TempA", 20, timestamp.AddSeconds(-1));
        var latest = Temperature("TempA", 23, timestamp.AddSeconds(1));
        foreach (var packet in new[] { first, second, delayed, latest }) Require(engine.Enqueue(packet), "Packet rejected.");
        Drain(engine);
        var history = engine.GetTimeline("TempA");
        Equal(4, history.Count, "Collision-safe timeline length");
        SequenceEqual(new[] { delayed.PacketId, first.PacketId, second.PacketId, latest.PacketId }, history.Select(log => log.PacketId), "Chronological timeline with arrival order ties");
        Equal(4, history.Select(log => log.Sequence).Distinct().Count(), "Unique timeline sequence count");
        SequenceEqual(new[] { second.PacketId, latest.PacketId }, engine.GetTimeline("TempA", 2).Select(log => log.PacketId), "Newest requested timeline window");
    }

    public static void UniqueRegistration()
    {
        var store = new TelemetryStore();
        store.AddSensor(Sensor("TempA", SensorCategory.Temperature, "AA:BB:CC:00:00:01"));
        Throws<ArgumentException>(() => store.AddSensor(Sensor("TempA", SensorCategory.Humidity, "AA:BB:CC:00:00:02")));
        Throws<ArgumentException>(() => store.AddSensor(Sensor("tempa", SensorCategory.Humidity, "AA:BB:CC:00:00:03")));
        Throws<ArgumentException>(() => store.AddSensor(Sensor("TempB", SensorCategory.Temperature, "AA:BB:CC:00:00:01")));
        Throws<ArgumentException>(() => store.AddSensor(Sensor("TempC", SensorCategory.Temperature, "aa:bb:cc:00:00:01")));
        Throws<ArgumentException>(() => store.AddSensor(Sensor("InvalidCategory", (SensorCategory)99, "AA:BB:CC:00:00:04")));
        Equal(1, store.Sensors.Count, "Rejected registrations must not modify the registry");
        Equal("TempA", store.FindSensor("TempA")!.DeviceId, "Dictionary lookup preserves existing device");
    }

    public static void BulkUndo()
    {
        var engine = Engine(("ValveA", SensorCategory.ValveState), ("ValveB", SensorCategory.ValveState), ("ValveC", SensorCategory.ValveState));
        SetValve(engine, "ValveA", true);
        SetValve(engine, "ValveB", false);
        SetValve(engine, "ValveC", true);
        var command = engine.ExecuteCommand("*", "AllValvesOff");
        Equal(3, command.AffectedDeviceIds.Length, "Bulk command affected device count");
        Require(engine.GetSnapshot().Devices.All(device => !device.IsOn), "All valves should turn off.");
        var undo = engine.UndoCommand();
        Equal(command.CommandId, undo.CommandId, "Bulk undo command identity");
        Require(undo.IsUndone, "Undo should mark its history entry.");
        Require(Device(engine, "ValveA").IsOn, "Valve A's prior open state must return.");
        Require(!Device(engine, "ValveB").IsOn, "Valve B's prior closed state must remain.");
        Require(Device(engine, "ValveC").IsOn, "Valve C's prior open state must return.");
        Equal(1, engine.GetSnapshot().Commands.Count, "Undo must update rather than duplicate the history entry");
        Throws<InvalidOperationException>(() => engine.UndoCommand());
    }

    public static void CommandStack()
    {
        var engine = Engine(("PumpA", SensorCategory.ValveState), ("RelayA", SensorCategory.PowerMeter));
        var first = engine.ExecuteCommand("PumpA", "TurnOn");
        var second = engine.ExecuteCommand("RelayA", "TurnOn");
        Equal(second.CommandId, engine.UndoCommand().CommandId, "First undo must select the latest command");
        Require(Device(engine, "PumpA").IsOn, "Undoing relay should preserve pump state.");
        Require(!Device(engine, "RelayA").IsOn, "Relay should return to its prior off state.");
        Equal(first.CommandId, engine.UndoCommand().CommandId, "Second undo must select the earlier command");
        Require(!Device(engine, "PumpA").IsOn, "Pump should return to its prior off state.");
        Require(engine.GetSnapshot().Commands.All(command => command.IsUndone), "Both history entries should be marked undone.");
    }

    public static void InvalidPackets()
    {
        var engine = Engine(("MoistureB", SensorCategory.SoilMoisture), ("ValveA", SensorCategory.ValveState), ("TempA", SensorCategory.Temperature));
        Throws<ArgumentException>(() => engine.Enqueue(Temperature("MissingDevice", 25)));
        Throws<ArgumentException>(() => engine.Enqueue(new TelemetryPacket<float>("TempA", SensorCategory.Humidity, 50, "%")));
        Throws<ArgumentException>(() => engine.Enqueue(new TelemetryPacket<float>("ValveA", SensorCategory.ValveState, 1, "")));
        Throws<ArgumentException>(() => engine.Enqueue(new TelemetryPacket<bool>("TempA", SensorCategory.Temperature, true, "°C")));
        Throws<ArgumentException>(() => engine.Enqueue(new TelemetryPacket<double>("TempA", SensorCategory.Temperature, 25d, "°C")));
        Throws<ArgumentException>(() => engine.Enqueue(Moisture(-1)));
        Throws<ArgumentException>(() => engine.Enqueue(Moisture(101)));
        Throws<ArgumentException>(() => engine.Enqueue(Temperature("TempA", float.NaN)));
        Throws<ArgumentException>(() => engine.Enqueue(Temperature("TempA", float.PositiveInfinity)));
        var metrics = engine.GetSnapshot().Metrics;
        Equal(0, metrics.StandardQueueDepth, "Invalid packet standard queue depth");
        Equal(0L, metrics.ProcessedPackets, "Invalid packet processed count");
        Equal(0, engine.GetSnapshot().RecentTelemetry.Count, "Invalid packets must not appear in telemetry history");
    }

    public static void StickyOverride()
    {
        var engine = Engine(("PumpA", SensorCategory.ValveState), ("RelayA", SensorCategory.PowerMeter));
        SetValve(engine, "PumpA", false);
        var command = engine.ExecuteCommand("PumpA", "TurnOn");
        SetValve(engine, "PumpA", false);
        Require(Device(engine, "PumpA").IsOn, "Reported closed valve must not erase the desired TurnOn override.");
        Equal("CLOSED", Device(engine, "PumpA").Value, "Reported valve value should still update");
        Equal(command.CommandId, engine.UndoCommand().CommandId, "Undo should select the desired valve override");
        SetValve(engine, "PumpA", true);
        Require(!Device(engine, "PumpA").IsOn, "Reported open valve must not erase the desired state restored by undo.");
        Equal("OPEN", Device(engine, "PumpA").Value, "Reported valve value after undo should still update");

        Require(engine.Enqueue(new TelemetryPacket<int>("RelayA", SensorCategory.PowerMeter, 400, "W")), "Initial smart plug packet rejected.");
        Drain(engine);
        Require(Device(engine, "RelayA").IsOn, "Before an override, positive power telemetry should establish the plug state.");
        engine.ExecuteCommand("RelayA", "TurnOff");
        Require(engine.Enqueue(new TelemetryPacket<int>("RelayA", SensorCategory.PowerMeter, 400, "W")), "Reported smart plug packet rejected.");
        Drain(engine);
        Require(!Device(engine, "RelayA").IsOn, "Reported positive power must not erase the desired TurnOff override.");
        Equal("400", Device(engine, "RelayA").Value, "Reported smart plug value should still update");
        engine.UndoCommand();
        Require(engine.Enqueue(new TelemetryPacket<int>("RelayA", SensorCategory.PowerMeter, 0, "W")), "Reported off smart plug packet rejected.");
        Drain(engine);
        Require(Device(engine, "RelayA").IsOn, "Reported zero power must not erase the desired state restored by undo.");
        Equal("0", Device(engine, "RelayA").Value, "Reported smart plug value after undo should still update");
    }

    public static void InvalidCommands()
    {
        var engine = Engine(("PumpA", SensorCategory.ValveState), ("TempA", SensorCategory.Temperature));
        Throws<ArgumentException>(() => engine.ExecuteCommand("PumpA", "Reboot"));
        Throws<ArgumentException>(() => engine.ExecuteCommand("TempA", "TurnOn"));
        Throws<InvalidOperationException>(() => engine.ExecuteCommand("MissingDevice", "TurnOn"));
        Equal(0, engine.GetSnapshot().Commands.Count, "Rejected commands must not enter history");
        Require(!Device(engine, "PumpA").IsOn, "Rejected commands must preserve pump state.");
    }

    public static void QueueBounds()
    {
        var engine = Engine(("TempA", SensorCategory.Temperature), ("MoistureB", SensorCategory.SoilMoisture));
        for (var index = 0; index < 4096; index++) Require(engine.Enqueue(Temperature("TempA", 25)), "Queue rejected a packet before reaching capacity.");
        Require(!engine.Enqueue(Temperature("TempA", 26)), "Queue must report overflow rather than grow without bound.");
        Require(engine.Enqueue(Moisture(10)), "Critical packet must bypass a saturated standard queue.");
        var metrics = engine.GetSnapshot().Metrics;
        Equal(4096, metrics.StandardQueueDepth, "Bounded standard queue depth");
        Equal(1L, metrics.DroppedPackets, "Overflow packet metric");
        Equal(1L, metrics.ProcessedPackets, "Critical packet immediate processing at saturation");
        Drain(engine);
        Equal(4097L, engine.GetSnapshot().Metrics.ProcessedPackets, "Accepted packet total after draining");
        Equal(1000, engine.GetTimeline("TempA", 2000).Count, "Bounded per-device telemetry timeline");
    }

    public static void HistoryBounds()
    {
        var engine = Engine(("TempA", SensorCategory.Temperature), ("PumpA", SensorCategory.ValveState));
        for (var index = 0; index < 1005; index++) Require(engine.Enqueue(Temperature("TempA", 25)), "Normal packet rejected.");
        Drain(engine);
        var history = engine.GetTimeline("TempA", 2000);
        Equal(1000, history.Count, "Per-device history capacity");
        Equal(500, engine.GetSnapshot().RecentTelemetry.Count, "Dashboard recent telemetry capacity");
        Equal(999L, history.Last().Sequence - history.First().Sequence, "Retained history keeps the newest consecutive records");
        CommandRecord? last = null;
        for (var index = 0; index < 1005; index++) last = engine.ExecuteCommand("PumpA", index % 2 == 0 ? "TurnOn" : "TurnOff");
        Equal(1000, engine.GetSnapshot().Commands.Count, "Command history capacity");
        Equal(last!.CommandId, engine.UndoCommand().CommandId, "Bounded stack must retain its newest command");
    }

    public static void Filters()
    {
        var engine = Engine(("MoistureB", SensorCategory.SoilMoisture), ("PumpA", SensorCategory.ValveState), ("TempA", SensorCategory.Temperature));
        Require(engine.Enqueue(Moisture(10)), "Critical packet rejected.");
        Equal("PumpA", engine.GetSnapshot("pump").Devices.Single().DeviceId, "Case-insensitive device search");
        Equal("TempA", engine.GetSnapshot(category: SensorCategory.Temperature).Devices.Single().DeviceId, "Category filter");
        Equal("MoistureB", engine.GetSnapshot(alertsOnly: true).Devices.Single().DeviceId, "Active alert filter");
        Equal(0, engine.GetSnapshot("pump", SensorCategory.ValveState, true).Devices.Count, "Combined filters exclude unalerted pump");
        Equal("MoistureB", engine.GetSnapshot("moisture", SensorCategory.SoilMoisture, true).Devices.Single().DeviceId, "Combined filters select alerted source");
    }

    public static void CommandChurn()
    {
        var engine = Engine(("PumpA", SensorCategory.ValveState));
        var oldest = engine.ExecuteCommand("PumpA", "TurnOn");
        for (var index = 0; index < 1000; index++)
        {
            engine.ExecuteCommand("PumpA", "TurnOff");
            engine.UndoCommand();
        }
        var history = engine.GetSnapshot().Commands;
        Equal(1000, history.Count, "Command history stays bounded after execute/undo churn");
        Require(!history.Any(command => command.CommandId == oldest.CommandId), "The oldest command should have expired from history.");
        Require(history.All(command => command.IsUndone), "All visible commands were already undone.");
        Throws<InvalidOperationException>(() => engine.UndoCommand());
        Require(Device(engine, "PumpA").IsOn, "Expired commands must not invisibly alter desired actuator state.");
    }

    public static void SearchLearning()
    {
        var engine = Engine(("MoistureB", SensorCategory.SoilMoisture), ("PumpA", SensorCategory.ValveState));
        for (var occurrence = 0; occurrence < 3; occurrence++)
        {
            Require(engine.Enqueue(Moisture(10)), "Critical packet rejected.");
            engine.RecordSearch("PumpA");
            RecoverMoisture(engine);
        }
        Require(!engine.GetSnapshot().SuggestedActions.Any(action => action.DeviceId == "PumpA" && action.Action == "TurnOn"),
            "Learned critical response should not be suggested after the source recovers.");
        Require(engine.Enqueue(Moisture(10)), "New critical observation rejected.");
        var suggestion = engine.GetSnapshot().SuggestedActions.Single(action => action.DeviceId == "PumpA" && action.Action == "TurnOn");
        Require(suggestion.EvidenceCount >= 3, "Learned suggestion must report at least three observations.");
        Require(suggestion.Confidence > 0 && suggestion.Confidence <= 1, "Learned suggestion confidence must be a valid probability.");
        Require(!Device(engine, "PumpA").IsOn, "A suggested action must await an operator command.");
    }

    public static void CommandLearning()
    {
        var engine = Engine(("MoistureB", SensorCategory.SoilMoisture), ("PumpA", SensorCategory.ValveState));
        for (var occurrence = 0; occurrence < 3; occurrence++)
        {
            SetValve(engine, "PumpA", false);
            Require(engine.Enqueue(Moisture(10)), "Critical packet rejected.");
            engine.ExecuteCommand("PumpA", "TurnOn");
            RecoverMoisture(engine);
            engine.UndoCommand();
        }
        SetValve(engine, "PumpA", false);
        Require(engine.Enqueue(Moisture(10)), "New critical observation rejected.");
        var suggestion = engine.GetSnapshot().SuggestedActions.Single(action => action.DeviceId == "PumpA" && action.Action == "TurnOn");
        Require(suggestion.EvidenceCount >= 3, "Repeated manual commands should teach a response.");
        Require(!Device(engine, "PumpA").IsOn, "Learned response must remain a suggestion.");
    }

    public static void PassiveFiltering()
    {
        var engine = Engine(("MoistureB", SensorCategory.SoilMoisture), ("PumpA", SensorCategory.ValveState));
        Require(engine.Enqueue(Moisture(10)), "Critical packet rejected.");
        for (var index = 0; index < 10; index++) engine.GetSnapshot("PumpA");
        Require(!engine.GetSnapshot().SuggestedActions.Any(action => action.DeviceId == "PumpA" && action.EvidenceCount >= 3),
            "Dashboard refreshes and passive filters must not become learning observations.");
    }

    public static void PunctuationSearch()
    {
        var engine = Engine(("MoistureB", SensorCategory.SoilMoisture), ("PumpA", SensorCategory.ValveState));
        for (var occurrence = 0; occurrence < 3; occurrence++)
        {
            Require(engine.Enqueue(Moisture(10)), "Critical packet rejected.");
            engine.RecordSearch("!!!?");
            Equal(0, engine.GetSnapshot("!!!?").Devices.Count, "Punctuation should not match every device after search normalization");
            RecoverMoisture(engine);
        }
        Require(engine.Enqueue(Moisture(10)), "New critical observation rejected.");
        Require(!engine.GetSnapshot().SuggestedActions.Any(action => action.DeviceId == "PumpA" && action.EvidenceCount >= 3),
            "Punctuation-only searches must not teach a spurious action for every actuator.");
    }

    public static void FilteredSuggestions()
    {
        var engine = Engine(("MoistureB", SensorCategory.SoilMoisture), ("PumpA", SensorCategory.ValveState));
        TeachPumpSearch(engine);
        Require(engine.Enqueue(Moisture(10)), "New critical observation rejected.");
        var snapshot = engine.GetSnapshot("MoistureB", SensorCategory.SoilMoisture, alertsOnly: true);
        Equal("MoistureB", snapshot.Devices.Single().DeviceId, "Source filters should still narrow the device list");
        Require(snapshot.SuggestedActions.Any(action => action.DeviceId == "PumpA" && action.Action == "TurnOn" && action.EvidenceCount >= 3),
            "A related healthy actuator's suggestion must remain visible while the operator inspects the source alert.");
    }

    public static void DisconnectedSuggestions()
    {
        var engine = Engine(("MoistureB", SensorCategory.SoilMoisture), ("PumpA", SensorCategory.ValveState));
        TeachPumpSearch(engine);
        Require(engine.Enqueue(Moisture(10)), "New critical observation rejected.");
        Require(engine.GetSnapshot().SuggestedActions.Any(action => action.DeviceId == "PumpA" && action.Action == "TurnOn"),
            "Connected source should initially offer its learned response.");
        engine.MarkDisconnected(DateTime.UtcNow.AddSeconds(31), TimeSpan.FromSeconds(30));
        SetValve(engine, "PumpA", false);
        Require(Device(engine, "PumpA").IsConnected && !Device(engine, "MoistureB").IsConnected,
            "Target should reconnect independently while source remains disconnected.");
        Require(!engine.GetSnapshot().SuggestedActions.Any(action => action.DeviceId == "PumpA" && action.Action == "TurnOn"),
            "A connected target must not receive an actionable recommendation from a disconnected source.");
        Require(engine.Enqueue(Moisture(10)), "Source reconnection packet rejected.");
        Require(engine.GetSnapshot().SuggestedActions.Any(action => action.DeviceId == "PumpA" && action.Action == "TurnOn"),
            "Fresh source telemetry should restore a still-applicable learned suggestion.");

        var trendEngine = Engine(("MoistureB", SensorCategory.SoilMoisture));
        var timestamp = DateTime.UtcNow.AddMinutes(-1);
        var samples = new[] { 50f, 42f, 34f, 26f };
        for (var index = 0; index < samples.Length; index++)
        {
            Require(trendEngine.Enqueue(Moisture(samples[index], timestamp.AddSeconds(index))), "Trend sample rejected.");
            Drain(trendEngine);
        }
        Require(trendEngine.GetSnapshot().SuggestedActions.Any(action => action.Id.StartsWith("trend:", StringComparison.Ordinal)),
            "Connected falling source should initially offer its trend warning.");
        trendEngine.MarkDisconnected(DateTime.UtcNow.AddSeconds(31), TimeSpan.FromSeconds(30));
        Require(!trendEngine.GetSnapshot().SuggestedActions.Any(action => action.Id.StartsWith("trend:", StringComparison.Ordinal)),
            "Disconnected source must stop extrapolating old readings into a live trend warning.");
    }

    public static void TrendWarning()
    {
        var engine = Engine(("MoistureB", SensorCategory.SoilMoisture));
        var samples = new[] { 50f, 42f, 34f, 26f };
        var timestamp = DateTime.UtcNow.AddMinutes(-1);
        for (var index = 0; index < samples.Length; index++)
        {
            Require(engine.Enqueue(Moisture(samples[index], timestamp.AddSeconds(index))), "Normal moisture packet rejected.");
            engine.ProcessPending();
        }
        Equal(0L, engine.GetSnapshot().Metrics.CriticalPackets, "Trend should be detected before a critical packet");
        var suggestions = engine.GetSnapshot().SuggestedActions;
        Require(suggestions.Any(action => action.DeviceId == "MoistureB" && action.Title == "Moisture falling toward critical level"),
            "Falling moisture should highlight the source before crossing the critical threshold.");
    }

    public static void FlatTrend()
    {
        var engine = Engine(("MoistureB", SensorCategory.SoilMoisture));
        var timestamp = DateTime.UtcNow.AddMinutes(-1);
        for (var index = 0; index < 4; index++)
        {
            Require(engine.Enqueue(Moisture(50, timestamp.AddSeconds(index))), "Normal moisture packet rejected.");
            engine.ProcessPending();
        }
        Require(!engine.GetSnapshot().SuggestedActions.Any(action => action.Title == "Moisture falling toward critical level"),
            "A stable healthy sensor must not report a falling-moisture trend.");
    }

    public static void ConcurrentIngestion()
    {
        var engine = Engine(("TempA", SensorCategory.Temperature), ("MoistureB", SensorCategory.SoilMoisture));
        var producers = Enumerable.Range(0, 4).Select(producer => Task.Run(() =>
        {
            for (var index = 0; index < 300; index++) Require(engine.Enqueue(Temperature("TempA", 20 + producer)), "Concurrent normal packet rejected.");
        })).ToList();
        producers.Add(Task.Run(() =>
        {
            for (var index = 0; index < 100; index++) Require(engine.Enqueue(Moisture(10)), "Concurrent critical packet rejected.");
        }));
        producers.Add(Task.Run(() =>
        {
            for (var index = 0; index < 100; index++) engine.GetSnapshot();
        }));
        Task.WhenAll(producers).GetAwaiter().GetResult();
        Drain(engine);
        var metrics = engine.GetSnapshot().Metrics;
        Equal(1300L, metrics.ProcessedPackets, "Concurrent accepted packet accounting");
        Equal(100L, metrics.CriticalPackets, "Concurrent critical packet accounting");
        Equal(0L, metrics.DroppedPackets, "Concurrent packets must not be lost below capacity");
        Equal(1, metrics.UniqueActiveAlerts, "Concurrent critical alerts remain unique");
    }

    private static RealTimeEngine Engine(params (string Id, SensorCategory Category)[] devices)
    {
        var store = new TelemetryStore();
        for (var index = 0; index < devices.Length; index++)
            store.AddSensor(Sensor(devices[index].Id, devices[index].Category, $"AA:BB:CC:00:00:{index + 1:X2}"));
        return new RealTimeEngine(store);
    }

    private static SensorRegistration Sensor(string id, SensorCategory category, string mac) => new()
    {
        DeviceId = id,
        Category = category,
        MacAddress = mac,
        Location = $"Test room {id}"
    };

    private static TelemetryPacket<float> Temperature(string id, float value, DateTime? timestamp = null) => new(id, SensorCategory.Temperature, value, "°C", timestamp);
    private static TelemetryPacket<float> Moisture(float value, DateTime? timestamp = null) => new("MoistureB", SensorCategory.SoilMoisture, value, "%", timestamp);
    private static DeviceSnapshot Device(RealTimeEngine engine, string id) => engine.GetSnapshot().Devices.Single(device => device.DeviceId == id);

    private static void RecoverMoisture(RealTimeEngine engine)
    {
        Require(engine.Enqueue(Moisture(60)), "Recovery packet rejected.");
        Drain(engine);
    }

    private static void TeachPumpSearch(RealTimeEngine engine)
    {
        for (var occurrence = 0; occurrence < 3; occurrence++)
        {
            Require(engine.Enqueue(Moisture(10)), "Critical packet rejected.");
            engine.RecordSearch("PumpA");
            RecoverMoisture(engine);
        }
    }

    private static void SetValve(RealTimeEngine engine, string id, bool isOn)
    {
        Require(engine.Enqueue(new TelemetryPacket<bool>(id, SensorCategory.ValveState, isOn, "")), "Valve state packet rejected.");
        Drain(engine);
    }

    private static void Drain(RealTimeEngine engine)
    {
        while (engine.GetSnapshot().Metrics.StandardQueueDepth > 0)
            Require(engine.ProcessPending(128) > 0, "Pending queue failed to make progress.");
    }

    private static void Require(bool condition, string message)
    {
        Interlocked.Increment(ref _assertions);
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        Interlocked.Increment(ref _assertions);
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message}: expected {expected}, actual {actual}.");
    }

    private static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual, string message)
    {
        Interlocked.Increment(ref _assertions);
        if (!expected.SequenceEqual(actual)) throw new InvalidOperationException($"{message}: sequence differs.");
    }

    private static void Throws<TException>(Action action) where TException : Exception
    {
        Interlocked.Increment(ref _assertions);
        try { action(); }
        catch (TException) { return; }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}, but no exception was thrown.");
    }
}
