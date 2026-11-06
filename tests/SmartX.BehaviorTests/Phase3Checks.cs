using SmartX.Core.DataStructures;
using SmartX.Core.Models;
using SmartX.Core.Services;

internal static class Phase3Checks
{
    private static int _assertions;
    public static int Assertions => _assertions;
    private static readonly DateTime Origin = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    public static readonly (string Name, Action Run)[] Tests =
    [
        ("AVL balances left-left insertion rotations", () => Rotation([3, 2, 1])),
        ("AVL balances right-right insertion rotations", () => Rotation([1, 2, 3])),
        ("AVL balances left-right insertion rotations", () => Rotation([3, 1, 2])),
        ("AVL balances right-left insertion rotations", () => Rotation([1, 3, 2])),
        ("AVL remains balanced for ascending, descending and shuffled histories", AvlHeight),
        ("AVL ranges include both boundaries and preserve timestamp collisions", InclusiveRange),
        ("AVL validates queries and reports empty ranges", InvalidRanges),
        ("AVL evicts oldest observations and maintains minute counts", BoundedAvl),
        ("AVL range traversal prunes unrelated history", RangePruning),
        ("AVL range results agree with a randomized sorted oracle", RandomizedRanges),
        ("AVL range limits report truncation without losing chronological order", RangeLimit),
        ("Real-time ingestion feeds indexed per-device historical ranges", EngineHistory),
        ("Mesh BFS minimizes hops while Dijkstra minimizes link cost", RoutingAlgorithms),
        ("Mesh routing resolves device IDs and normalized MAC addresses", MacAliases),
        ("Prim finds the known minimum spanning tree without duplicate links", KnownMst),
        ("Prim reports a forest for disconnected active components", DisconnectedForest),
        ("MST routes follow only minimum spanning forest edges", MstRoute),
        ("BFS traversal identifies reachable, offline and isolated nodes", Traversal),
        ("Mesh routes exclude disabled wireless links", DisabledLink),
        ("Mesh excludes offline nodes and recovers routes on telemetry", OfflineRecovery),
        ("Mesh discovers newly registered devices without fake telemetry", DynamicRegistration),
        ("Mesh validates graph edits and preserves undirected edge identity", LinkValidation),
        ("Mesh self routes have zero cost and traversal is deterministic", SelfAndDeterministicRoutes),
        ("Mesh snapshots and routes remain consistent during concurrent link updates", ConcurrentGraph),
        ("Mesh snapshots remain internally consistent during concurrent registrations", ConcurrentRegistrations)
    ];

    private static void Rotation(int[] values)
    {
        var index = new TelemetryHistoryIndex();
        foreach (var value in values) index.Add(Log(value));
        Equal(2, index.Height, "Three-node AVL height");
        Sequence(new long[] { 1, 2, 3 }, index.Latest(3).Select(log => log.Sequence), "Rotated timestamp ordering");
    }

    private static void AvlHeight()
    {
        const int count = 5000;
        var random = new Random(7312);
        var orders = new[]
        {
            Enumerable.Range(0, count).ToArray(),
            Enumerable.Range(0, count).Reverse().ToArray(),
            Enumerable.Range(0, count).OrderBy(_ => random.Next()).ToArray()
        };
        foreach (var values in orders)
        {
            var index = new TelemetryHistoryIndex(count);
            foreach (var value in values) index.Add(Log(value));
            Equal(count, index.Count, "Retained observation count");
            Require(index.Height <= 19, $"AVL height {index.Height} must remain logarithmic for 5000 nodes.");
            Sequence(Enumerable.Range(0, count).Select(value => (long)value), index.Latest(count).Select(log => log.Sequence), "Chronological order");
        }
    }

    private static void InclusiveRange()
    {
        var index = new TelemetryHistoryIndex();
        var sameTime = Origin.AddSeconds(5);
        foreach (var value in new[] { Log(1), Log(3, sameTime), Log(2, sameTime), Log(4, Origin.AddSeconds(8)) }) index.Add(value);
        var exact = index.Query(sameTime, sameTime);
        Sequence(new long[] { 2, 3 }, exact.Readings.Select(log => log.Sequence), "Equal-timestamp sequence order");
        var inclusive = index.Query(Origin.AddSeconds(1), Origin.AddSeconds(8));
        Equal(4, inclusive.Readings.Count, "Both exact boundary timestamps included");
        Equal(4, inclusive.RetainedCount, "Range diagnostics preserve index size");
        Equal(index.Height, inclusive.TreeHeight, "Range tree height diagnostic");
    }

