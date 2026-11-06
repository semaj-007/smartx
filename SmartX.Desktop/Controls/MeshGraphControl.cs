using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Globalization;
using SmartX.Desktop.Dtos;

namespace SmartX.Desktop.Controls;

/// <summary>Interactive, deterministic rendering of the simulated wireless mesh.</summary>
public sealed class MeshGraphControl : Control
{
    private const float NodeRadius = 14;
    private static readonly Color GatewayColor = Color.FromArgb(48, 102, 176);
    private static readonly Color TreeColor = Color.FromArgb(0, 136, 143);
    private static readonly Color RouteColor = Color.FromArgb(217, 111, 14);
    private readonly ToolTip _tooltip = new() { AutoPopDelay = 12000 };
    private readonly Dictionary<string, PointF> _positions = new(StringComparer.OrdinalIgnoreCase);
    private NodeView[] _nodes = [];
    private LinkView[] _links = [];
    private HashSet<string> _treeEdges = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _routeEdges = new(StringComparer.OrdinalIgnoreCase);
    private string? _selectedDeviceId;
    private string? _hoveredDeviceId;
    private bool _showSpanningTree = true;

    private sealed record NodeView(string Id, string Mac, string Location, bool Online, bool Gateway, string[] Alerts);
    private sealed record LinkView(string Source, string Target, double Cost, bool Enabled, bool Available);

    public event EventHandler<string>? NodeSelected;

    public MeshGraphControl()
    {
        // Double buffering and custom paint events keep topology refreshes smooth (Microsoft, n.d.-k).
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = UiTheme.Surface;
        ForeColor = UiTheme.TextPrimary;
        MinimumSize = new Size(220, 160);
        Cursor = Cursors.Hand;
        AccessibleName = "IoT mesh network graph";
        AccessibleDescription = "Select a sensor to inspect its routes and historical readings. Online nodes are green, the gateway blue, and offline nodes grey.";
    }

