using SmartX.Desktop.Controls;
using SmartX.Desktop.Dtos;

namespace SmartX.Desktop.Forms;

/// <summary>Interactive simulated wireless mesh, live routing and AVL-indexed telemetry history.</summary>
public sealed class NetworkTopologyForm : ModuleForm
{
    private readonly ApiClient _api = new(ApiClient.DefaultBaseUrl);
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    private CancellationTokenSource? _active;
    private bool _ready, _refreshing, _actionBusy, _binding;
    private string? _selectedId;
    private string? _tracedFrom, _tracedTo, _tracedAlgorithm;
    private string? _discoveredFrom;
    private int _routeRevision, _discoveryRevision;
    private MeshTopologyDto _topology = new();
    private readonly MeshGraphControl _graph = new() { Dock = DockStyle.Fill };
    private readonly TelemetryTimelineControl _timeline = new() { Dock = DockStyle.Fill };
    private readonly DataGridView _nodes = Grid();
    private readonly DataGridView _links = Grid();
    private readonly DataGridView _history = Grid();
    private readonly TextBox _source = new() { Text = "MOISTURE-B", Width = 155, MaxLength = 100 };
    private readonly TextBox _target = new() { Text = "GATEWAY", Width = 130, MaxLength = 100 };
    private readonly ComboBox _algorithm = new() { Width = 125, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _historyDevice = new() { Text = "MOISTURE-B", Width = 150, MaxLength = 100 };
    private readonly DateTimePicker _from = DatePicker(DateTime.UtcNow.AddHours(-1));
    private readonly DateTimePicker _to = DatePicker(DateTime.UtcNow);
    private readonly CheckBox _toNow = new() { Text = "End at current UTC", Checked = true, AutoSize = true };
    private readonly CheckBox _mst = new() { Text = "Show minimum spanning tree / forest", AutoSize = true };
    private readonly Label _metrics = Label();
    private readonly Label _status = Label();
    private readonly Label _route = Label();
    private readonly Label _historyStats = Label();
    private readonly Button _trace = Button("Trace route");
    private readonly Button _query = Button("Query history");
    private readonly Button _start = Button("Start device simulator");
    private readonly Button _stop = Button("Stop simulator");
    private readonly Button _saveLink = Button("Save wireless link");
    private readonly Button _removeLink = Button("Remove link");
    private readonly Button _discover = Button("Discover subnet (BFS)");
    private readonly TextBox _discoveryRoot = new() { Text = "GATEWAY", Width = 180, MaxLength = 100 };
    private readonly TextBox _discoveryResult = new() { Dock = DockStyle.Fill, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical,
        BackColor = UiTheme.Surface, ForeColor = UiTheme.TextPrimary, BorderStyle = BorderStyle.None };
    private readonly TextBox _linkSource = new() { Text = "MOISTURE-B", Width = 135, MaxLength = 100 };
    private readonly TextBox _linkTarget = new() { Text = "GATEWAY", Width = 135, MaxLength = 100 };
    private readonly NumericUpDown _cost = new() { Minimum = 0.01M, Maximum = 100000M, DecimalPlaces = 2, Value = 1, Width = 85 };
    private readonly CheckBox _linkEnabled = new() { Text = "Enabled", Checked = true, AutoSize = true };
    private DateTime _holdStatusUntil;

    public NetworkTopologyForm()
    {
        Text = "Smart-X - Network Topology & Mesh Routing";
        ClientSize = new Size(1460, 930);
        MinimumSize = new Size(1160, 800);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = UiTheme.Background;
        Font = new Font("Segoe UI", 9);
        _algorithm.Items.AddRange(["BFS (fewest hops)", "Dijkstra (least energy)", "Prim backbone path"]);
        _algorithm.SelectedIndex = 0;
        _algorithm.Width = 165;
        _to.Enabled = false;
        _graph.ShowSpanningTree = _mst.Checked;
        BuildLayout();
        _timer.Tick += async (_, _) => await RefreshTopologyAsync();
        _trace.Click += async (_, _) => await RunActionAsync(TraceRouteAsync);
        _query.Click += async (_, _) => await RunActionAsync(QueryHistoryAsync);
        _discover.Click += async (_, _) => await RunActionAsync(DiscoverSubnetAsync);
        _source.TextChanged += (_, _) => InvalidateRoute();
        _target.TextChanged += (_, _) => InvalidateRoute();
        _algorithm.SelectedIndexChanged += (_, _) => InvalidateRoute();
        _discoveryRoot.TextChanged += (_, _) => { ++_discoveryRevision; _discoveredFrom = null; _discoveryResult.Text = "Discovery root changed. Discover the subnet again."; };
        _start.Click += async (_, _) => await RunActionAsync(async token =>
        {
            await _api.SetSimulationAsync(true, token);
            SetStatus("HTTP device simulator started. Live nodes become available after their first reading.", hold: true);
        });
        _stop.Click += async (_, _) => await RunActionAsync(async token =>
        {
            await _api.SetSimulationAsync(false, token);
            SetStatus("Simulator stopped. Devices time out after 15 seconds; routes then exclude them.", hold: true);
        });
        _mst.CheckedChanged += (_, _) => _graph.ShowSpanningTree = _mst.Checked;
        _toNow.CheckedChanged += (_, _) => { _to.Enabled = !_toNow.Checked; if (_toNow.Checked) _to.Value = DateTime.UtcNow; };
        _nodes.SelectionChanged += (_, _) =>
        {
            if (!_binding && _nodes.SelectedRows.Count > 0 && _nodes.SelectedRows[0].Tag is MeshNodeDto node)
                SelectNode(node.DeviceId);
        };
        _graph.NodeSelected += (_, id) => SelectNode(id);
        _links.SelectionChanged += (_, _) =>
        {
            if (_binding || _links.SelectedRows.Count == 0 || _links.SelectedRows[0].Tag is not MeshLinkDto link) return;
            _linkSource.Text = link.SourceId;
            _linkTarget.Text = link.TargetId;
            _cost.Value = Math.Clamp((decimal)link.Cost, _cost.Minimum, _cost.Maximum);
            _linkEnabled.Checked = link.Enabled;
        };
        _saveLink.Click += async (_, _) => await RunActionAsync(async token =>
        {
            await _api.SetMeshLinkAsync(_linkSource.Text.Trim(), _linkTarget.Text.Trim(), (double)_cost.Value, _linkEnabled.Checked, token);
            SetStatus("Simulated wireless link saved. Traced routes and the spanning tree refresh automatically.", hold: true);
        });
        _removeLink.Click += async (_, _) => await RunActionAsync(async token =>
        {
            await _api.RemoveMeshLinkAsync(_linkSource.Text.Trim(), _linkTarget.Text.Trim(), token);
            SetStatus("Simulated link removed. Available alternative paths are used on the next route refresh.", hold: true);
        });
        _source.KeyDown += async (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            await RunActionAsync(TraceRouteAsync);
        };
        _historyDevice.KeyDown += async (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            await RunActionAsync(QueryHistoryAsync);
        };
        _ready = true;
        _route.Text = "Trace an ID/MAC route: BFS minimises hops; Dijkstra minimises route energy; Prim follows the minimum-energy backbone.";
        _discoveryResult.Text = "Discover from GATEWAY to list reachable sensors, offline devices and online devices isolated by disabled or missing links.";
        _historyStats.Text = "Select a sensor, choose UTC bounds, then query its retained history. The graph and log show the same returned readings.";
        UpdateButtons();
    }

    private void BuildLayout()
    {
        var page = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), RowCount = 5, ColumnCount = 1 };
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        Controls.Add(page);
        var heading = Flow();
        var back = Button("< Main Menu");
        back.Click += (_, _) => Close();
        heading.Controls.AddRange([back,
            new Label { Text = "Network Topology & Mesh Routing", AutoSize = true, ForeColor = UiTheme.Accent,
                Font = new Font("Segoe UI", 17, FontStyle.Bold), Padding = new Padding(10, 0, 12, 0) }, _start, _stop]);
        page.Controls.Add(heading, 0, 0);
        page.Controls.Add(_metrics, 0, 1);

