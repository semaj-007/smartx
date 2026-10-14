using System.Globalization;
using SmartX.Core.Models;

namespace SmartX.Core.Services;

/// <summary>
/// In-memory real-time processor. One short lock protects compound state transitions,
/// and every public read returns snapshots so asynchronous UI readers never enumerate
/// collections being changed by a device or the background queue processor.
/// </summary>
public sealed class RealTimeEngine
{
    public const int MaximumQueueDepth = 4096;
    public const int MaximumTimelineEntries = 1000;
    public const int MaximumCommandHistory = 1000;
    private const int MaximumRecentTelemetry = 500;
    private const int MaximumLearnedPatterns = 1024;
    private static readonly TimeSpan LearningWindow = TimeSpan.FromMinutes(2);
    private readonly object _gate = new();
    private readonly TelemetryStore _store;

    // Queue<T> guarantees FIFO for normal telemetry. Critical packets have a separate
    // priority queue, ordered by severity then arrival, and bypass the normal backlog.
    private readonly Queue<ITelemetryPacket> _standardQueue = new();
    private readonly PriorityQueue<ITelemetryPacket, (int Severity, long Arrival)> _criticalQueue = new();
    private readonly Dictionary<string, Queue<long>> _pendingArrivalOrder = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DeviceState> _devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<TelemetryLog> _recentTelemetry = new();

    // The HashSet holds unique device/error pairs. Repeated disconnect or threshold
    // alerts are idempotent, and recovery removes the corresponding pair.
    private readonly HashSet<(string DeviceId, string Code)> _activeAlerts = new();
    private Stack<CommandEntry> _undoHistory = new();
    private readonly List<CommandEntry> _commandHistory = new();

    // A bounded, weighted association graph learns source-alert -> operator action.
    // Evidence is counted by distinct threshold episodes, never dashboard polling.
    private readonly Dictionary<string, CriticalContext> _criticalContexts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<PatternKey, PatternEvidence> _patterns = new();
    private readonly Queue<(DateTime Utc, string Query)> _searchHistory = new();
    private long _arrivalSequence;
    private long _processedPackets;
    private long _criticalPackets;
    private long _droppedPackets;
    private long _criticalEpisodeSequence;

    public RealTimeEngine(TelemetryStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        foreach (var registration in store.Sensors)
            _devices.Add(registration.DeviceId, new DeviceState(registration));
    }

    /// <summary>Registers metadata only. No synthetic reading enters the processor here.</summary>
    public void RegisterDevice(SensorRegistration sensor)
    {
        ArgumentNullException.ThrowIfNull(sensor);
        lock (_gate)
        {
            // The store rejects duplicate ids/MACs before any engine state is changed.
            _store.AddSensor(sensor);
            _devices.Add(sensor.DeviceId, new DeviceState(_store.FindSensor(sensor.DeviceId)!));
        }
    }

    /// <returns>False when the bounded normal queue is full. Critical alerts always bypass it.</returns>
    public bool Enqueue(ITelemetryPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        lock (_gate)
        {
            var numericValue = ValidatePacket(packet);
            var critical = IsCritical(packet.Category, numericValue);
            if (!critical && _standardQueue.Count >= MaximumQueueDepth)
            {
                _droppedPackets++;
                return false;
            }
            var arrival = ++_arrivalSequence;
            if (!_pendingArrivalOrder.TryGetValue(packet.PacketId, out var arrivals))
                _pendingArrivalOrder.Add(packet.PacketId, arrivals = new Queue<long>());
            arrivals.Enqueue(arrival);

            if (critical)
            {
                var severity = packet.Category == SensorCategory.PowerMeter && numericValue >= 3000
                    || packet.Category == SensorCategory.SoilMoisture && numericValue < 10 ? 0 : 1;
                _criticalQueue.Enqueue(packet, (severity, arrival));
                // Critical handling completes inside the ingest call rather than waiting
                // for the next background tick or any earlier standard packet.
                while (_criticalQueue.TryDequeue(out var next, out _))
                    ProcessPacket(next, true);
            }
            else
                _standardQueue.Enqueue(packet);
            return true;
        }
    }