    private static void InvalidRanges()
    {
        var index = new TelemetryHistoryIndex();
        Equal(0, index.Query(Origin, Origin).Readings.Count, "Empty AVL query");
        Equal(0, index.Height, "Empty AVL height");
        Throws<ArgumentException>(() => index.Query(Origin.AddSeconds(1), Origin));
        Throws<ArgumentOutOfRangeException>(() => index.Query(Origin, Origin, 0));
        Throws<ArgumentOutOfRangeException>(() => index.Latest(0));
        Throws<ArgumentOutOfRangeException>(() => new TelemetryHistoryIndex(0));
        index.Add(Log(1));
        Throws<ArgumentException>(() => index.Add(Log(1)));
        Equal(1, index.Count, "Rejected duplicate key leaves tree unchanged");
        Equal(0, index.Query(Origin.AddDays(1), Origin.AddDays(2)).Readings.Count, "Disjoint range is empty");
    }

    private static void BoundedAvl()
    {
        var index = new TelemetryHistoryIndex(5);
        foreach (var value in new[] { 3, 1, 5, 2, 4, 0, 6 }) index.Add(Log(value, Origin.AddMinutes(value)));
        Sequence(new long[] { 2, 3, 4, 5, 6 }, index.Latest(10).Select(log => log.Sequence), "Capacity retains newest timestamps even for delayed arrivals");
        Equal(5, index.Count, "Bounded retained count");
        Equal(5, index.TimeBuckets.Values.Sum(), "Minute counts match retained readings");
        Require(index.TimeBuckets.Keys.All(time => time >= Origin.AddMinutes(2)), "Evicted minute buckets must be removed.");
        Require(index.Height <= 3, "AVL deletion must rebalance retained nodes.");
        var snapshot = index.TimeBuckets;
        index.Add(Log(7, Origin.AddMinutes(7)));
        Require(snapshot.ContainsKey(Origin.AddMinutes(2)), "Earlier histogram snapshots must be independent.");
        Require(!index.TimeBuckets.ContainsKey(Origin.AddMinutes(2)), "Current histogram reflects eviction.");
    }

    private static void RangePruning()
    {
        var index = new TelemetryHistoryIndex(5000);
        for (var value = 0; value < 5000; value++) index.Add(Log(value));
        var result = index.Query(Origin.AddSeconds(2500), Origin.AddSeconds(2500));
        Equal(1, result.Readings.Count, "Narrow query result count");
        Equal(2500L, result.Readings[0].Sequence, "Narrow query identity");
        Require(result.VisitedNodes > 0 && result.VisitedNodes <= 64,
            $"Narrow range should prune 5000-node tree; visited {result.VisitedNodes}.");
    }

    private static void RandomizedRanges()
    {
        var random = new Random(2026);
        var index = new TelemetryHistoryIndex(200);
        var oracle = new List<TelemetryLog>();
        for (var sequence = 1; sequence <= 600; sequence++)
        {
            var reading = Log(sequence, Origin.AddSeconds(random.Next(80)));
            index.Add(reading);
            oracle.Add(reading);
            oracle = oracle.OrderBy(log => log.TimestampUtc).ThenBy(log => log.Sequence).TakeLast(200).ToList();
            if (sequence % 15 != 0) continue;
            var first = random.Next(85);
            var last = random.Next(first, 90);
            var expected = oracle.Where(log => log.TimestampUtc >= Origin.AddSeconds(first) && log.TimestampUtc <= Origin.AddSeconds(last));
            var result = index.Query(Origin.AddSeconds(first), Origin.AddSeconds(last));
            Sequence(expected.Select(log => log.Sequence), result.Readings.Select(log => log.Sequence), "Randomized inclusive range oracle");
            Equal(oracle.Count, result.RetainedCount, "Randomized retention count");
            Equal(oracle.Count, index.TimeBuckets.Values.Sum(), "Randomized minute histogram accounting");
        }
    }

