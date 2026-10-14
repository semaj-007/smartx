namespace SmartX.Core.Models;

/// <summary>
/// Lightweight, aggregation-friendly reading used to demonstrate operator
/// overloading on sensor data. Two SensorReadings from compatible meters
/// can be summed (aggregate load), subtracted (delta) and compared
/// directly in code, e.g.:
///
///     var aggregateLoad = Meter1 + Meter2;
///     var delta          = Meter1 - Meter2;
///     bool meter1Higher  = Meter1 > Meter2;
/// </summary>
public struct SensorReading
{
    public string SensorId { get; set; }
    public double Value { get; set; }
    public string Unit { get; set; }

    public SensorReading(string sensorId, double value, string unit)
    {
        SensorId = sensorId;
        Value = value;
        Unit = unit;
    }

    public static SensorReading operator +(SensorReading a, SensorReading b)
    {
        EnsureCompatible(a, b);
        return new SensorReading($"{a.SensorId}+{b.SensorId}", a.Value + b.Value, a.Unit);
    }

    public static SensorReading operator -(SensorReading a, SensorReading b)
    {
        EnsureCompatible(a, b);
        return new SensorReading($"{a.SensorId}-{b.SensorId}", a.Value - b.Value, a.Unit);
    }

    public static bool operator >(SensorReading a, SensorReading b)
    {
        EnsureCompatible(a, b);
        return a.Value > b.Value;
    }

    public static bool operator <(SensorReading a, SensorReading b)
    {
        EnsureCompatible(a, b);
        return a.Value < b.Value;
    }

    public static bool operator ==(SensorReading a, SensorReading b) => a.Value == b.Value && a.Unit == b.Unit;

    public static bool operator !=(SensorReading a, SensorReading b) => !(a == b);

    private static void EnsureCompatible(SensorReading a, SensorReading b)
    {
        if (a.Unit != b.Unit)
            throw new InvalidOperationException($"Cannot combine readings with mismatched units ('{a.Unit}' vs '{b.Unit}').");
    }

    public override bool Equals(object? obj) => obj is SensorReading other && this == other;

    public override int GetHashCode() => HashCode.Combine(Value, Unit);

    public override string ToString() => $"{SensorId}: {Value:0.00}{Unit}";
}