        var network = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        network.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56));
        network.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44));
        var graphPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        graphPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        graphPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        graphPanel.Controls.Add(_mst, 0, 0);
        graphPanel.Controls.Add(_graph, 0, 1);
        network.Controls.Add(Card("Simulated wireless network · click a node to select it", graphPanel), 0, 0);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        var routingTab = new TabPage("Routes & devices") { BackColor = UiTheme.Surface };
        var linkTab = new TabPage("Wireless links") { BackColor = UiTheme.Surface };
        var discoveryTab = new TabPage("Subnet discovery") { BackColor = UiTheme.Surface };
        tabs.TabPages.AddRange([routingTab, linkTab, discoveryTab]);
        network.Controls.Add(tabs, 1, 0);
        page.Controls.Add(network, 0, 2);

        var routing = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(7) };
        routing.RowStyles.Add(new RowStyle(SizeType.Absolute, 37));
        routing.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        routing.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        routing.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var endpoints = Flow();
        endpoints.Controls.AddRange([Caption("From ID/MAC"), _source, Caption("To"), _target]);
        var tracing = Flow();
        tracing.Controls.AddRange([_algorithm, _trace]);
        routing.Controls.Add(endpoints, 0, 0);
        routing.Controls.Add(tracing, 0, 1);
        routing.Controls.Add(_route, 0, 2);
        Columns(_nodes, ("Device", 25), ("MAC", 33), ("Category", 24), ("State", 18));
        routing.Controls.Add(_nodes, 0, 3);
        routingTab.Controls.Add(routing);

        var editing = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(7) };
        editing.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        editing.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        editing.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        editing.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var edgeFields = Flow();
        edgeFields.Controls.AddRange([Caption("From"), _linkSource, Caption("To"), _linkTarget]);
        var edgeActions = Flow();
        edgeActions.Controls.AddRange([Caption("Cost"), _cost, _linkEnabled, _saveLink, _removeLink]);
        editing.Controls.Add(edgeFields, 0, 0);
        editing.Controls.Add(edgeActions, 0, 1);
        editing.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "Costs are simulated transmission energy units. Disable/remove a link to demonstrate rerouting. The Prim backbone minimises total active-network energy.", ForeColor = UiTheme.TextSecondary }, 0, 2);
        Columns(_links, ("Source", 27), ("Target", 27), ("Cost", 13), ("Enabled", 16), ("Available", 17));
        editing.Controls.Add(_links, 0, 3);
        linkTab.Controls.Add(editing);

        var discovery = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(7) };
        discovery.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        discovery.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        discovery.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var discoveryFields = Flow();
        discoveryFields.Controls.AddRange([Caption("Root ID/MAC"), _discoveryRoot, _discover]);
        discovery.Controls.Add(discoveryFields, 0, 0);
        discovery.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "BFS traverses available wireless links. Results refresh every second after discovery, so failures and restored paths are visible.", ForeColor = UiTheme.TextSecondary }, 0, 1);
        discovery.Controls.Add(_discoveryResult, 0, 2);
        discoveryTab.Controls.Add(discovery);

        var historyPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        historyPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 37));
        historyPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 33));
        historyPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        historyPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var bounds = Flow();
        bounds.Controls.AddRange([Caption("Sensor ID/MAC"), _historyDevice, Caption("From UTC"), _from, Caption("To UTC"), _to]);
        var historyActions = Flow();
        historyActions.Controls.AddRange([_toNow, _query, new Label { Text = "Inclusive bounds · oldest first · up to 1,000 retained readings per sensor", AutoSize = true, Padding = new Padding(8, 5, 0, 0), ForeColor = UiTheme.TextSecondary }]);
        historyPanel.Controls.Add(bounds, 0, 0);
        historyPanel.Controls.Add(historyActions, 0, 1);
        historyPanel.Controls.Add(_historyStats, 0, 2);
        var historyData = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        historyData.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        historyData.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        Columns(_history, ("UTC timestamp", 28), ("Device", 19), ("Value", 16), ("Unit", 11), ("Priority", 14), ("Seq", 12));
        historyData.Controls.Add(_history, 0, 0);
        historyData.Controls.Add(_timeline, 1, 0);
        historyPanel.Controls.Add(historyData, 0, 3);
        page.Controls.Add(Card("Historical telemetry · AVL timestamp range query", historyPanel), 0, 3);
        page.Controls.Add(_status, 0, 4);
    }

    private static DateTimePicker DatePicker(DateTime value) => new()
    {
        Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd HH:mm:ss", Width = 165, Value = value,
        ShowUpDown = true
    };
    private static FlowLayoutPanel Flow() => new() { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) };
    private static Label Caption(string text) => new() { Text = text, AutoSize = true, Padding = new Padding(3, 5, 4, 0) };
    private static Label Label() => new() { Dock = DockStyle.Fill, ForeColor = UiTheme.TextSecondary, AutoEllipsis = true };
    private static Button Button(string text) => new()
    {
        Text = text, AutoSize = true, MinimumSize = new Size(85, 29), FlatStyle = FlatStyle.Flat,
        BackColor = UiTheme.ButtonSecondaryBack, ForeColor = UiTheme.TextPrimary, Cursor = Cursors.Hand
    };
    private static GroupBox Card(string title, Control content)
    {
        var card = new GroupBox { Text = title, Dock = DockStyle.Fill, Padding = new Padding(9, 23, 9, 9), Margin = new Padding(3), ForeColor = UiTheme.Accent, BackColor = UiTheme.Surface };
        card.Controls.Add(content);
        return card;
    }
    private static DataGridView Grid()
    {
        var grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
            AllowUserToDeleteRows = false, RowHeadersVisible = false, MultiSelect = false, AutoGenerateColumns = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            BackgroundColor = UiTheme.Surface, BorderStyle = BorderStyle.None, GridColor = UiTheme.Border, EnableHeadersVisualStyles = false };
        grid.ColumnHeadersDefaultCellStyle.BackColor = UiTheme.GridHeaderBack;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = UiTheme.TextPrimary;
        grid.DefaultCellStyle.ForeColor = UiTheme.TextPrimary;
        grid.DefaultCellStyle.BackColor = UiTheme.Surface;
        grid.DefaultCellStyle.SelectionBackColor = UiTheme.Accent;
        grid.DefaultCellStyle.SelectionForeColor = Color.White;
        return grid;
    }
    private static void Columns(DataGridView grid, params (string Title, int Weight)[] columns)
    {
        foreach (var (title, weight) in columns)
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = title, HeaderText = title, FillWeight = weight, SortMode = DataGridViewColumnSortMode.NotSortable });
    }

    private void SelectNode(string id)
    {
        var node = _topology.Nodes.FirstOrDefault(n => n.DeviceId == id);
        if (node is null) return;
        _selectedId = id;
        _source.Text = id;
        _graph.SelectedDeviceId = id;
        if (!node.IsGateway) _historyDevice.Text = id;
        _binding = true;
        try
        {
            _nodes.ClearSelection();
            foreach (DataGridViewRow row in _nodes.Rows)
                if (row.Tag is MeshNodeDto candidate && candidate.DeviceId == id)
                {
                    _nodes.CurrentCell = row.Cells[0];
                    row.Selected = true;
                    break;
                }
        }
        finally { _binding = false; }
        SetStatus($"Selected {node.DeviceId} · {node.MacAddress} · {node.Location}. Query history to load its readings.", hold: true);
    }

    // Awaiting network calls keeps the Windows message loop free for navigation (Microsoft, n.d.-c).
    private async Task RefreshTopologyAsync()
    {
        if (_refreshing || !Visible || IsDisposed) return;
        _refreshing = true;
        var token = _active?.Token ?? CancellationToken.None;
        try
        {
            var topologyTask = _api.GetMeshTopologyAsync(token);
            var simulationTask = _api.GetSimulationAsync(token);
            await Task.WhenAll(topologyTask, simulationTask);
            if (token.IsCancellationRequested || IsDisposed || !Visible) return;
            _topology = await topologyTask;
            ApplyTopology();
            var simulation = await simulationTask;
            _start.Enabled = !_actionBusy && !simulation.Running;
            _stop.Enabled = !_actionBusy && simulation.Running;
            if (_toNow.Checked) _to.Value = DateTime.UtcNow;
            if (_tracedFrom is not null && !_actionBusy)
            {
                var routeRevision = _routeRevision;
                var route = await _api.GetMeshRouteAsync(_tracedFrom, _tracedTo!, _tracedAlgorithm!, token);
                if (!token.IsCancellationRequested && !IsDisposed && Visible && routeRevision == _routeRevision) ApplyRoute(route);
            }
            if (_discoveredFrom is not null && !_actionBusy)
            {
                var discoveryRevision = _discoveryRevision;
                var result = await _api.DiscoverMeshSubnetAsync(_discoveredFrom, token);
                if (!token.IsCancellationRequested && !IsDisposed && Visible && discoveryRevision == _discoveryRevision) ApplyDiscovery(result);
            }
            if (DateTime.UtcNow >= _holdStatusUntil)
                SetStatus(simulation.LastError is { Length: > 0 } error ? $"Simulator: {error}" : $"Live topology · {DateTime.Now:T} · wireless links and costs are simulated", simulation.LastError is { Length: > 0 });
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { SetStatus($"Mesh refresh failed: {ex.Message}", error: true); }
        finally { _refreshing = false; }
    }

    private void ApplyTopology()
    {
        _graph.SetTopology(_topology);
        var mst = _topology.SpanningTree;
        _metrics.Text = $"{_topology.Nodes.Count(n => !n.IsGateway && n.IsConnected)} / {_topology.Nodes.Count(n => !n.IsGateway)} sensors online · {_topology.Links.Count(l => l.IsAvailable)} available links · "
            + $"Prim forest: {mst.Links.Count} edges, {mst.TotalCost:0.##} energy units, {mst.ComponentCount} active component(s)";
        int scroll = Math.Max(0, _nodes.FirstDisplayedScrollingRowIndex);
        string? selectedLink = _links.SelectedRows.Count > 0 && _links.SelectedRows[0].Tag is MeshLinkDto selected ? $"{selected.SourceId}|{selected.TargetId}" : null;
        int linkScroll = Math.Max(0, _links.FirstDisplayedScrollingRowIndex);
        _binding = true;
        try
        {
            _nodes.Rows.Clear();
            foreach (var node in _topology.Nodes)
            {
                var row = _nodes.Rows[_nodes.Rows.Add(node.DeviceId, node.MacAddress, node.Category, node.IsConnected ? "Online" : "Offline")];
                row.Tag = node;
                row.Cells[0].ToolTipText = $"{node.Location}\nLast seen UTC: {node.LastSeenUtc?.ToString("u") ?? "awaiting telemetry"}\n{string.Join(", ", node.ActiveAlerts)}";
                if (!node.IsConnected) row.DefaultCellStyle.ForeColor = UiTheme.TextSecondary;
            }
            _nodes.ClearSelection();
            if (_selectedId is not null)
                foreach (DataGridViewRow row in _nodes.Rows)
                    if (row.Tag is MeshNodeDto node && node.DeviceId == _selectedId) { _nodes.CurrentCell = row.Cells[0]; row.Selected = true; break; }
            if (_nodes.Rows.Count > 0) _nodes.FirstDisplayedScrollingRowIndex = Math.Min(scroll, _nodes.Rows.Count - 1);
            _links.Rows.Clear();
            foreach (var link in _topology.Links)
            {
                var row = _links.Rows[_links.Rows.Add(link.SourceId, link.TargetId, link.Cost, link.Enabled, link.IsAvailable)];
                row.Tag = link;
                if ($"{link.SourceId}|{link.TargetId}" == selectedLink) { _links.CurrentCell = row.Cells[0]; row.Selected = true; }
            }
            if (_links.Rows.Count > 0) _links.FirstDisplayedScrollingRowIndex = Math.Min(linkScroll, _links.Rows.Count - 1);
        }
        finally { _binding = false; }
    }

    private async Task TraceRouteAsync(CancellationToken token)
    {
        var from = _source.Text.Trim();
        var to = _target.Text.Trim();
        if (from.Length == 0 || to.Length == 0) throw new ArgumentException("Enter both route endpoints using a device ID or MAC address.");
        var algorithm = _algorithm.SelectedIndex switch { 1 => "dijkstra", 2 => "mst", _ => "bfs" };
        var revision = ++_routeRevision;
        var route = await _api.GetMeshRouteAsync(from, to, algorithm, token);
        if (token.IsCancellationRequested || IsDisposed || !Visible || revision != _routeRevision) return;
        _tracedFrom = from;
        _tracedTo = to;
        _tracedAlgorithm = algorithm;
        ApplyRoute(route);
        SetStatus(route.Found ? "Route traced. The highlighted path refreshes with connectivity and link changes." : route.Message, !route.Found, hold: true);
    }

    private void ApplyRoute(MeshRouteDto route)
    {
        _graph.SetRoute(route);
        _route.Text = route.Found
            ? $"{route.Algorithm.ToUpperInvariant()} · {route.Hops} hop(s) · {route.TotalCost:0.##} energy units · {route.VisitedCount} visited\n{string.Join(" → ", route.Path)}"
            : $"No route: {route.Message}";
        _route.ForeColor = route.Found ? UiTheme.TextPrimary : UiTheme.Error;
    }

    private void InvalidateRoute()
    {
        ++_routeRevision;
        _tracedFrom = null;
        _graph.SetRoute(null);
        _route.Text = "Route inputs changed. Trace the route again to apply these endpoints and algorithm.";
        _route.ForeColor = UiTheme.TextSecondary;
    }

    private async Task DiscoverSubnetAsync(CancellationToken token)
    {
        var from = _discoveryRoot.Text.Trim();
        if (from.Length == 0) throw new ArgumentException("Enter a discovery root ID or MAC address.");
        var revision = ++_discoveryRevision;
        var result = await _api.DiscoverMeshSubnetAsync(from, token);
        if (token.IsCancellationRequested || IsDisposed || !Visible || revision != _discoveryRevision) return;
        _discoveredFrom = from;
        ApplyDiscovery(result);
        SetStatus("BFS subnet discovery completed; results will follow connectivity changes.", hold: true);
    }

    private void ApplyDiscovery(MeshTraversalDto result)
    {
        static string Devices(string[] ids) => ids.Length == 0 ? "  None" : "  " + string.Join(Environment.NewLine + "  ", ids);
        _discoveryResult.Text = $"BFS root: {result.SourceId}\r\nVisited vertices: {result.VisitedCount}\r\n\r\n"
            + $"Reachable devices ({result.ReachableDeviceIds.Length}):\r\n{Devices(result.ReachableDeviceIds)}\r\n\r\n"
            + $"Offline devices ({result.OfflineDeviceIds.Length}):\r\n{Devices(result.OfflineDeviceIds)}\r\n\r\n"
            + $"Online but unreachable ({result.UnreachableDeviceIds.Length}):\r\n{Devices(result.UnreachableDeviceIds)}";
    }

    private async Task QueryHistoryAsync(CancellationToken token)
    {
        var device = _historyDevice.Text.Trim();
        if (device.Length == 0) throw new ArgumentException("Select a sensor or enter its device ID/MAC address.");
        var from = DateTime.SpecifyKind(_from.Value, DateTimeKind.Utc);
        var to = _toNow.Checked ? DateTime.UtcNow : DateTime.SpecifyKind(_to.Value, DateTimeKind.Utc);
        if (from > to) throw new ArgumentException("The start of the UTC range must not be after its end.");
        var result = await _api.GetMeshHistoryAsync(device, from, to, token);
        if (token.IsCancellationRequested || IsDisposed || !Visible) return;
        _history.Rows.Clear();
        foreach (var log in result.Readings)
            _history.Rows.Add(log.TimestampUtc.ToString("yyyy-MM-dd HH:mm:ss.fff"), log.DeviceId, log.Value, log.Unit, log.IsCritical ? "Critical" : "Normal", log.Sequence);
        _timeline.SetReadings(result.Readings);
        _historyStats.Text = $"{device} · {from:yyyy-MM-dd HH:mm:ss} to {to:yyyy-MM-dd HH:mm:ss} UTC · {result.Readings.Count} returned / {result.RetainedCount} retained · "
            + $"AVL height {result.TreeHeight}; {result.VisitedNodes} nodes visited" + (result.Truncated ? " · result truncated; narrow the time range" : "");
        SetStatus(result.Readings.Count == 0 ? "No retained readings match this UTC range. Start the simulator or widen the bounds." : "Historical log and graph loaded from the same AVL range query.", hold: true);
    }

    private async Task RunActionAsync(Func<CancellationToken, Task> action)
    {
        if (_actionBusy || !Visible || IsDisposed) return;
        _actionBusy = true;
        UpdateButtons();
        var token = _active?.Token ?? CancellationToken.None;
        try { await action(token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { SetStatus(ex.Message, error: true, hold: true); }
        finally { _actionBusy = false; if (!IsDisposed) UpdateButtons(); }
        await RefreshTopologyAsync();
    }

    private void UpdateButtons()
    {
        _trace.Enabled = _query.Enabled = _saveLink.Enabled = _removeLink.Enabled = _discover.Enabled = !_actionBusy;
        if (_actionBusy) _start.Enabled = _stop.Enabled = false;
    }
    private void SetStatus(string message, bool error = false, bool hold = false)
    {
        if (IsDisposed || !Visible) return;
        _status.Text = message;
        _status.ForeColor = error ? UiTheme.Error : UiTheme.Accent;
        if (hold) _holdStatusUntil = DateTime.UtcNow.AddSeconds(6);
    }

    // Cancellation on hide prevents late responses from updating retained hidden views (Microsoft, n.d.-d).
    protected override async void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (!_ready || IsDisposed) return;
        if (Visible)
        {
            _active?.Dispose();
            _active = new CancellationTokenSource();
            _timer.Start();
            await RefreshTopologyAsync();
        }
        else { _timer.Stop(); _active?.Cancel(); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _ready = false;
            _timer.Stop();
            _active?.Cancel();
            _timer.Dispose();
            _active?.Dispose();
            _api.Dispose();
        }
        base.Dispose(disposing);
    }
}