    private static void RangeLimit()
    {
        var index = new TelemetryHistoryIndex();
        for (var sequence = 0; sequence < 20; sequence++) index.Add(Log(sequence));
        var limited = index.Query(Origin, Origin.AddMinutes(1), 3);
        Require(limited.Truncated, "A matching range larger than its requested limit must report truncation.");
        Sequence(new long[] { 0, 1, 2 }, limited.Readings.Select(log => log.Sequence), "Limited range returns chronological prefix");
        Require(!index.Query(Origin, Origin.AddSeconds(2), 3).Truncated, "An exact-limit range is complete.");
        Equal(20, index.Count, "Range limits never mutate history");
    }

    private static void EngineHistory()
    {
        var store = new TelemetryStore();
        store.AddSensor(Sensor("RangeA", 1));
        store.AddSensor(Sensor("RangeB", 2));
        var engine = new RealTimeEngine(store);
        var early = Packet("RangeA", 20, Origin);
        var last = Packet("RangeA", 22, Origin.AddMinutes(2));
        var middle = Packet("RangeA", 21, Origin.AddMinutes(1));
        foreach (var packet in new[] { last, early, Packet("RangeB", 40, Origin.AddMinutes(1)), middle }) engine.Enqueue(packet);
        engine.ProcessPending();
        var result = engine.GetHistoryRange("RangeA", Origin, Origin.AddMinutes(1));
        Sequence(new[] { early.PacketId, middle.PacketId }, result.Readings.Select(log => log.PacketId), "Actual processed readings in source-specific range");
        Equal(3, result.RetainedCount, "Other device telemetry excluded");
        Equal(last.PacketId, engine.GetTimeline("RangeA", 1).Single().PacketId, "Legacy newest timeline uses AVL ordering");
        Throws<InvalidOperationException>(() => engine.GetHistoryRange("Missing", Origin, Origin.AddMinutes(1)));
    }

    private static void RoutingAlgorithms()
    {
        var fixture = Graph();
        var bfs = fixture.Mesh.FindRoute("A");
        Sequence(new[] { "A", "GATEWAY" }, bfs.Path, "BFS uses one-hop gateway connection");
        Equal(1, bfs.Hops, "BFS hop count");
        Equal(10d, bfs.TotalCost, "BFS reports actual path cost");
        var weighted = fixture.Mesh.FindRoute("A", "GATEWAY", "dijkstra");
        Sequence(new[] { "A", "B", "GATEWAY" }, weighted.Path, "Dijkstra chooses lower-cost multi-hop route");
        Equal(2d, weighted.TotalCost, "Weighted route cost");
        Require(weighted.Found && bfs.Found, "Both valid routes must be found.");
    }

    private static void MacAliases()
    {
        var fixture = Graph();
        var route = fixture.Mesh.FindRoute("02-00-00-00-00-01", "gateway", "bfs");
        Equal("A", route.SourceId, "Hyphenated MAC resolves canonical device id");
        Equal("GATEWAY", route.TargetId, "Gateway alias resolves canonical id");
        Require(fixture.Mesh.FindRoute("020000000001", "02:00:00:00:00:02", "dijkstra").Found, "Compact/colon MAC route endpoints must resolve.");
        Sequence(new[] { "A", "B" }, fixture.Mesh.FindRoute("a", "b").Path, "IDs match case-insensitively");
    }

    private static void KnownMst()
    {
        var tree = Graph().Mesh.GetTopology().SpanningTree;
        Equal(4, tree.ActiveNodeCount, "Gateway and three active sensors");
        Equal(3, tree.Links.Count, "Connected spanning tree has V-1 edges");
        Equal(4d, tree.TotalCost, "Known minimum spanning tree weight");
        Equal(1, tree.ComponentCount, "Connected graph component count");
        Require(tree.IsConnected, "Known connected graph must report a complete spanning tree.");
        Equal(tree.Links.Count, tree.Links.Select(EdgeKey).Distinct().Count(), "MST undirected edges cannot duplicate");
    }