    [DefaultValue(true)]
    public bool ShowSpanningTree
    {
        get => _showSpanningTree;
        set { if (_showSpanningTree == value) return; _showSpanningTree = value; Invalidate(); }
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? SelectedDeviceId
    {
        get => _selectedDeviceId;
        set
        {
            if (string.Equals(_selectedDeviceId, value, StringComparison.OrdinalIgnoreCase)) return;
            _selectedDeviceId = value;
            Invalidate();
        }
    }

    public void SetTopology(MeshTopologyDto topology)
    {
        ArgumentNullException.ThrowIfNull(topology);
        if (IsDisposed || Disposing) return;
        // Local copies prevent later HTTP responses from mutating an in-progress drawing.
        _nodes = topology.Nodes.Select(node => new NodeView(node.DeviceId, node.MacAddress, node.Location,
                node.IsConnected, node.IsGateway, node.ActiveAlerts.ToArray()))
            .OrderBy(node => node.Id, StringComparer.OrdinalIgnoreCase).ToArray();
        _links = topology.Links.Select(link => new LinkView(link.SourceId, link.TargetId, link.Cost,
            link.Enabled, link.IsAvailable)).ToArray();
        _treeEdges = topology.SpanningTree.Links.Select(link => EdgeKey(link.SourceId, link.TargetId))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        RebuildLayout();
        Invalidate();
    }

    public void SetRoute(MeshRouteDto? route)
    {
        if (IsDisposed || Disposing) return;
        _routeEdges.Clear();
        if (route is { Found: true })
            for (var index = 1; index < route.Path.Count; index++)
                _routeEdges.Add(EdgeKey(route.Path[index - 1], route.Path[index]));
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        // WinForms can invoke OnResize from base construction, before field initialization.
        if (_positions is not null) RebuildLayout();
    }

    private void RebuildLayout()
    {
        _positions.Clear();
        if (_nodes.Length == 0 || Width < 2 || Height < 2) return;
        var gateway = _nodes.FirstOrDefault(node => node.Gateway);
        var sensors = _nodes.Where(node => gateway is null || node.Id != gateway.Id).ToArray();
        var plot = new RectangleF(65, 64, Math.Max(1, Width - 130), Math.Max(1, Height - 126));
        var centre = new PointF(plot.Left + plot.Width / 2, plot.Top + plot.Height / 2);
        if (gateway is not null) _positions[gateway.Id] = centre;
        if (sensors.Length == 1 && gateway is null) { _positions[sensors[0].Id] = centre; return; }

        // A fixed ID ordering keeps the graph stable while connection states change.
        var ringCount = Math.Max(1, (sensors.Length + 13) / 14);
        var maximumRadiusX = plot.Width / 2;
        var maximumRadiusY = plot.Height / 2;
        for (var index = 0; index < sensors.Length; index++)
        {
            var ring = index / 14;
            var first = ring * 14;
            var inRing = Math.Min(14, sensors.Length - first);
            var fraction = (ring + 1d) / ringCount;
            var angle = -Math.PI / 2 + (index - first) * Math.PI * 2 / Math.Max(1, inRing);
            _positions[sensors[index].Id] = new PointF(centre.X + (float)(Math.Cos(angle) * maximumRadiusX * fraction),
                centre.Y + (float)(Math.Sin(angle) * maximumRadiusY * fraction));
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var graphics = e.Graphics;
        graphics.Clear(BackColor);
        if (Width < 2 || Height < 2) return;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var border = new Pen(UiTheme.Border);
        graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        DrawLegend(graphics);
        if (_nodes.Length == 0)
        {
            TextRenderer.DrawText(graphics, "Waiting for the mesh topology. Start the API and device simulator.", Font,
                new Rectangle(15, 62, Math.Max(1, Width - 30), Math.Max(1, Height - 90)), UiTheme.TextSecondary,
                TextFormatFlags.WordBreak | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        // Weighted edges, selected routes and spanning-tree overlays use GDI+ drawing (Microsoft, n.d.-k).
        using var ordinary = new Pen(Color.FromArgb(185, 196, 189), 1.2f);
        using var unavailable = new Pen(Color.FromArgb(205, 208, 205), 1f) { DashStyle = DashStyle.Dash };
        using var tree = new Pen(TreeColor, 3f);
        using var route = new Pen(RouteColor, 4.5f);
        foreach (var link in _links) DrawLink(graphics, link, link.Available && link.Enabled ? ordinary : unavailable);
        if (_showSpanningTree)
            foreach (var link in _links.Where(link => link.Available && link.Enabled && _treeEdges.Contains(EdgeKey(link.Source, link.Target))))
                DrawLink(graphics, link, tree);
        foreach (var link in _links.Where(link => link.Available && link.Enabled && _routeEdges.Contains(EdgeKey(link.Source, link.Target))))
            DrawLink(graphics, link, route);
        // Put costs above the overlays so a thick selected route never hides its weights.
        foreach (var link in _links) DrawWeight(graphics, link);

        using var outline = new Pen(Color.White, 2);
        using var selectedOutline = new Pen(RouteColor, 3);
        using var alertOutline = new Pen(UiTheme.Error, 2);
        using var labelFont = new Font(Font.FontFamily, Math.Max(8, Font.Size - 0.5f), FontStyle.Bold);
        foreach (var node in _nodes)
        {
            if (!_positions.TryGetValue(node.Id, out var position)) continue;
            var fill = node.Gateway ? GatewayColor : node.Online ? UiTheme.Accent : UiTheme.DisabledText;
            using var brush = new SolidBrush(fill);
            var bounds = new RectangleF(position.X - NodeRadius, position.Y - NodeRadius, NodeRadius * 2, NodeRadius * 2);
            graphics.FillEllipse(brush, bounds);
            graphics.DrawEllipse(string.Equals(node.Id, _selectedDeviceId, StringComparison.OrdinalIgnoreCase)
                ? selectedOutline : node.Alerts.Length > 0 ? alertOutline : outline, bounds);
            if (node.Gateway)
                TextRenderer.DrawText(graphics, "G", labelFont, Rectangle.Round(bounds), Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            var labelBounds = new Rectangle((int)position.X - 70, (int)(position.Y + NodeRadius + 3), 140, 21);
            TextRenderer.DrawText(graphics, node.Id, labelFont, labelBounds, UiTheme.TextPrimary, BackColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }
        TextRenderer.DrawText(graphics, "Click a node · wireless links and costs are simulated · dashed links are unavailable", Font,
            new Rectangle(9, Height - 27, Math.Max(1, Width - 18), 21), UiTheme.TextSecondary,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
    }

    private void DrawLegend(Graphics graphics)
    {
        using var bold = new Font(Font, FontStyle.Bold);
        TextRenderer.DrawText(graphics, "Live mesh network", bold, new Rectangle(10, 8, Math.Max(1, Width - 20), 22),
            UiTheme.TextPrimary, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        var items = new (string Label, Color Color)[] { ("Gateway", GatewayColor), ("Online", UiTheme.Accent),
            ("Offline", UiTheme.DisabledText), ("Route", RouteColor), ("MST", TreeColor) };
        var x = 12;
        foreach (var item in items)
        {
            using var brush = new SolidBrush(item.Color);
            graphics.FillRectangle(brush, x, 37, 10, 10);
            TextRenderer.DrawText(graphics, item.Label, Font, new Point(x + 15, 33), UiTheme.TextSecondary,
                TextFormatFlags.NoPadding);
            x += TextRenderer.MeasureText(item.Label, Font).Width + 27;
        }
    }

    private void DrawLink(Graphics graphics, LinkView link, Pen pen)
    {
        if (!_positions.TryGetValue(link.Source, out var source) || !_positions.TryGetValue(link.Target, out var target)) return;
        graphics.DrawLine(pen, source, target);
    }

    private void DrawWeight(Graphics graphics, LinkView link)
    {
        if (!_positions.TryGetValue(link.Source, out var source) || !_positions.TryGetValue(link.Target, out var target)) return;
        var midpoint = new Point((int)((source.X + target.X) / 2) + 3, (int)((source.Y + target.Y) / 2) - 9);
        TextRenderer.DrawText(graphics, link.Cost.ToString("0.##", CultureInfo.InvariantCulture), Font,
            new Rectangle(midpoint, new Size(50, 20)), link.Available ? UiTheme.TextSecondary : UiTheme.DisabledText,
            BackColor, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
    }

    private NodeView? HitTest(Point location) => _nodes.FirstOrDefault(node =>
        _positions.TryGetValue(node.Id, out var point) && Math.Pow(point.X - location.X, 2) + Math.Pow(point.Y - location.Y, 2)
        <= Math.Pow(NodeRadius + 6, 2));

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button != MouseButtons.Left || HitTest(e.Location) is not { } node) return;
        SelectedDeviceId = node.Id;
        NodeSelected?.Invoke(this, node.Id);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var node = HitTest(e.Location);
        if (node?.Id == _hoveredDeviceId) return;
        _hoveredDeviceId = node?.Id;
        _tooltip.SetToolTip(this, node is null ? "" : $"{node.Id}\nMAC: {node.Mac}\n{node.Location}\n"
            + (node.Gateway ? "Gateway" : node.Online ? "Online" : "Offline")
            + (node.Alerts.Length == 0 ? "" : "\nAlerts: " + string.Join(", ", node.Alerts)));
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hoveredDeviceId = null;
        _tooltip.SetToolTip(this, "");
    }

    private static string EdgeKey(string first, string second) => string.Compare(first, second, StringComparison.OrdinalIgnoreCase) <= 0
        ? first + "|" + second : second + "|" + first;

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tooltip.Dispose();
        base.Dispose(disposing);
    }
}
