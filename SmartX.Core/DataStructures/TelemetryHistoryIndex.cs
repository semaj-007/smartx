using SmartX.Core.Models;

namespace SmartX.Core.DataStructures;

/// <summary>
/// Capacity-bounded AVL tree keyed by UTC timestamp and unique processing sequence.
/// The owning RealTimeEngine serializes access. Reads return independent snapshots.
/// Insert/delete take O(log n); a range query prunes unrelated subtrees in O(log n + k).
/// </summary>
public sealed class TelemetryHistoryIndex
{
    private Node? _root;
    private readonly int _capacity;
    private int _count;

    // Ordered minute summaries retain the Part 2 SortedDictionary use without
    // keeping a second copy of every telemetry log (Microsoft, n.d.-v).
    private readonly SortedDictionary<DateTime, int> _minuteCounts = new();

    public TelemetryHistoryIndex(int capacity = 1000)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
    }

    public int Count => _count;
    public int Height => GetHeight(_root);
    public int Capacity => _capacity;
    public IReadOnlyDictionary<DateTime, int> TimeBuckets => new SortedDictionary<DateTime, int>(_minuteCounts);

    public void Add(TelemetryLog reading)
    {
        ArgumentNullException.ThrowIfNull(reading);
        reading = reading with { TimestampUtc = ToUtc(reading.TimestampUtc) };
        // AVL insertion rebalances each ancestor, preserving logarithmic tree height
        // even when live sensor timestamps arrive in increasing order (Shaffer, n.d.-a).
        _root = Insert(_root, reading);
        _count++;
        var minute = MinuteOf(reading.TimestampUtc);
        _minuteCounts[minute] = _minuteCounts.GetValueOrDefault(minute) + 1;

        if (_count > _capacity)
        {
            _root = RemoveMinimum(_root!, out var removed);
            _count--;
            var removedMinute = MinuteOf(removed.TimestampUtc);
            if (_minuteCounts[removedMinute] == 1) _minuteCounts.Remove(removedMinute);
            else _minuteCounts[removedMinute]--;
        }
    }

    /// <summary>Retrieves the newest count readings in ascending timestamp/sequence order.</summary>
    public IReadOnlyList<TelemetryLog> Latest(int count)
    {
        if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));
        var readings = new List<TelemetryLog>(Math.Min(count, _count));
        CollectLatest(_root, count, readings);
        readings.Reverse();
        return readings.ToArray();
    }

    /// <summary>Inclusive UTC bounds; one extra match establishes whether the result is truncated.</summary>
    public TelemetryRangeResult Query(DateTime fromUtc, DateTime toUtc, int limit = 1000)
    {
        if (limit < 1) throw new ArgumentOutOfRangeException(nameof(limit));
        fromUtc = ToUtc(fromUtc);
        toUtc = ToUtc(toUtc);
        if (fromUtc > toUtc) throw new ArgumentException("The start timestamp must precede or equal the end timestamp.");
        var readings = new List<TelemetryLog>(Math.Min(limit, _count));
        var visited = 0;
        // Binary-search ordering skips subtrees whose timestamps cannot lie in the
        // requested interval, rather than filtering an unordered full log (Shaffer, n.d.-b).
        CollectRange(_root, fromUtc, toUtc, limit, readings, ref visited);
        var truncated = readings.Count > limit;
        if (truncated) readings.RemoveAt(readings.Count - 1);
        return new TelemetryRangeResult(readings.ToArray(), _count, visited, Height, truncated);
    }

    private static Node Insert(Node? node, TelemetryLog reading)
    {
        if (node is null) return new Node(reading);
        var comparison = Compare(reading, node.Reading);
        if (comparison < 0) node.Left = Insert(node.Left, reading);
        else if (comparison > 0) node.Right = Insert(node.Right, reading);
        else throw new ArgumentException("A reading with this timestamp and processing sequence is already indexed.", nameof(reading));
        return Balance(node);
    }

    private static Node? RemoveMinimum(Node node, out TelemetryLog removed)
    {
        if (node.Left is null)
        {
            removed = node.Reading;
            return node.Right;
        }
        node.Left = RemoveMinimum(node.Left, out removed);
        return Balance(node);
    }

    private static Node Balance(Node node)
    {
        UpdateHeight(node);
        var factor = GetHeight(node.Left) - GetHeight(node.Right);
        // Single and double AVL rotations restore the height difference invariant
        // after insertion and oldest-reading eviction (Shaffer, n.d.-a).
        if (factor > 1)
        {
            if (GetHeight(node.Left!.Left) < GetHeight(node.Left.Right))
                node.Left = RotateLeft(node.Left);
            return RotateRight(node);
        }
        if (factor < -1)
        {
            if (GetHeight(node.Right!.Right) < GetHeight(node.Right.Left))
                node.Right = RotateRight(node.Right);
            return RotateLeft(node);
        }
        return node;
    }

    private static Node RotateLeft(Node node)
    {
        var pivot = node.Right!;
        node.Right = pivot.Left;
        pivot.Left = node;
        UpdateHeight(node);
        UpdateHeight(pivot);
        return pivot;
    }

    private static Node RotateRight(Node node)
    {
        var pivot = node.Left!;
        node.Left = pivot.Right;
        pivot.Right = node;
        UpdateHeight(node);
        UpdateHeight(pivot);
        return pivot;
    }

    private static void CollectLatest(Node? node, int limit, List<TelemetryLog> readings)
    {
        if (node is null || readings.Count >= limit) return;
        CollectLatest(node.Right, limit, readings);
        if (readings.Count >= limit) return;
        readings.Add(node.Reading);
        CollectLatest(node.Left, limit, readings);
    }

    private static void CollectRange(Node? node, DateTime fromUtc, DateTime toUtc, int limit,
        List<TelemetryLog> readings, ref int visited)
    {
        if (node is null || readings.Count > limit) return;
        visited++;
        var timestamp = node.Reading.TimestampUtc;
        if (timestamp >= fromUtc)
            CollectRange(node.Left, fromUtc, toUtc, limit, readings, ref visited);
        if (readings.Count > limit) return;
        if (timestamp >= fromUtc && timestamp <= toUtc) readings.Add(node.Reading);
        if (timestamp <= toUtc)
            CollectRange(node.Right, fromUtc, toUtc, limit, readings, ref visited);
    }

    private static int Compare(TelemetryLog left, TelemetryLog right)
    {
        var timestamp = left.TimestampUtc.CompareTo(right.TimestampUtc);
        return timestamp != 0 ? timestamp : left.Sequence.CompareTo(right.Sequence);
    }

    private static int GetHeight(Node? node) => node?.Height ?? 0;
    private static void UpdateHeight(Node node) => node.Height = 1 + Math.Max(GetHeight(node.Left), GetHeight(node.Right));
    private static DateTime MinuteOf(DateTime timestamp) => new(timestamp.Ticks - timestamp.Ticks % TimeSpan.TicksPerMinute, DateTimeKind.Utc);
    private static DateTime ToUtc(DateTime timestamp) => timestamp.Kind switch
    {
        DateTimeKind.Local => timestamp.ToUniversalTime(),
        DateTimeKind.Unspecified => DateTime.SpecifyKind(timestamp, DateTimeKind.Utc),
        _ => timestamp
    };

    private sealed class Node(TelemetryLog reading)
    {
        public TelemetryLog Reading { get; } = reading;
        public Node? Left { get; set; }
        public Node? Right { get; set; }
        public int Height { get; set; } = 1;
    }
}