    private static void DisconnectedForest()
    {
        var fixture = Graph();
        fixture.Mesh.RemoveLink("A", "GATEWAY");
        fixture.Mesh.RemoveLink("A", "B");
        var route = fixture.Mesh.FindRoute("A");
        Require(!route.Found && route.Path.Count == 0, "Isolated active sensor has no gateway route.");
        var forest = fixture.Mesh.GetTopology().SpanningTree;
        Equal(2, forest.ComponentCount, "Isolated node forms an additional component");
        Equal(2, forest.Links.Count, "Forest edge count is V-components");
        Equal(3d, forest.TotalCost, "Forest total excludes isolated node");
        Require(!forest.IsConnected, "Disconnected active graph must not claim one spanning tree.");
    }

    private static void DisabledLink()
    {
        var fixture = Graph();
        fixture.Mesh.UpsertLink("A", "GATEWAY", 10, false);
        Sequence(new[] { "A", "B", "GATEWAY" }, fixture.Mesh.FindRoute("A").Path, "BFS reroutes around disabled direct link");
        var disabled = fixture.Mesh.GetTopology().Links.Single(link => EdgeKey(link) == "A|GATEWAY");
        Require(!disabled.Enabled && !disabled.IsAvailable, "Disabled wireless edges must remain visible but unavailable.");
    }

    private static void MstRoute()
    {
        var fixture = Graph();
        var route = fixture.Mesh.FindRoute("C", "GATEWAY", "mst");
        Require(route.Found, "Connected mesh must have a spanning-tree route.");
        Sequence(new[] { "C", "B", "GATEWAY" }, route.Path, "MST route uses shared low-energy backbone");
        Equal(3d, route.TotalCost, "MST route energy cost");
        var treeEdges = fixture.Mesh.GetTopology().SpanningTree.Links.Select(EdgeKey).ToHashSet();
        foreach (var (first, second) in route.Path.Zip(route.Path.Skip(1)))
            Require(treeEdges.Contains(EdgeKey(new MeshLink(first, second, 1, true, true))), "Every MST route hop must belong to the selected spanning forest.");
    }

    private static void Traversal()
    {
        var fixture = Graph();
        fixture.Mesh.RemoveLink("A", "GATEWAY");
        fixture.Mesh.RemoveLink("A", "B");
        var traversal = fixture.Mesh.Traverse();
        Require(traversal.ReachableDeviceIds.Contains("GATEWAY") && traversal.ReachableDeviceIds.Contains("B") && traversal.ReachableDeviceIds.Contains("C"), "Gateway BFS must visit its whole active component.");
        Sequence(new[] { "A" }, traversal.UnreachableDeviceIds, "Isolated online node appears in unreachable set");
        Equal(0, traversal.OfflineDeviceIds.Count, "No initially offline fixture devices");
        Equal(3, traversal.VisitedCount, "Each reachable vertex visited once");
        fixture.Engine.MarkDisconnected(DateTime.UtcNow.AddMinutes(1), TimeSpan.FromSeconds(15));
        fixture.Engine.Enqueue(Packet("B", 21));
        fixture.Engine.ProcessPending();
        var offline = fixture.Mesh.Traverse("B");
        Require(offline.OfflineDeviceIds.Contains("A") && offline.OfflineDeviceIds.Contains("C"), "Offline devices are classified separately from isolated active vertices.");
        Require(offline.ReachableDeviceIds.Contains("GATEWAY"), "Active node traversal includes reachable gateway.");
        Equal(0, offline.UnreachableDeviceIds.Count, "Offline vertices are not also marked online-unreachable");
        fixture.Engine.Enqueue(Packet("C", 22));
        fixture.Engine.ProcessPending();
        Require(fixture.Mesh.Traverse("B").ReachableDeviceIds.Contains("C"), "Reconnected device returns to BFS traversal.");
    }

