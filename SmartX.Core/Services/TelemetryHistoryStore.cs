namespace SmartX.Core.Services;

/// <summary>
/// Bounded gateway batches retain the Part 1 jagged-array demonstration using
/// actual ingested readings. A partially filled final batch explains why the
/// upload representation is jagged rather than a rectangular array.
/// </summary>
public class TelemetryHistoryStore
{
    private readonly object _gate = new();
    private readonly List<List<double>> _batches = new();
    private readonly int _maximumBatches;
    private const int ReadingsPerBatch = 20;

    public TelemetryHistoryStore(int batchCount = 50)
    {
        if (batchCount < 1)
            throw new ArgumentOutOfRangeException(nameof(batchCount));
        _maximumBatches = batchCount;
    }

    /// <summary>Independent snapshots protect readers while telemetry is arriving.</summary>
    public List<List<double>> OptimisedBatches
    {
        get
        {
            lock (_gate)
                return _batches.Select(batch => batch.ToList()).ToList();
        }
    }

    public double[][] RawBatches
    {
        get
        {
            lock (_gate)
                return _batches.Select(batch => batch.ToArray()).ToArray();
        }
    }

    public void AddReading(double reading)
    {
        if (!double.IsFinite(reading))
            throw new ArgumentException("Sensor readings must be finite.", nameof(reading));
        lock (_gate)
        {
            if (_batches.Count == 0 || _batches[^1].Count == ReadingsPerBatch)
            {
                _batches.Add(new List<double>(ReadingsPerBatch));
                if (_batches.Count > _maximumBatches)
                    _batches.RemoveAt(0);
            }
            _batches[^1].Add(reading);
        }
    }

    public double GetBatchAverage(int batchIndex)
    {
        lock (_gate)
        {
            if (batchIndex < 0 || batchIndex >= _batches.Count)
                return 0;
            var batch = _batches[batchIndex];
            return batch.Count == 0 ? 0 : batch.Average();
        }
    }
}
