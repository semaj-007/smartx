namespace SmartX.Core.Services;

/// <summary>
/// Demonstrates jagged-array batch capture followed by a transfer into
/// optimised List&lt;T&gt; collections for querying. Each raw batch (one
/// gateway upload session) can hold a different number of readings -
/// exactly what field gateways with intermittent connectivity produce -
/// which is what a jagged array (double[][]), rather than a fixed
/// rectangular double[,], is suited for.
/// </summary>
public class TelemetryHistoryStore
{
    private readonly double[][] _rawBatches;
    public List<List<double>> OptimisedBatches { get; } = new();

    public TelemetryHistoryStore(int batchCount)
    {
        _rawBatches = new double[batchCount][];

        for (int i = 0; i < batchCount; i++)
        {
            int readingsInBatch = Random.Shared.Next(5, 15);
            _rawBatches[i] = new double[readingsInBatch];

            for (int j = 0; j < readingsInBatch; j++)
                _rawBatches[i][j] = Math.Round(Random.Shared.NextDouble() * 100, 2);
        }

        TransferToOptimisedCollections();
    }

    /// <summary>Copies the jagged raw-batch arrays into the optimised List&lt;T&gt; collection used for querying.</summary>
    private void TransferToOptimisedCollections()
    {
        OptimisedBatches.Clear();
        foreach (var batch in _rawBatches)
            OptimisedBatches.Add(new List<double>(batch));
    }

    public double[][] RawBatches => _rawBatches;

    public double GetBatchAverage(int batchIndex)
    {
        if (batchIndex < 0 || batchIndex >= OptimisedBatches.Count)
            return 0;

        var batch = OptimisedBatches[batchIndex];
        return batch.Count == 0 ? 0 : batch.Average();
    }
}
