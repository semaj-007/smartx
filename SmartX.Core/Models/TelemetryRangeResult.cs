namespace SmartX.Core.Models;

/// <summary>A chronological range snapshot and diagnostics from the balanced history index.</summary>
public sealed record TelemetryRangeResult(
    IReadOnlyList<TelemetryLog> Readings,
    int RetainedCount,
    int VisitedNodes,
    int TreeHeight,
    bool Truncated);