    public int ProcessPending(int max = 128)
    {
        if (max < 1)
            throw new ArgumentOutOfRangeException(nameof(max), "The batch size must be positive.");
        lock (_gate)
        {
            var processed = 0;
            while (processed < max && _criticalQueue.TryDequeue(out var critical, out _))
            {
                ProcessPacket(critical, true);
                processed++;
            }
            while (processed < max && _standardQueue.TryDequeue(out var standard))
            {
                ProcessPacket(standard, false);
                processed++;
            }
            return processed;
        }
    }

    public void MarkDisconnected(DateTime utcNow, TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));
        lock (_gate)
        {
            foreach (var device in _devices.Values)
            {
                if (utcNow - (device.LastReceivedUtc ?? device.Registration.RegisteredUtc) <= timeout)
                    continue;
                device.IsConnected = false;
                SetAlert(device, "Disconnected", true);
            }
        }
    }

    public DashboardSnapshot GetSnapshot(string query = "", SensorCategory? category = null, bool alertsOnly = false)
    {
        query = ValidateQuery(query);
        if (category.HasValue && !Enum.IsDefined(category.Value))
            throw new ArgumentException("Unknown sensor category.", nameof(category));
        lock (_gate)
        {
            var devices = _devices.Values
                .Where(device => (!category.HasValue || device.Registration.Category == category.Value)
                    && (!alertsOnly || device.Alerts.Count > 0) && MatchesQuery(device, query))
                .OrderBy(device => device.Registration.DeviceId, StringComparer.OrdinalIgnoreCase)
                .Select(ToSnapshot).ToArray();
            var ids = devices.Select(device => device.DeviceId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return new DashboardSnapshot(
                devices,
                _recentTelemetry.Reverse().Where(log => ids.Contains(log.DeviceId)).ToArray(),
                _commandHistory.AsEnumerable().Reverse().Select(entry => CopyCommand(entry.Record)).ToArray(),
                // Insights remain global: an alerts-only moisture view must still
                // offer the related healthy pump learned from operator actions.
                BuildSuggestions(),
                new StreamMetrics(_standardQueue.Count, _criticalQueue.Count, _processedPackets,
                    _criticalPackets, _activeAlerts.Count, _droppedPackets, _devices.Count));
        }
    }

    /// <summary>Call only for an explicit operator search; querying snapshots never learns.</summary>
    public void RecordSearch(string query)
    {
        query = ValidateQuery(query);
        if (query.Length == 0)
            return;
        lock (_gate)
        {
            var now = DateTime.UtcNow;
            _searchHistory.Enqueue((now, query));
            if (_searchHistory.Count > 256)
                _searchHistory.Dequeue();
            foreach (var target in _devices.Values.Where(device => MatchesQuery(device, query)))
            {
                if (!IsActuator(target))
                    continue;
                Learn(target.Registration.DeviceId, null, now, fromSearch: true);
            }
        }
    }

    public CommandRecord ExecuteCommand(string deviceId, string action)
    {
        if (string.IsNullOrWhiteSpace(deviceId) || string.IsNullOrWhiteSpace(action))
            throw new ArgumentException("A target device and command action are required.");
        action = action.Trim().ToUpperInvariant() switch
        {
            "TURNON" => "TurnOn",
            "TURNOFF" => "TurnOff",
            "ALLVALVESOFF" => "AllValvesOff",
            _ => throw new ArgumentException("Action must be TurnOn, TurnOff or AllValvesOff.", nameof(action))
        };
        lock (_gate)
        {
            DeviceState[] targets;
            if (action == "AllValvesOff")
            {
                if (deviceId != "*")
                    throw new ArgumentException("AllValvesOff requires the '*' target.", nameof(deviceId));
                targets = _devices.Values.Where(device => device.Registration.Category == SensorCategory.ValveState).ToArray();
                if (targets.Length == 0)
                    throw new InvalidOperationException("No valves are registered.");
            }
            else
            {
                if (!_devices.TryGetValue(deviceId, out var target))
                    throw new InvalidOperationException($"Device '{deviceId}' is not registered.");
                if (!IsActuator(target))
                    throw new ArgumentException("Manual overrides are supported for valves and power-meter smart plugs only.");
                deviceId = target.Registration.DeviceId;
                targets = new[] { target };
            }

            var previous = targets.ToDictionary(target => target.Registration.DeviceId, target => target.IsOn,
                StringComparer.OrdinalIgnoreCase);
            var record = new CommandRecord(Guid.NewGuid().ToString("N"), deviceId, action,
                DateTime.UtcNow, false, targets.Select(target => target.Registration.DeviceId).ToArray());
            var entry = new CommandEntry(record, previous);
            _commandHistory.Add(entry);
            _undoHistory.Push(entry);
            if (_commandHistory.Count > MaximumCommandHistory)
            {
                var evicted = _commandHistory[0];
                _commandHistory.RemoveAt(0);
                // Invisible history entries must not remain undoable after a
                // long series of execute/undo operations evicts their records.
                if (!evicted.Record.IsUndone)
                    _undoHistory = new Stack<CommandEntry>(_undoHistory.Reverse().Where(item => item != evicted));
            }

            foreach (var target in targets)
            {
                target.IsOn = action == "TurnOn";
                target.HasManualOverride = true;
                Learn(target.Registration.DeviceId, target.IsOn ? "TurnOn" : "TurnOff", record.IssuedUtc, fromSearch: false);
            }
            return CopyCommand(record);
        }
    }

    public CommandRecord UndoCommand()
    {
        lock (_gate)
        {
            if (!_undoHistory.TryPop(out var entry))
                throw new InvalidOperationException("No manual override remains to undo.");
            // Each device's prior state is stored independently, so a bulk action
            // correctly restores a mixture of open and closed valves.
            foreach (var (deviceId, wasOn) in entry.PreviousStates)
            {
                _devices[deviceId].IsOn = wasOn;
                _devices[deviceId].HasManualOverride = true;
            }
            entry.Record = entry.Record with { IsUndone = true };
            return CopyCommand(entry.Record);
        }
    }

    /// <summary>Ascending timestamp order; equal timestamp packets remain distinct.</summary>
    public IReadOnlyList<TelemetryLog> GetTimeline(string deviceId, int count = 200)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            throw new ArgumentException("A device id is required.", nameof(deviceId));
        if (count < 1)
            throw new ArgumentOutOfRangeException(nameof(count));
        lock (_gate)
        {
            if (!_devices.TryGetValue(deviceId, out var device))
                throw new InvalidOperationException($"Device '{deviceId}' is not registered.");
            return device.Timeline.Values.SelectMany(bucket => bucket)
                .TakeLast(Math.Min(count, MaximumTimelineEntries)).ToArray();
        }
    }

    private double? ValidatePacket(ITelemetryPacket packet)
    {
        if (string.IsNullOrWhiteSpace(packet.DeviceId) || string.IsNullOrWhiteSpace(packet.PacketId))
            throw new ArgumentException("A device id and packet id are required.");
        if (!_devices.TryGetValue(packet.DeviceId, out var device))
            throw new ArgumentException($"Device '{packet.DeviceId}' is not registered.");
        if (packet.Category != device.Registration.Category)
            throw new ArgumentException("The packet category does not match the registered device.");
        if (packet.Category == SensorCategory.ValveState)
        {
            if (packet is not TelemetryPacket<bool>)
                throw new ArgumentException("Valve-state packets require a bool reading.");
            return null;
        }
        var numericValue = packet switch
        {
            TelemetryPacket<float> reading => (double)reading.Value,
            TelemetryPacket<int> reading => reading.Value,
            _ => throw new ArgumentException("This category requires a native float or int telemetry packet.")
        };
        if (!double.IsFinite(numericValue))
            throw new ArgumentException("Telemetry values must be finite.");
        if ((packet.Category is SensorCategory.SoilMoisture or SensorCategory.Humidity) && (numericValue < 0 || numericValue > 100))
            throw new ArgumentException("Percentage readings must be between 0 and 100.");
        if ((packet.Category is SensorCategory.PowerMeter or SensorCategory.LightLevel) && numericValue < 0)
            throw new ArgumentException("Power and light readings cannot be negative.");
        return numericValue;
    }

    private void ProcessPacket(ITelemetryPacket packet, bool critical)
    {
        var arrivals = _pendingArrivalOrder[packet.PacketId];
        var arrival = arrivals.Dequeue();
        if (arrivals.Count == 0)
            _pendingArrivalOrder.Remove(packet.PacketId);
        var device = _devices[packet.DeviceId];
        var now = DateTime.UtcNow;
        double? numeric = packet switch
        {
            TelemetryPacket<float> reading => reading.Value,
            TelemetryPacket<int> reading => reading.Value,
            _ => null
        };
        var log = new TelemetryLog(packet.PacketId, device.Registration.DeviceId, packet.Category,
            packet.TimestampUtc, now, ++_processedPackets, packet.FormattedValue, packet.Unit, critical, numeric);
        if (critical)
            _criticalPackets++;
        _store.AddPacket(packet);
        _recentTelemetry.Enqueue(log);
        if (_recentTelemetry.Count > MaximumRecentTelemetry)
            _recentTelemetry.Dequeue();

        // SortedDictionary orders timestamps in O(log n). Lists preserve collisions
        // rather than silently overwriting packets that share a device clock timestamp.
        if (!device.Timeline.TryGetValue(log.TimestampUtc, out var bucket))
            device.Timeline.Add(log.TimestampUtc, bucket = new List<TelemetryLog>());
        bucket.Add(log);
        device.TimelineCount++;
        while (device.TimelineCount > MaximumTimelineEntries)
        {
            var first = device.Timeline.First();
            first.Value.RemoveAt(0);
            if (first.Value.Count == 0)
                device.Timeline.Remove(first.Key);
            device.TimelineCount--;
        }

        device.LastReceivedUtc = now;
        device.IsConnected = true;
        SetAlert(device, "Disconnected", false);
        // Critical bypass can process a newer reading before older FIFO packets.
        // Logging those older packets must not clear the current alarm or state.
        if (device.LastSeenUtc is { } lastSeen && (packet.TimestampUtc < lastSeen
            || packet.TimestampUtc == lastSeen && arrival <= device.LastArrivalOrder))
            return;
        device.LastSeenUtc = packet.TimestampUtc;
        device.LastArrivalOrder = arrival;
        device.Value = packet.FormattedValue;
        device.Unit = packet.Unit;
        // Reported telemetry and operator intent are separate: an in-flight device
        // packet may have been captured before a command or Undo. Once overridden,
        // IsOn remains the desired simulated state; Value still reports the reading.
        if (!device.HasManualOverride)
        {
            if (packet is TelemetryPacket<bool> state)
                device.IsOn = state.Value;
            else if (packet.Category == SensorCategory.PowerMeter)
                device.IsOn = numeric > 0;
        }

        SetAlert(device, "CriticalMoisture", packet.Category == SensorCategory.SoilMoisture && numeric < 20, now);
        SetAlert(device, "PowerSpike", packet.Category == SensorCategory.PowerMeter && numeric >= 2000, now);
        SetAlert(device, "TemperatureOutOfRange", packet.Category == SensorCategory.Temperature && (numeric < 0 || numeric > 50));
        SetAlert(device, "HumidityOutOfRange", packet.Category == SensorCategory.Humidity && (numeric < 10 || numeric > 90));
    }

    private void SetAlert(DeviceState device, string code, bool active, DateTime? observedUtc = null)
    {
        var id = device.Registration.DeviceId;
        if (active)
        {
            var firstOccurrence = device.Alerts.Add(code);
            _activeAlerts.Add((id, code));
            if (observedUtc is { } observed && code is "CriticalMoisture" or "PowerSpike")
            {
                if (firstOccurrence || !_criticalContexts.TryGetValue(id, out var context) || context.Code != code)
                    _criticalContexts[id] = new CriticalContext(code, observed, ++_criticalEpisodeSequence);
                else
                    _criticalContexts[id] = context with { ObservedUtc = observed };
            }
        }
        else
        {
            device.Alerts.Remove(code);
            _activeAlerts.Remove((id, code));
        }
    }

    private void Learn(string targetId, string? action, DateTime now, bool fromSearch)
    {
        foreach (var (sourceId, context) in _criticalContexts)
        {
            if (now - context.ObservedUtc > LearningWindow)
                continue;
            var suggestedAction = action ?? (context.Code == "CriticalMoisture" ? "TurnOn" : "TurnOff");
            var key = new PatternKey(sourceId, context.Code, targetId, suggestedAction);
            if (!_patterns.TryGetValue(key, out var evidence))
            {
                if (_patterns.Count >= MaximumLearnedPatterns)
                    _patterns.Remove(_patterns.MinBy(pattern => pattern.Value.LastObservedUtc).Key);
                _patterns.Add(key, evidence = new PatternEvidence());
            }
            if (evidence.LastEpisode != context.Episode)
            {
                evidence.Occurrences++;
                evidence.LastEpisode = context.Episode;
            }
            if (fromSearch && evidence.LastSearchEpisode != context.Episode)
            {
                evidence.Searches++;
                evidence.LastSearchEpisode = context.Episode;
            }
            if (!fromSearch && evidence.LastCommandEpisode != context.Episode)
            {
                evidence.Commands++;
                evidence.LastCommandEpisode = context.Episode;
            }
            evidence.LastObservedUtc = now;
        }
    }

    private IReadOnlyList<SuggestedAction> BuildSuggestions()
    {
        var suggestions = new List<SuggestedAction>();
        // Rank the strongest learned action for each source/target pair to avoid
        // presenting conflicting On and Off suggestions for the same trigger.
        var learned = _patterns.Where(pattern => pattern.Value.Occurrences >= 3
                && _devices[pattern.Key.SourceId].IsConnected
                && _devices[pattern.Key.SourceId].Alerts.Contains(pattern.Key.AlertCode))
            .GroupBy(pattern => (pattern.Key.SourceId, pattern.Key.TargetId))
            .Select(group => group.MaxBy(pattern => pattern.Value.Score));
        foreach (var (pattern, evidence) in learned)
        {
            var source = _devices[pattern.SourceId];
            suggestions.Add(new SuggestedAction(
                $"learned:{pattern.SourceId}:{pattern.TargetId}:{pattern.Action}",
                $"Consider {pattern.Action} for {pattern.TargetId}",
                $"{source.Registration.DeviceId} has {pattern.AlertCode}. You searched for or commanded {pattern.TargetId} "
                + $"during {evidence.Occurrences} similar alert episodes ({evidence.Searches} searches, {evidence.Commands} commands). "
                + "This learned suggestion requires your confirmation.",
                pattern.TargetId, pattern.Action,
                Math.Min(0.98, evidence.Occurrences / (evidence.Occurrences + 2.0)), evidence.Occurrences));
        }
        foreach (var device in _devices.Values)
        {
            foreach (var alert in device.Alerts)
                suggestions.Add(new SuggestedAction($"alert:{device.Registration.DeviceId}:{alert}",
                    alert switch
                    {
                        "CriticalMoisture" => "Critical moisture drop",
                        "PowerSpike" => "Critical power spike",
                        "Disconnected" => "Device disconnected",
                        _ => "Reading outside the expected range"
                    },
                    $"Inspect {device.Registration.DeviceId} at {device.Registration.Location}: {alert}. "
                    + "Review the timeline and choose an override only when appropriate.",
                    device.Registration.DeviceId, "", 1.0, 1));
            var trend = BuildTrendSuggestion(device);
            if (trend is not null)
                suggestions.Add(trend);
        }
        // Learned operator actions and early warnings remain visible even when
        // many observed threshold/disconnect alerts would otherwise fill the cap.
        return suggestions.OrderByDescending(suggestion => suggestion.Action.Length > 0)
            .ThenByDescending(suggestion => suggestion.Id.StartsWith("trend:", StringComparison.Ordinal))
            .ThenByDescending(suggestion => suggestion.Confidence)
            .ThenBy(suggestion => suggestion.Id, StringComparer.Ordinal).Take(50).ToArray();
    }

    private static SuggestedAction? BuildTrendSuggestion(DeviceState device)
    {
        var category = device.Registration.Category;
        if (!device.IsConnected || category is not (SensorCategory.SoilMoisture or SensorCategory.PowerMeter)
            || device.Alerts.Contains("CriticalMoisture") || device.Alerts.Contains("PowerSpike"))
            return null;
        var samples = device.Timeline.Values.SelectMany(bucket => bucket)
            .Where(log => log.NumericValue.HasValue).TakeLast(8).ToArray();
        if (samples.Length < 4)
            return null;
        var origin = samples[0].TimestampUtc;
        var x = samples.Select(sample => (sample.TimestampUtc - origin).TotalSeconds).ToArray();
        var y = samples.Select(sample => sample.NumericValue!.Value).ToArray();
        var meanX = x.Average();
        var meanY = y.Average();
        var variance = x.Sum(value => (value - meanX) * (value - meanX));
        if (variance <= 0 || x[^1] <= 0)
            return null;
        var covariance = x.Select((value, index) => (value - meanX) * (y[index] - meanY)).Sum();
        var slope = covariance / variance;
        var totalVariance = y.Sum(value => (value - meanY) * (value - meanY));
        if (totalVariance <= 0)
            return null;
        var fit = covariance * covariance / (variance * totalVariance);
        if (fit < 0.65)
            return null;
        var horizon = x[^1] / (samples.Length - 1) * 2;
        var projected = y[^1] + slope * horizon;
        var moisture = category == SensorCategory.SoilMoisture;
        if (!(moisture && slope < 0 && y[^1] >= 20 && projected < 20)
            && !(!moisture && slope > 0 && y[^1] < 2000 && projected >= 2000))
            return null;
        return new SuggestedAction($"trend:{device.Registration.DeviceId}",
            moisture ? "Moisture falling toward critical level" : "Power rising toward spike",
            $"{samples.Length} readings from {device.Registration.DeviceId} show a consistent trend. "
            + $"The two-interval projection is {projected.ToString("0.0", CultureInfo.InvariantCulture)}{device.Unit} "
            + $"in {horizon.ToString("0.0", CultureInfo.InvariantCulture)}s. Inspect this device before the threshold is reached.",
            device.Registration.DeviceId, "", Math.Min(0.95, fit), samples.Length);
    }

    private static bool IsCritical(SensorCategory category, double? value) =>
        category == SensorCategory.SoilMoisture && value < 20
        || category == SensorCategory.PowerMeter && value >= 2000;

    private static bool IsActuator(DeviceState device) =>
        device.Registration.Category is SensorCategory.ValveState or SensorCategory.PowerMeter;

    private static string ValidateQuery(string? query)
    {
        query = query?.Trim() ?? "";
        if (query.Length > 128)
            throw new ArgumentException("Search queries are limited to 128 characters.", nameof(query));
        return query;
    }

    private static bool MatchesQuery(DeviceState device, string query)
    {
        if (query.Length == 0)
            return true;
        var text = device.SearchText + " " + string.Join(' ', device.Alerts);
        var normalised = NormaliseSearch(query);
        return text.Contains(query, StringComparison.OrdinalIgnoreCase)
            || normalised.Length > 0 && NormaliseSearch(text).Contains(normalised, StringComparison.Ordinal);
    }

    private static string NormaliseSearch(string text) =>
        new(text.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private static DeviceSnapshot ToSnapshot(DeviceState device) => new(
        device.Registration.DeviceId, device.Registration.MacAddress, device.Registration.Location,
        device.Registration.Category, device.Value, device.Unit, device.LastSeenUtc,
        device.IsConnected, device.IsOn, device.Alerts.Order(StringComparer.Ordinal).ToArray());

    private static CommandRecord CopyCommand(CommandRecord record) =>
        record with { AffectedDeviceIds = record.AffectedDeviceIds.ToArray() };

    private sealed class DeviceState
    {
        public SensorRegistration Registration { get; }
        public string SearchText { get; }
        public string Value { get; set; } = "Awaiting telemetry";
        public string Unit { get; set; } = "";
        public DateTime? LastSeenUtc { get; set; }
        public DateTime? LastReceivedUtc { get; set; }
        public long LastArrivalOrder { get; set; }
        public bool IsConnected { get; set; }
        public bool IsOn { get; set; }
        public bool HasManualOverride { get; set; }
        public HashSet<string> Alerts { get; } = new(StringComparer.Ordinal);
        public SortedDictionary<DateTime, List<TelemetryLog>> Timeline { get; } = new();
        public int TimelineCount { get; set; }

        public DeviceState(SensorRegistration registration)
        {
            Registration = registration;
            SearchText = $"{registration.DeviceId} {registration.MacAddress} {registration.Location} {registration.Category}";
        }
    }

    private sealed class CommandEntry(CommandRecord record, Dictionary<string, bool> previousStates)
    {
        public CommandRecord Record { get; set; } = record;
        public Dictionary<string, bool> PreviousStates { get; } = previousStates;
    }

    private sealed record CriticalContext(string Code, DateTime ObservedUtc, long Episode);
    private sealed record PatternKey(string SourceId, string AlertCode, string TargetId, string Action);
    private sealed class PatternEvidence
    {
        public int Occurrences { get; set; }
        public int Searches { get; set; }
        public int Commands { get; set; }
        public long LastEpisode { get; set; }
        public long LastSearchEpisode { get; set; }
        public long LastCommandEpisode { get; set; }
        public DateTime LastObservedUtc { get; set; }
        public double Score => Occurrences + Searches * 0.2 + Commands * 0.5;
    }
}