    private static void OfflineRecovery()
    {
        var fixture = Graph();
        fixture.Engine.MarkDisconnected(DateTime.UtcNow.AddMinutes(1), TimeSpan.FromSeconds(15));
        fixture.Engine.Enqueue(Packet("A", 20));
        fixture.Engine.Enqueue(Packet("C", 22));
        fixture.Engine.ProcessPending();
        Require(!fixture.Mesh.FindRoute("B").Found, "Offline source cannot route.");
        Require(fixture.Mesh.GetTopology().Links.Where(link => link.SourceId == "B" || link.TargetId == "B").All(link => !link.IsAvailable), "Offline transit edges unavailable.");
        Equal(3, fixture.Mesh.GetTopology().SpanningTree.ActiveNodeCount, "MST excludes offline node but includes gateway");
        fixture.Engine.Enqueue(Packet("B", 21));
        fixture.Engine.ProcessPending();
        Require(fixture.Mesh.FindRoute("B").Found, "Fresh telemetry restores mesh routes.");
        Equal(4, fixture.Mesh.GetTopology().SpanningTree.ActiveNodeCount, "Reconnected node rejoins MST");
    }

    private static void DynamicRegistration()
    {
        var fixture = Graph();
        fixture.Engine.RegisterDevice(Sensor("NEW", 4));
        var node = fixture.Mesh.GetTopology().Nodes.Single(node => node.DeviceId == "NEW");
        Require(!node.IsConnected && node.LastSeenUtc is null, "Registration must not fabricate connection telemetry.");
        Require(!fixture.Mesh.FindRoute("NEW").Found, "Unseen node cannot claim an active route.");
        fixture.Engine.Enqueue(Packet("NEW", 23));
        fixture.Engine.ProcessPending();
        Require(fixture.Mesh.FindRoute("NEW").Found, "New node automatically gains simulated mesh connectivity on telemetry.");
    }

    private static void LinkValidation()
    {
        var fixture = Graph();
        var before = fixture.Mesh.GetTopology().Links.Count;
        foreach (var cost in new[] { 0d, -1, double.NaN, double.PositiveInfinity, 100001 })
            Throws<ArgumentException>(() => fixture.Mesh.UpsertLink("A", "B", cost));
        Throws<ArgumentException>(() => fixture.Mesh.UpsertLink("A", "A", 1));
        Throws<ArgumentException>(() => fixture.Mesh.FindRoute("A", "GATEWAY", "unknown"));
        Throws<KeyNotFoundException>(() => fixture.Mesh.FindRoute("MISSING"));
        Equal(before, fixture.Mesh.GetTopology().Links.Count, "Rejected graph edits leave topology intact");
        fixture.Mesh.UpsertLink("B", "A", 3);
        var edge = fixture.Mesh.GetTopology().Links.Single(link => EdgeKey(link) == "A|B");
        Equal(3d, edge.Cost, "Reverse-endpoint update changes the same undirected edge");
        Equal(before, fixture.Mesh.GetTopology().Links.Count, "Reverse update does not duplicate edges");
    }

    private static void SelfAndDeterministicRoutes()
    {
        var fixture = Graph();
        var self = fixture.Mesh.FindRoute("A", "A", "dijkstra");
        Require(self.Found, "An online node can reach itself.");
        Equal(0, self.Hops, "Self-route hop count");
        Equal(0d, self.TotalCost, "Self-route cost");
        Sequence(new[] { "A" }, self.Path, "Self-route node identity");
        var expected = fixture.Mesh.FindRoute("C", "GATEWAY", "bfs").Path;
        for (var iteration = 0; iteration < 20; iteration++)
            Sequence(expected, fixture.Mesh.FindRoute("C", "GATEWAY", "bfs").Path, "Stable BFS tie order");
    }

    private static void ConcurrentGraph()
    {
        var fixture = Graph();
        var readers = Task.Run(() =>
        {
            for (var iteration = 0; iteration < 100; iteration++)
            {
                var snapshot = fixture.Mesh.GetTopology();
                Equal(4, snapshot.Nodes.Count, "Concurrent graph snapshot node count");
                Equal(5, snapshot.Links.Count, "Concurrent graph snapshot edge count");
            }
        });
        var routes = Task.Run(() =>
        {
            for (var iteration = 0; iteration < 100; iteration++) Require(fixture.Mesh.FindRoute("A", "GATEWAY", "dijkstra").Found, "Concurrent link update must preserve reachable route.");
        });
        var writer = Task.Run(() =>
        {
            for (var iteration = 0; iteration < 100; iteration++) fixture.Mesh.UpsertLink("A", "B", 1 + iteration % 2);
        });
        Task.WhenAll(readers, routes, writer).GetAwaiter().GetResult();
    }

