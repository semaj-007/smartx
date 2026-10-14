using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SmartX.Desktop.Controls;

/// <summary>The "Dynamic Engagement Feature": a live radar-style pulse that sweeps and blips per telemetry packet, showing rate and rhythm rather than a single value.</summary>
public class PulseGaugeControl : Control
{
    private readonly List<Blip> _blips = new();
    private readonly System.Windows.Forms.Timer _timer;
    private double _sweepAngle;
    private int _packetsThisSecond;
    private int _lastRate;
    private DateTime _lastRateReset = DateTime.UtcNow;

    private class Blip
    {
        public double Angle;
        public double Life = 1.0;
    }

    public PulseGaugeControl()
    {
        // Kept deliberately dark against the light theme, like an instrument panel, for higher sweep/blip contrast.
        DoubleBuffered = true;
        BackColor = Color.FromArgb(22, 28, 25);

        // Pulse sweep timer (Microsoft, n.d.)
        _timer = new System.Windows.Forms.Timer { Interval = 40 };
        _timer.Tick += (s, e) => Tick();
        _timer.Start();
    }

    /// <summary>Called by the dashboard every time a new telemetry packet is observed.</summary>
    public void RegisterPing()
    {
        _blips.Add(new Blip { Angle = Random.Shared.NextDouble() * 360.0, Life = 1.0 });
        _packetsThisSecond++;
    }

    private void Tick()
    {
        _sweepAngle = (_sweepAngle + 4) % 360;

        for (int i = _blips.Count - 1; i >= 0; i--)
        {
            _blips[i].Life -= 0.02;
            if (_blips[i].Life <= 0)
                _blips.RemoveAt(i);
        }

        if ((DateTime.UtcNow - _lastRateReset).TotalSeconds >= 1)
        {
            _lastRate = _packetsThisSecond;
            _packetsThisSecond = 0;
            _lastRateReset = DateTime.UtcNow;
        }

        Invalidate();
    }

    // Custom GDI+ drawing (GeeksforGeeks, 2019)
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var center = new PointF(Width / 2f, Height / 2f);
        float radius = Math.Min(Width, Height) / 2f - 12;
        if (radius <= 0) return;

        using (var bg = new SolidBrush(BackColor))
            g.FillRectangle(bg, ClientRectangle);

        using (var borderPen = new Pen(SmartX.Desktop.UiTheme.Border, 1))
            g.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);

        using (var ringPen = new Pen(Color.FromArgb(45, 95, 65), 1))
        {
            for (int i = 1; i <= 4; i++)
            {
                float r = radius * i / 4f;
                g.DrawEllipse(ringPen, center.X - r, center.Y - r, r * 2, r * 2);
            }
        }

        double sweepRad = _sweepAngle * Math.PI / 180.0;
        var sweepEnd = new PointF(
            center.X + radius * (float)Math.Cos(sweepRad),
            center.Y + radius * (float)Math.Sin(sweepRad));

        using (var sweepPen = new Pen(Color.FromArgb(170, 80, 255, 140), 2))
            g.DrawLine(sweepPen, center, sweepEnd);

        foreach (var blip in _blips)
        {
            double blipRad = blip.Angle * Math.PI / 180.0;
            float bx = center.X + radius * 0.78f * (float)Math.Cos(blipRad);
            float by = center.Y + radius * 0.78f * (float)Math.Sin(blipRad);
            int alpha = Math.Max(0, Math.Min(255, (int)(255 * blip.Life)));
            float size = 6 * (float)blip.Life + 2;

            using var blipBrush = new SolidBrush(Color.FromArgb(alpha, 80, 255, 140));
            g.FillEllipse(blipBrush, bx - size / 2, by - size / 2, size, size);
        }

        using var font = new Font("Consolas", 10, FontStyle.Bold);
        using var textBrush = new SolidBrush(Color.FromArgb(210, 255, 225));
        string label = $"{_lastRate} pkt/s";
        var textSize = g.MeasureString(label, font);
        g.DrawString(label, font, textBrush, center.X - textSize.Width / 2, center.Y - textSize.Height / 2);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _timer.Dispose();
        base.Dispose(disposing);
    }
}
