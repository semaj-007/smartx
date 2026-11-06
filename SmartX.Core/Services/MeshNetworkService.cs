using SmartX.Core.Models;

namespace SmartX.Core.Services;

/// <summary>
/// Models an undirected IoT mesh. Device liveness is supplied by the telemetry
/// engine; links and their positive costs are an explicit simulation, not RF discovery.
/// </summary>
public sealed class MeshNetworkService
{
    public const string GatewayId = "GATEWAY";
    private readonly object _gate = new();
    private readonly TelemetryStore _store;
    private readonly RealTimeEngine _engine;

    // Adjacency lists represent vertices and their weighted neighbours (Shaffer, n.d.-c).
    private readonly Dictionary<string, Dictionary<string, Edge>> _adjacency = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _canonicalIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _idByMac = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _registrationOrder = new();

    public MeshNetworkService(TelemetryStore store, RealTimeEngine engine)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _adjacency.Add(GatewayId, new Dictionary<string, Edge>(StringComparer.OrdinalIgnoreCase));
        _canonicalIds.Add(GatewayId, GatewayId);
        SynchroniseRegistry();
    }

    public MeshTopologySnapshot GetTopology()
    {
        lock (_gate)
        {
            var nodes = CaptureNodes();
            var online = OnlineIds(nodes);
            var known = nodes.Select(node => node.DeviceId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return new MeshTopologySnapshot(nodes, SnapshotLinks(online, known), BuildMinimumSpanningForest(online), DateTime.UtcNow);
        }
    }

    public string ResolveDeviceId(string identifier)
    {
        lock (_gate)
        {
            SynchroniseRegistry();
            return Resolve(identifier);
        }
    }

    public MeshRoute FindRoute(string from, string to = GatewayId, string algorithm = "bfs")
    {
        var method = algorithm?.Trim().ToLowerInvariant();
        if (method is not ("bfs" or "dijkstra" or "mst"))
            throw new ArgumentException("Algorithm must be 'bfs', 'dijkstra' or 'mst'.", nameof(algorithm));
        lock (_gate)
        {
            var nodes = CaptureNodes();
            var source = Resolve(from);
            var target = Resolve(to);
            var online = OnlineIds(nodes);
            if (!online.Contains(source) || !online.Contains(target))
                return Unreachable(source, target, method, 0, "The source or destination is offline or awaiting telemetry.");
            if (source == target)
                return new MeshRoute(source, target, method, true, new[] { source }, 0, 0, 1, "Source and destination are the same active node.");
            if (method == "dijkstra")
                return MinimumCostRoute(source, target, online);
            if (method == "mst")
            {
                var treeEdges = BuildMinimumSpanningForest(online).Links
                    .Select(link => _adjacency[link.SourceId][link.TargetId]).ToHashSet();
                return BreadthFirstRoute(source, target, online, treeEdges, "mst");
            }
            return BreadthFirstRoute(source, target, online);
        }
    }

    // BFS identifies the active subnet reachable from a selected gateway/node (Shaffer, n.d.-d).
    public MeshTraversal Traverse(string from = GatewayId)
    {
        lock (_gate)
        {
            var nodes = CaptureNodes();
            var source = Resolve(from);
            var online = OnlineIds(nodes);
            var pending = new Queue<string>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (online.Contains(source))
            {
                pending.Enqueue(source);
                visited.Add(source);
            }
            while (pending.TryDequeue(out var current))
                foreach (var (neighbour, _) in AvailableNeighbours(current, online))
                    if (visited.Add(neighbour))
                        pending.Enqueue(neighbour);
            return new MeshTraversal(source,
                visited.Order(StringComparer.Ordinal).ToArray(),
                nodes.Where(node => !node.IsConnected).Select(node => node.DeviceId).Order(StringComparer.Ordinal).ToArray(),
                online.Where(id => !visited.Contains(id)).Order(StringComparer.Ordinal).ToArray(), visited.Count);
        }
    }

    public void UpsertLink(string from, string to, double cost, bool enabled = true)
    {
        if (!double.IsFinite(cost) || cost <= 0 || cost > 100_000)
            throw new ArgumentException("Link cost must be finite, greater than zero and at most 100000.", nameof(cost));
        lock (_gate)
        {
            SynchroniseRegistry();
            var source = Resolve(from);
            var target = Resolve(to);
            ValidateDistinct(source, target);
            SetLink(source, target, cost, enabled);
        }
    }

    public void RemoveLink(string from, string to)
    {
        lock (_gate)
        {
            SynchroniseRegistry();
            var source = Resolve(from);
            var target = Resolve(to);
            ValidateDistinct(source, target);
            _adjacency[source].Remove(target);
            _adjacency[target].Remove(source);
        }
    }

    private IReadOnlyList<MeshNode> CaptureNodes()
    {
        var devices = _engine.GetSnapshot().Devices;
        // Capture liveness first, then create graph vertices from that exact snapshot.
        // Registration can run concurrently; an online node must never appear in a
        // route/MST without its matching adjacency list, or a link without its node.
        SynchroniseRegistry(devices.Select(device => (device.DeviceId, device.MacAddress)));
        var nodes = new List<MeshNode>(devices.Count + 1)
        {
            new(GatewayId, "", "Smart-X ingestion gateway", "Gateway", true, true, null, Array.Empty<string>())
        };
        foreach (var device in devices.OrderBy(d => d.DeviceId, StringComparer.OrdinalIgnoreCase))
            nodes.Add(new MeshNode(device.DeviceId, device.MacAddress, device.Location, device.Category.ToString(),
                device.IsConnected, false, device.LastSeenUtc, device.ActiveAlerts.ToArray()));
        return nodes;
    }

    private void SynchroniseRegistry(IEnumerable<(string DeviceId, string MacAddress)>? capturedDevices = null)
    {
        // Default wireless edges are created once per new registration. Explicit
        // edits/deletions survive subsequent refreshes instead of being silently reset.
        var devices = capturedDevices ?? _store.Sensors.Select(device => (device.DeviceId, device.MacAddress));
        foreach (var device in devices.OrderBy(s => s.DeviceId, StringComparer.OrdinalIgnoreCase))
        {
            if (_canonicalIds.ContainsKey(device.DeviceId))
                continue;
            var index = _registrationOrder.Count;
            _canonicalIds.Add(device.DeviceId, device.DeviceId);
            _adjacency.Add(device.DeviceId, new Dictionary<string, Edge>(StringComparer.OrdinalIgnoreCase));
            var mac = NormaliseMac(device.MacAddress);
            if (mac is not null)
                _idByMac[mac] = device.DeviceId;
            _registrationOrder.Add(device.DeviceId);
            if (index == 0 || index % 4 == 0)
                SetLink(device.DeviceId, GatewayId, 4 + index % 3, true);
            if (index > 0)
                SetLink(device.DeviceId, _registrationOrder[index - 1], 1 + index % 5, true);
            if (index > 1)
                SetLink(device.DeviceId, _registrationOrder[index - 2], 1.5 + index % 7 / 10.0, true);
        }
    }

    private string Resolve(string? identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier) || identifier.Length > 100)
            throw new ArgumentException("A device ID or MAC address of at most 100 characters is required.", nameof(identifier));
        identifier = identifier.Trim();
        if (_canonicalIds.TryGetValue(identifier, out var id))
            return id;
        var mac = NormaliseMac(identifier);
        if (mac is not null && _idByMac.TryGetValue(mac, out id))
            return id;
        throw new KeyNotFoundException($"Device '{identifier}' is not registered in the mesh.");
    }

    private static string? NormaliseMac(string? mac)
    {
        if (mac is null) return null;
        var normalised = mac.Trim().Replace(":", "").Replace("-", "");
        return normalised.Length == 12 && normalised.All(Uri.IsHexDigit) ? normalised.ToUpperInvariant() : null;
    }

    private static void ValidateDistinct(string source, string target)
    {
        if (source == target)
            throw new ArgumentException("A wireless link requires two different devices.");
    }

    private void SetLink(string source, string target, double cost, bool enabled)
    {
        // Both directions reference the same immutable edge, preserving an
        // undirected graph when a cost or availability setting is replaced.
        var first = StringComparer.Ordinal.Compare(source, target) < 0 ? source : target;
        var second = first == source ? target : source;
        var edge = new Edge(first, second, cost, enabled);
        _adjacency[source][target] = edge;
        _adjacency[target][source] = edge;
    }

    private static HashSet<string> OnlineIds(IReadOnlyList<MeshNode> nodes) =>
        nodes.Where(n => n.IsConnected).Select(n => n.DeviceId).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private IReadOnlyList<MeshLink> SnapshotLinks(HashSet<string> online, HashSet<string> known) =>
        _adjacency.Values.SelectMany(neighbours => neighbours.Values).Distinct()
            .Where(edge => known.Contains(edge.SourceId) && known.Contains(edge.TargetId))
            .OrderBy(edge => edge.SourceId, StringComparer.Ordinal)
            .ThenBy(edge => edge.TargetId, StringComparer.Ordinal)
            .Select(edge => ToLink(edge, online)).ToArray();

    private static MeshLink ToLink(Edge edge, HashSet<string> online) =>
        new(edge.SourceId, edge.TargetId, edge.Cost, edge.Enabled,
            edge.Enabled && online.Contains(edge.SourceId) && online.Contains(edge.TargetId));

    private IEnumerable<KeyValuePair<string, Edge>> AvailableNeighbours(string id, HashSet<string> online) =>
        _adjacency[id].Where(pair => pair.Value.Enabled && online.Contains(pair.Key))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal);

    // BFS discovers each active vertex once and returns a minimum-hop path (Shaffer, n.d.-d).
    private MeshRoute BreadthFirstRoute(string source, string target, HashSet<string> online,
        HashSet<Edge>? allowedEdges = null, string algorithm = "bfs")
    {
        var pending = new Queue<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { source };
        var previous = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        pending.Enqueue(source);
        while (pending.TryDequeue(out var current))
        {
            foreach (var (neighbour, edge) in AvailableNeighbours(current, online))
            {
                if (allowedEdges is not null && !allowedEdges.Contains(edge)) continue;
                if (!visited.Add(neighbour)) continue;
                previous.Add(neighbour, current);
                if (neighbour == target)
                    return BuildRoute(source, target, algorithm, previous, visited.Count);
                pending.Enqueue(neighbour);
            }
        }
        return Unreachable(source, target, algorithm, visited.Count, "No active wireless path connects these devices.");
    }

    // Dijkstra relaxes positive-weight edges to find minimum total cost (Shaffer, n.d.-e).
    private MeshRoute MinimumCostRoute(string source, string target, HashSet<string> online)
    {
        var distances = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { [source] = 0 };
        var previous = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var settled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // The heap dequeues the lowest accumulated cost; a sequence resolves ties (Microsoft, n.d.-s).
        var pending = new PriorityQueue<string, (double Cost, long Sequence)>();
        long sequence = 0;
        pending.Enqueue(source, (0, sequence++));
        while (pending.TryDequeue(out var current, out var priority))
        {
            if (!settled.Add(current)) continue;
            if (current == target)
                return BuildRoute(source, target, "dijkstra", previous, settled.Count);
            foreach (var (neighbour, edge) in AvailableNeighbours(current, online))
            {
                if (settled.Contains(neighbour)) continue;
                var candidate = priority.Cost + edge.Cost;
                if (distances.TryGetValue(neighbour, out var known) && candidate >= known) continue;
                distances[neighbour] = candidate;
                previous[neighbour] = current;
                pending.Enqueue(neighbour, (candidate, sequence++));
            }
        }
        return Unreachable(source, target, "dijkstra", settled.Count, "No active wireless path connects these devices.");
    }

    private MeshRoute BuildRoute(string source, string target, string algorithm,
        Dictionary<string, string> previous, int visitedCount)
    {
        var path = new List<string> { target };
        for (var current = target; current != source;)
        {
            current = previous[current];
            path.Add(current);
        }
        path.Reverse();
        var total = 0.0;
        for (var index = 1; index < path.Count; index++)
            total += _adjacency[path[index - 1]][path[index]].Cost;
        var message = algorithm switch
        {
            "bfs" => "Minimum-hop active route.",
            "mst" => "Route follows the minimum spanning forest, which minimizes total simulated link energy cost across each connected mesh component.",
            _ => "Minimum-cost active route."
        };
        return new MeshRoute(source, target, algorithm, true, path.ToArray(), total,
            path.Count - 1, visitedCount, message);
    }

    private static MeshRoute Unreachable(string source, string target, string algorithm, int visitedCount, string message) =>
        new(source, target, algorithm, false, Array.Empty<string>(), 0, 0, visitedCount, message);

    // Prim's cut expansion constructs an MST per active connected component;
    // restarting at an unvisited vertex yields a minimum spanning forest (Shaffer, n.d.-f).
    private MeshSpanningTree BuildMinimumSpanningForest(HashSet<string> online)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var selected = new List<MeshLink>();
        var components = 0;
        var total = 0.0;
        long sequence = 0;
        // PriorityQueue is the min-heap used to choose the cheapest frontier edge (Microsoft, n.d.-s).
        var frontier = new PriorityQueue<(string Target, Edge Edge), (double Cost, long Sequence)>();
        foreach (var start in online.OrderBy(id => id == GatewayId ? 0 : 1).ThenBy(id => id, StringComparer.Ordinal))
        {
            if (!visited.Add(start)) continue;
            components++;
            AddFrontier(start);
            while (frontier.TryDequeue(out var candidate, out _))
            {
                if (!visited.Add(candidate.Target)) continue;
                selected.Add(ToLink(candidate.Edge, online));
                total += candidate.Edge.Cost;
                AddFrontier(candidate.Target);
            }
        }
        return new MeshSpanningTree(selected.ToArray(), total, components, components <= 1, online.Count);

        void AddFrontier(string id)
        {
            foreach (var (neighbour, edge) in AvailableNeighbours(id, online))
                if (!visited.Contains(neighbour))
                    frontier.Enqueue((neighbour, edge), (edge.Cost, sequence++));
        }
    }

    private sealed record Edge(string SourceId, string TargetId, double Cost, bool Enabled);
}