    private static void ConcurrentRegistrations()
    {
        var fixture = Graph();
        var writer = Task.Run(() =>
        {
            for (var iteration = 0; iteration < 60; iteration++)
            {
                var id = "Concurrent-" + iteration;
                fixture.Engine.RegisterDevice(new SensorRegistration
                {
                    DeviceId = id, MacAddress = $"04:00:00:00:00:{iteration:X2}",
                    Location = "Concurrent mesh", Category = SensorCategory.SoilMoisture
                });
                fixture.Engine.Enqueue(new TelemetryPacket<float>(id, SensorCategory.SoilMoisture, 10, "%"));
            }
        });
        var readers = Task.Run(() =>
        {
            for (var iteration = 0; iteration < 100; iteration++)
            {
                var snapshot = fixture.Mesh.GetTopology();
                var ids = snapshot.Nodes.Select(node => node.DeviceId).ToHashSet();
                Require(snapshot.Links.All(edge => ids.Contains(edge.SourceId) && ids.Contains(edge.TargetId)),
                    "Every edge endpoint must belong to the same immutable topology snapshot.");
                var traversal = fixture.Mesh.Traverse();
                Require(traversal.ReachableDeviceIds.Contains("GATEWAY"), "Concurrent traversal retains gateway source.");
            }
        });
        Task.WhenAll(writer, readers).GetAwaiter().GetResult();
        Equal(64, fixture.Mesh.GetTopology().Nodes.Count, "All concurrent registrations visible after completion");
    }

    private static (RealTimeEngine Engine, MeshNetworkService Mesh) Graph()
    {
        var store = new TelemetryStore();
        store.AddSensor(Sensor("A", 1));
        store.AddSensor(Sensor("B", 2));
        store.AddSensor(Sensor("C", 3));
        var engine = new RealTimeEngine(store);
        foreach (var id in new[] { "A", "B", "C" }) engine.Enqueue(Packet(id, 20));
        engine.ProcessPending();
        var mesh = new MeshNetworkService(store, engine);
        foreach (var edge in mesh.GetTopology().Links) mesh.RemoveLink(edge.SourceId, edge.TargetId);
        mesh.UpsertLink("A", "GATEWAY", 10);
        mesh.UpsertLink("A", "B", 1);
        mesh.UpsertLink("B", "GATEWAY", 1);
        mesh.UpsertLink("B", "C", 2);
        mesh.UpsertLink("C", "GATEWAY", 5);
        return (engine, mesh);
    }

    private static string EdgeKey(MeshLink edge) => string.CompareOrdinal(edge.SourceId, edge.TargetId) < 0
        ? edge.SourceId + "|" + edge.TargetId : edge.TargetId + "|" + edge.SourceId;

    private static SensorRegistration Sensor(string id, int suffix) => new()
    {
        DeviceId = id, MacAddress = $"02:00:00:00:00:{suffix:X2}", Category = SensorCategory.Temperature, Location = "Mesh test " + id
    };

    private static TelemetryPacket<float> Packet(string id, float value, DateTime? timestamp = null) => new(id, SensorCategory.Temperature, value, "C", timestamp);
    private static TelemetryLog Log(long sequence, DateTime? timestamp = null) => new("packet-" + sequence, "AVL", SensorCategory.Temperature,
        timestamp ?? Origin.AddSeconds(sequence), Origin, sequence, sequence.ToString(), "C", false, sequence);

    private static void Require(bool condition, string message)
    {
        Interlocked.Increment(ref _assertions);
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void Equal<T>(T expected, T actual, string message) => Require(EqualityComparer<T>.Default.Equals(expected, actual), $"{message}: expected {expected}, actual {actual}.");
    private static void Sequence<T>(IEnumerable<T> expected, IEnumerable<T> actual, string message) => Require(expected.SequenceEqual(actual), message);
    private static void Throws<T>(Action action) where T : Exception
    {
        Interlocked.Increment(ref _assertions);
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
