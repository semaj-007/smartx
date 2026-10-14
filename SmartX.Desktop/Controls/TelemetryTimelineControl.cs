using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;
using SmartX.Desktop.Dtos;

namespace SmartX.Desktop.Controls;

/// <summary>Draws timestamp-ordered numeric telemetry without an external chart dependency.</summary>
public sealed class TelemetryTimelineControl : Control
{
    private ReadingPoint[] _points = [];
    private int _packetCount;
    private int _otherUnitCount;
    private string _unit = string.Empty;

    private readonly record struct ReadingPoint(DateTime TimestampUtc, double Value, string Unit, bool IsCritical);

    public TelemetryTimelineControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = UiTheme.Surface;
        ForeColor = UiTheme.TextPrimary;
        MinimumSize = new Size(220, 130);
        AccessibleName = "Sensor telemetry timeline";
        AccessibleDescription = "Numeric sensor readings in chronological order, with critical readings marked in red.";
    }

    /// <summary>
    /// Copies incoming rows so polling can replace its collection safely. Mixed-unit histories
    /// display the latest numeric reading's unit to avoid comparing incompatible measurements.
    /// </summary>
    public void SetReadings(IReadOnlyList<RealtimeTelemetryRow> readings)
    {
        ArgumentNullException.ThrowIfNull(readings);
        if (IsDisposed || Disposing) return;

        var packetCount = readings.Count;
        var numericPoints = readings
            .Where(row => row.NumericValue is double value && double.IsFinite(value))
            .Select(row => new ReadingPoint(ToUtc(row.TimestampUtc), row.NumericValue!.Value,
                row.Unit ?? string.Empty, row.IsCritical))
            .OrderBy(point => point.TimestampUtc)
            .ToArray();

        void Apply()
        {
            if (IsDisposed || Disposing) return;
            _packetCount = packetCount;
            _unit = numericPoints.Length == 0 ? string.Empty : numericPoints[^1].Unit;
            _points = numericPoints.Where(point => string.Equals(point.Unit, _unit, StringComparison.Ordinal)).ToArray();
            _otherUnitCount = numericPoints.Length - _points.Length;
            AccessibleDescription = _points.Length == 0
                ? "No numeric telemetry is available."
                : $"{_points.Length} numeric readings in {_unit}, ordered by UTC timestamp. Critical readings are marked in red.";
            Invalidate();
        }

        if (InvokeRequired)
        {
            try { BeginInvoke((Action)Apply); }
            catch (InvalidOperationException) when (IsDisposed || Disposing || !IsHandleCreated) { }
        }
        else
        {
            Apply();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(BackColor);
        if (Width < 2 || Height < 2) return;

        using var borderPen = new Pen(UiTheme.Border);
        using var gridPen = new Pen(UiTheme.Border) { DashStyle = DashStyle.Dot };
        using var linePen = new Pen(UiTheme.Accent, 2f);
        using var pointBrush = new SolidBrush(UiTheme.Accent);
        using var criticalBrush = new SolidBrush(UiTheme.Error);
        using var boldFont = new Font(Font, FontStyle.Bold);

        graphics.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);
        var title = string.IsNullOrWhiteSpace(_unit) ? "Sensor timeline" : $"Sensor timeline ({_unit})";
        TextRenderer.DrawText(graphics, title, boldFont, new Rectangle(12, 8, Math.Max(1, Width - 110), 22),
            UiTheme.TextPrimary, TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(graphics, "● Critical", Font, new Rectangle(Width - 96, 10, 85, 20),
            UiTheme.Error, TextFormatFlags.Right | TextFormatFlags.NoPadding);

        if (_points.Length == 0)
        {
            DrawMessage(graphics, _packetCount == 0
                ? "Waiting for numeric sensor telemetry.\nReadings appear after packets reach the API."
                : "These readings have no finite numeric value.\nText and relay states remain available in the telemetry log.");
            return;
        }

        var firstDate = _points[0].TimestampUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var lastDate = _points[^1].TimestampUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string dates = firstDate == lastDate ? firstDate : $"{firstDate} – {lastDate}";
        string detail = $"{_points.Length} readings · {dates} UTC";
        if (_otherUnitCount > 0) detail += $" · {_otherUnitCount} readings in other units hidden";
        TextRenderer.DrawText(graphics, detail, Font, new Rectangle(12, 31, Width - 24, 20),
            UiTheme.TextSecondary, TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

        var plot = new RectangleF(76, 57, Width - 92, Height - 96);
        if (plot.Width < 20 || plot.Height < 20) return;

        double minimum = _points.Min(point => point.Value);
        double maximum = _points.Max(point => point.Value);
        if (minimum == maximum)
        {
            double padding = Math.Max(Math.Abs(minimum) * 0.05, 1);
            double paddedMinimum = minimum - padding;
            double paddedMaximum = maximum + padding;
            if (double.IsFinite(paddedMinimum)) minimum = paddedMinimum;
            if (double.IsFinite(paddedMaximum)) maximum = paddedMaximum;
        }

        for (int index = 0; index <= 4; index++)
        {
            double fraction = index / 4d;
            float y = plot.Bottom - (float)fraction * plot.Height;
            graphics.DrawLine(gridPen, plot.Left, y, plot.Right, y);
            double labelValue = minimum * (1 - fraction) + maximum * fraction;
            TextRenderer.DrawText(graphics, labelValue.ToString("G4", CultureInfo.InvariantCulture), Font,
                new Rectangle(3, (int)y - 9, 65, 20), UiTheme.TextSecondary,
                TextFormatFlags.Right | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }

        graphics.DrawLine(borderPen, plot.Left, plot.Top, plot.Left, plot.Bottom);
        graphics.DrawLine(borderPen, plot.Left, plot.Bottom, plot.Right, plot.Bottom);

        var firstTimestamp = _points[0].TimestampUtc;
        var lastTimestamp = _points[^1].TimestampUtc;
        long spanTicks = lastTimestamp.Ticks - firstTimestamp.Ticks;
        var coordinates = new PointF[_points.Length];
        for (int index = 0; index < _points.Length; index++)
        {
            var point = _points[index];
            double xFraction = spanTicks == 0 ? 0.5 : (point.TimestampUtc.Ticks - firstTimestamp.Ticks) / (double)spanTicks;
            double yFraction = ScaleValue(point.Value, minimum, maximum);
            coordinates[index] = new PointF(plot.Left + (float)xFraction * plot.Width,
                plot.Bottom - (float)yFraction * plot.Height);
        }

        if (coordinates.Length > 1) graphics.DrawLines(linePen, coordinates);
        for (int index = 0; index < coordinates.Length; index++)
        {
            bool isCritical = _points[index].IsCritical;
            float radius = isCritical ? 4f : 2.5f;
            var point = coordinates[index];
            graphics.FillEllipse(isCritical ? criticalBrush : pointBrush,
                point.X - radius, point.Y - radius, radius * 2, radius * 2);
        }

        DrawTimeLabel(graphics, firstTimestamp, new Rectangle((int)plot.Left - 4, (int)plot.Bottom + 8, 100, 20), TextFormatFlags.Left);
        if (plot.Width >= 300 && spanTicks > 0)
        {
            var midpoint = new DateTime(firstTimestamp.Ticks + spanTicks / 2, DateTimeKind.Utc);
            DrawTimeLabel(graphics, midpoint, new Rectangle((int)(plot.Left + plot.Width / 2) - 50,
                (int)plot.Bottom + 8, 100, 20), TextFormatFlags.HorizontalCenter);
        }
        if (spanTicks > 0)
            DrawTimeLabel(graphics, lastTimestamp, new Rectangle((int)plot.Right - 96, (int)plot.Bottom + 8, 100, 20), TextFormatFlags.Right);
    }

    private void DrawMessage(Graphics graphics, string message) =>
        TextRenderer.DrawText(graphics, message, Font, new Rectangle(16, 42, Width - 32, Math.Max(1, Height - 54)),
            UiTheme.TextSecondary, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);

    private void DrawTimeLabel(Graphics graphics, DateTime timestamp, Rectangle bounds, TextFormatFlags alignment) =>
        TextRenderer.DrawText(graphics, timestamp.ToString("HH:mm:ss", CultureInfo.InvariantCulture), Font, bounds,
            UiTheme.TextSecondary, alignment | TextFormatFlags.NoPadding);

    private static DateTime ToUtc(DateTime timestamp) => timestamp.Kind switch
    {
        DateTimeKind.Local => timestamp.ToUniversalTime(),
        DateTimeKind.Unspecified => DateTime.SpecifyKind(timestamp, DateTimeKind.Utc),
        _ => timestamp
    };

    private static double ScaleValue(double value, double minimum, double maximum)
    {
        double range = maximum - minimum;
        double fraction = double.IsFinite(range)
            ? (range == 0 ? 0.5 : (value - minimum) / range)
            : (value / 2 - minimum / 2) / (maximum / 2 - minimum / 2);
        return Math.Clamp(fraction, 0, 1);
    }
}
