using SmartX.Core.Models;
using SmartX.Desktop.Controls;
using SmartX.Desktop.Dtos;

namespace SmartX.Desktop.Forms;

/// <summary>Live polling, device overrides and predictive insights share a cancellable UI lifecycle.</summary>
public sealed class CommandStreamForm : ModuleForm
{
    private readonly ApiClient _api = new(ApiClient.DefaultBaseUrl);
    private readonly System.Windows.Forms.Timer _pollTimer = new() { Interval = 750 };
    private readonly System.Windows.Forms.Timer _searchTimer = new() { Interval = 450 };
    private readonly ToolTip _tooltips = new() { AutoPopDelay = 15000 };
    private CancellationTokenSource? _activeCts;
    private CancellationTokenSource? _refreshCts;
    private bool _ready, _refreshing, _actionBusy, _binding, _recordSearchPending;
    private int _filterRevision;
    private string? _selectedDeviceId, _lastRecordedSearch;
    private DateTime _statusHoldUntilUtc;
    private RealtimeDashboard _snapshot = new();

    private readonly TextBox _search = new() { Width = 260, MaxLength = 100, PlaceholderText = "Device, location, MAC or alert..." };
    private readonly ComboBox _category = new() { Width = 150, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _alertsOnly = new() { Text = "Active alerts only", AutoSize = true };
    private readonly Label _status = new() { Dock = DockStyle.Fill, ForeColor = UiTheme.TextSecondary, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label _metrics = new() { Dock = DockStyle.Fill, ForeColor = UiTheme.TextPrimary, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label _deviceDetails = new() { Dock = DockStyle.Fill, ForeColor = UiTheme.TextPrimary, AutoEllipsis = true };
    private readonly Label _insightDetail = new() { Dock = DockStyle.Fill, ForeColor = UiTheme.TextSecondary, AutoEllipsis = true };
    private readonly Label _simStatus = new() { Text = "Simulator status pending", AutoSize = true, Padding = new Padding(5, 8, 0, 0), ForeColor = UiTheme.TextSecondary };
    private readonly DataGridView _devices = CreateGrid();
    private readonly DataGridView _feed = CreateGrid();
    private readonly DataGridView _history = CreateGrid();
    private readonly DataGridView _insights = CreateGrid();
    private readonly TelemetryTimelineControl _timeline = new() { Dock = DockStyle.Fill };
    private readonly Button _turnOn = Button("Turn on");
    private readonly Button _turnOff = Button("Turn off");
    private readonly Button _allOff = Button("All valves off");
    private readonly Button _undo = Button("Undo latest override");
    private readonly Button _acceptInsight = Button("Apply suggested command");
    private readonly Button _startSim = Button("Start device simulator");
    private readonly Button _stopSim = Button("Stop simulator");

    public CommandStreamForm()
    {
        Text = "Smart-X - Real-Time Command Stream & History";
        ClientSize = new Size(1460, 920);
        MinimumSize = new Size(1160, 780);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = UiTheme.Background;
        Font = new Font("Segoe UI", 9);
        BuildLayout();
        _category.Items.Add("All categories");
        _category.Items.AddRange(Enum.GetNames<SensorCategory>());
        _category.SelectedIndex = 0;

        _pollTimer.Tick += async (_, _) => await RefreshAsync();
        _searchTimer.Tick += async (_, _) => { _searchTimer.Stop(); await CommitSearchAsync(); };
        _search.TextChanged += (_, _) => FiltersChanged(recordSearch: true);
        _search.KeyDown += async (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            _searchTimer.Stop();
            _recordSearchPending = true;
            _lastRecordedSearch = null;
            await CommitSearchAsync();
        };
        _category.SelectedIndexChanged += (_, _) => FiltersChanged(recordSearch: false);
        _alertsOnly.CheckedChanged += (_, _) => FiltersChanged(recordSearch: false);
        _devices.SelectionChanged += async (_, _) => await DeviceSelectionChangedAsync();
        _insights.SelectionChanged += (_, _) => UpdateInsightSelection();
        _turnOn.Click += async (_, _) => await IssueSelectedAsync("TurnOn");
        _turnOff.Click += async (_, _) => await IssueSelectedAsync("TurnOff");
        _allOff.Click += async (_, _) => await RunActionAsync(token => _api.SendCommandAsync("*", "AllValvesOff", token), "All valves switched off. Undo restores each valve's prior state.");
        _undo.Click += async (_, _) => await RunActionAsync(_api.UndoCommandAsync, "Latest override reverted.");
        _acceptInsight.Click += async (_, _) => await ApplyInsightAsync();
        _startSim.Click += async (_, _) => await RunActionAsync(token => _api.SetSimulationAsync(true, token), "Device simulator started. Readings arrive through the HTTP ingestion API.");
        _stopSim.Click += async (_, _) => await RunActionAsync(token => _api.SetSimulationAsync(false, token), "Device simulator stopped.");
        _ready = true;
        UpdateButtons();
    }

    private void BuildLayout()
    {
        var page = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 1, RowCount = 6 };
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 52));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 48));
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        Controls.Add(page);

        var heading = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        var back = Button("< Main Menu");
        back.Click += (_, _) => Close();
        heading.Controls.Add(back);
        heading.Controls.Add(new Label { Text = "Real-Time Command Stream", Font = new Font("Segoe UI", 15, FontStyle.Bold), ForeColor = UiTheme.Accent, AutoSize = true, Padding = new Padding(12, 1, 10, 0) });
        heading.Controls.Add(_startSim);
        heading.Controls.Add(_stopSim);
        heading.Controls.Add(_simStatus);
        page.Controls.Add(heading, 0, 0);

        var filters = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 5, 0, 0) };
        filters.Controls.Add(new Label { Text = "Find devices", AutoSize = true, Padding = new Padding(0, 5, 8, 0) });
        filters.Controls.Add(_search);
        filters.Controls.Add(_category);
        filters.Controls.Add(_alertsOnly);
        filters.Controls.Add(new Label { Text = "750 ms live refresh · device state persists between views", AutoSize = true, ForeColor = UiTheme.TextSecondary, Padding = new Padding(12, 5, 0, 0) });
        page.Controls.Add(filters, 0, 1);
        page.Controls.Add(_metrics, 0, 2);

        var live = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        live.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 59));
        live.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 41));
        page.Controls.Add(live, 0, 3);
        AddColumns(_devices, ("Device", 12), ("Location", 22), ("Category", 17), ("Reading", 13), ("State", 13), ("Active alerts", 23));
        live.Controls.Add(Card("Live devices · select a row to inspect or command", _devices), 0, 0);

        var selected = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        selected.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        selected.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        selected.RowStyles.Add(new RowStyle(SizeType.Absolute, 39));
        selected.Controls.Add(_deviceDetails, 0, 0);
        selected.Controls.Add(_timeline, 0, 1);
        var overrides = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        overrides.Controls.AddRange([_turnOn, _turnOff, _allOff]);
        selected.Controls.Add(overrides, 0, 2);
        live.Controls.Add(Card("Selected device · timestamp-ordered history", selected), 1, 0);

        var streams = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        streams.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 37));
        streams.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 29));
        streams.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        page.Controls.Add(streams, 0, 4);
        AddColumns(_feed, ("UTC", 17), ("Device", 17), ("Reading", 26), ("Priority", 20), ("Seq", 20));
        streams.Controls.Add(Card("Incoming telemetry · critical packets bypass FIFO", _feed), 0, 0);

        AddColumns(_history, ("UTC", 20), ("Device", 22), ("Override", 33), ("State", 25));
        var historyPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
        historyPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        historyPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        historyPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        historyPanel.Controls.Add(_history, 0, 0);
        historyPanel.Controls.Add(_undo, 0, 1);
        historyPanel.Controls.Add(new Label { Text = "Newest first · simulated device overrides", Dock = DockStyle.Fill, ForeColor = UiTheme.TextSecondary }, 0, 2);
        streams.Controls.Add(Card("Command history · last in, first undone", historyPanel), 1, 0);

        AddColumns(_insights, ("Suggested action / insight", 60), ("Confidence", 23), ("Evidence", 17));
        var insightPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
        insightPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        insightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
        insightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        insightPanel.Controls.Add(_insights, 0, 0);
        insightPanel.Controls.Add(_insightDetail, 0, 1);
        insightPanel.Controls.Add(_acceptInsight, 0, 2);
        streams.Controls.Add(Card("Suggested Actions & Automated Insights", insightPanel), 2, 0);
        page.Controls.Add(_status, 0, 5);
    }

    private static Button Button(string text) => new()
    {
        Text = text, AutoSize = true, Height = 30, MinimumSize = new Size(82, 30),
        FlatStyle = FlatStyle.Flat, BackColor = UiTheme.ButtonSecondaryBack,
        ForeColor = UiTheme.TextPrimary, Cursor = Cursors.Hand, Margin = new Padding(3, 2, 3, 2)
    };

    private static GroupBox Card(string title, Control content)
    {
        var box = new GroupBox { Text = title, Dock = DockStyle.Fill, Padding = new Padding(10, 23, 10, 10), ForeColor = UiTheme.Accent, BackColor = UiTheme.Surface, Margin = new Padding(3, 5, 3, 5) };
        box.Controls.Add(content);
        return box;
    }

    private static DataGridView CreateGrid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false, AutoGenerateColumns = false, RowHeadersVisible = false, MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = UiTheme.Surface, BorderStyle = BorderStyle.None, GridColor = UiTheme.Border,
            EnableHeadersVisualStyles = false, ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize
        };
        grid.ColumnHeadersDefaultCellStyle.BackColor = UiTheme.GridHeaderBack;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = UiTheme.TextPrimary;
        grid.DefaultCellStyle.BackColor = UiTheme.Surface;
        grid.DefaultCellStyle.ForeColor = UiTheme.TextPrimary;
        grid.DefaultCellStyle.SelectionBackColor = UiTheme.Accent;
        grid.DefaultCellStyle.SelectionForeColor = Color.White;
        return grid;
    }

    private static void AddColumns(DataGridView grid, params (string Title, int Weight)[] columns)
    {
        foreach (var (title, weight) in columns)
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = title, HeaderText = title, FillWeight = weight, SortMode = DataGridViewColumnSortMode.NotSortable });
    }

    private void FiltersChanged(bool recordSearch)
    {
        ++_filterRevision;
        _refreshCts?.Cancel();
        _recordSearchPending |= recordSearch;
        if (recordSearch && string.IsNullOrWhiteSpace(_search.Text)) _lastRecordedSearch = null;
        _searchTimer.Stop();
        if (Visible) _searchTimer.Start();
    }

    private async Task CommitSearchAsync()
    {
        var token = _activeCts?.Token ?? CancellationToken.None;
        var query = _search.Text.Trim();
        try
        {
            if (_recordSearchPending && query.Length > 0 && query != _lastRecordedSearch)
            {
                _recordSearchPending = false;
                await _api.RecordSearchAsync(query, token);
                _lastRecordedSearch = query;
            }
            else _recordSearchPending = false;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { ShowStatus($"Search history could not be saved: {ex.Message}", error: true, hold: true); }
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (_refreshing || !Visible || IsDisposed || _searchTimer.Enabled) return;
        _refreshing = true;
        using var updateCts = CancellationTokenSource.CreateLinkedTokenSource(_activeCts?.Token ?? CancellationToken.None);
        _refreshCts = updateCts;
        var token = updateCts.Token;
        var revision = _filterRevision;
        try
        {
            var dashboardTask = _api.GetDashboardAsync(_search.Text.Trim(), _category.SelectedIndex > 0 ? _category.SelectedItem?.ToString() : null, _alertsOnly.Checked, token);
            var simulatorTask = _api.GetSimulationAsync(token);
            await Task.WhenAll(dashboardTask, simulatorTask);
            if (token.IsCancellationRequested || IsDisposed || !Visible || revision != _filterRevision) return;
            _snapshot = await dashboardTask;
            ApplySnapshot();
            var simulator = await simulatorTask;
            _simStatus.Text = simulator.Running ? $"HTTP simulator running · {simulator.SentPackets:N0} sent" : "HTTP simulator stopped";
            _startSim.Enabled = !_actionBusy && !simulator.Running;
            _stopSim.Enabled = !_actionBusy && simulator.Running;
            if (!string.IsNullOrWhiteSpace(simulator.LastError)) ShowStatus($"Simulator: {simulator.LastError}", error: true);
            else if (!_actionBusy && DateTime.UtcNow >= _statusHoldUntilUtc) ShowStatus($"Live · refreshed {DateTime.Now:T} · timestamps in tables and graphs are UTC");

            var device = SelectedDevice();
            if (device is null) _timeline.SetReadings([]);
            else
            {
                var readings = await _api.GetTimelineAsync(device.DeviceId, token);
                if (!token.IsCancellationRequested && !IsDisposed && Visible && SelectedDevice()?.DeviceId == device.DeviceId && revision == _filterRevision)
                    _timeline.SetReadings(readings);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { ShowStatus($"Live update failed: {ex.Message}. Retrying automatically.", error: true); }
        finally { _refreshCts = null; _refreshing = false; }
    }

    private void ApplySnapshot()
    {
        _binding = true;
        var viewports = new[] { _devices, _feed, _history, _insights }.Select(CaptureViewport).ToArray();
        try
        {
            var metrics = _snapshot.Metrics;
            _metrics.Text = $"Registered: {metrics.RegisteredDevices}    FIFO waiting: {metrics.StandardQueueDepth}    Critical waiting: {metrics.CriticalQueueDepth}    Processed: {metrics.ProcessedPackets:N0}    Critical: {metrics.CriticalPackets:N0}    Unique alerts: {metrics.UniqueActiveAlerts}    Dropped: {metrics.DroppedPackets:N0}";
            _devices.Rows.Clear();
            foreach (var device in _snapshot.Devices)
            {
                var state = device.LastSeenUtc is null ? "Awaiting data" : device.IsConnected ? "Online" : "Disconnected";
                if (IsActuator(device)) state += device.IsOn ? " · ON" : " · OFF";
                var index = _devices.Rows.Add(device.DeviceId, device.Location, device.Category,
                    device.LastSeenUtc is null ? "—" : $"{device.Value} {device.Unit}".Trim(), state,
                    device.ActiveAlerts.Length == 0 ? "—" : string.Join(", ", device.ActiveAlerts));
                var row = _devices.Rows[index];
                row.Tag = device;
                row.Cells[0].ToolTipText = $"{device.DeviceId}\nMAC {device.MacAddress}\nLast seen UTC: {device.LastSeenUtc?.ToString("u") ?? "awaiting first reading"}";
                if (device.ActiveAlerts.Length > 0) row.DefaultCellStyle.ForeColor = UiTheme.Error;
            }
            _devices.ClearSelection();
            if (_selectedDeviceId is null && _snapshot.Devices.Count > 0) _selectedDeviceId = _snapshot.Devices[0].DeviceId;
            SelectTaggedRow(_devices, row => row.Tag is LiveDeviceRow device && device.DeviceId == _selectedDeviceId);

            _feed.Rows.Clear();
            foreach (var packet in _snapshot.RecentTelemetry)
            {
                var row = _feed.Rows[_feed.Rows.Add(packet.TimestampUtc.ToString("HH:mm:ss.fff"), packet.DeviceId,
                    $"{packet.Value} {packet.Unit}".Trim(), packet.IsCritical ? "CRITICAL" : "FIFO", packet.Sequence)];
                row.Tag = packet;
                if (packet.IsCritical) row.DefaultCellStyle.ForeColor = UiTheme.Error;
            }

            var commandId = (_history.SelectedRows.Cast<DataGridViewRow>().FirstOrDefault()?.Tag as CommandHistoryRow)?.CommandId;
            _history.Rows.Clear();
            foreach (var command in _snapshot.Commands)
            {
                var row = _history.Rows[_history.Rows.Add(command.IssuedUtc.ToString("HH:mm:ss"), command.DeviceId,
                    command.Action, command.IsUndone ? "Undone" : "Applied")];
                row.Tag = command;
                row.Cells[1].ToolTipText = string.Join(", ", command.AffectedDeviceIds);
            }
            SelectTaggedRow(_history, row => row.Tag is CommandHistoryRow command && command.CommandId == commandId);

            var insightId = SelectedInsight()?.Id;
            _insights.Rows.Clear();
            foreach (var insight in _snapshot.SuggestedActions)
                _insights.Rows[_insights.Rows.Add(insight.Title, insight.Confidence.ToString("P0"), insight.EvidenceCount)].Tag = insight;
            SelectTaggedRow(_insights, row => row.Tag is SuggestedActionRow insight && insight.Id == insightId);
            var grids = new[] { _devices, _feed, _history, _insights };
            for (var index = 0; index < grids.Length; index++) RestoreViewport(grids[index], viewports[index]);
        }
        finally { _binding = false; }
        UpdateDeviceDetails();
        UpdateInsightSelection();
        UpdateButtons();
    }

    private static void SelectTaggedRow(DataGridView grid, Func<DataGridViewRow, bool> match)
    {
        foreach (DataGridViewRow row in grid.Rows)
            if (match(row)) { grid.CurrentCell = row.Cells[0]; row.Selected = true; return; }
    }

    private readonly record struct GridViewport(string? TopKey, int TopIndex, int HorizontalOffset);

    private static string? RowKey(object? row) => row switch
    {
        LiveDeviceRow device => device.DeviceId,
        RealtimeTelemetryRow packet => packet.PacketId,
        CommandHistoryRow command => command.CommandId,
        SuggestedActionRow insight => insight.Id,
        _ => null
    };

    private static GridViewport CaptureViewport(DataGridView grid)
    {
        var index = grid.FirstDisplayedScrollingRowIndex;
        return new(index >= 0 && index < grid.RowCount ? RowKey(grid.Rows[index].Tag) : null,
            index, grid.HorizontalScrollingOffset);
    }

    private static void RestoreViewport(DataGridView grid, GridViewport viewport)
    {
        if (grid.RowCount == 0 || viewport.TopIndex < 0) return;
        var index = Math.Min(viewport.TopIndex, grid.RowCount - 1);
        // A viewport at the head follows fresh traffic; an older viewport follows its stable row.
        if (viewport.TopIndex > 0 && viewport.TopKey is not null)
            foreach (DataGridViewRow row in grid.Rows)
                if (RowKey(row.Tag) == viewport.TopKey) { index = row.Index; break; }
        grid.FirstDisplayedScrollingRowIndex = index;
        grid.HorizontalScrollingOffset = viewport.HorizontalOffset;
    }

    private LiveDeviceRow? SelectedDevice() => _devices.SelectedRows.Cast<DataGridViewRow>().FirstOrDefault()?.Tag as LiveDeviceRow;
    private SuggestedActionRow? SelectedInsight() => _insights.SelectedRows.Cast<DataGridViewRow>().FirstOrDefault()?.Tag as SuggestedActionRow;
    private static bool IsActuator(LiveDeviceRow device) => device.Category is "ValveState" or "PowerMeter";

    private async Task DeviceSelectionChangedAsync()
    {
        if (_binding || !_ready) return;
        _refreshCts?.Cancel();
        _selectedDeviceId = SelectedDevice()?.DeviceId ?? _selectedDeviceId;
        _timeline.SetReadings([]);
        UpdateDeviceDetails();
        UpdateButtons();
        await RefreshAsync();
    }

    private void UpdateDeviceDetails()
    {
        var device = SelectedDevice();
        _deviceDetails.Text = device is null
            ? "Select a device from the filtered list. Start the HTTP simulator to receive device readings."
            : $"{device.Location} · {device.Category}\n{device.DeviceId} · {device.MacAddress}\nLast seen UTC: {device.LastSeenUtc?.ToString("HH:mm:ss") ?? "awaiting first HTTP reading"}";
    }

    private void UpdateInsightSelection()
    {
        if (_binding || !_ready || IsDisposed || Disposing) return;
        var insight = SelectedInsight();
        _insightDetail.Text = insight is null
            ? "Insights use sensor trends and repeated operator searches or commands. Select an insight to see its evidence."
            : $"{insight.Detail}\nDevice: {insight.DeviceId} · {insight.Confidence:P0} confidence · {insight.EvidenceCount} evidence events";
        _tooltips.SetToolTip(_insightDetail, _insightDetail.Text);
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        if (!_ready || IsDisposed || Disposing) return;
        var actuator = SelectedDevice() is { } device && IsActuator(device);
        _turnOn.Enabled = !_actionBusy && actuator;
        _turnOff.Enabled = !_actionBusy && actuator;
        _allOff.Enabled = !_actionBusy && _snapshot.Metrics.RegisteredDevices > 0;
        _undo.Enabled = !_actionBusy && _snapshot.Commands.Any(command => !command.IsUndone);
        _acceptInsight.Enabled = !_actionBusy && SelectedInsight()?.Action is "TurnOn" or "TurnOff" or "AllValvesOff";
        if (_actionBusy) { _startSim.Enabled = false; _stopSim.Enabled = false; }
    }

    private Task IssueSelectedAsync(string action)
    {
        var device = SelectedDevice();
        return device is null ? Task.CompletedTask : RunActionAsync(token => _api.SendCommandAsync(device.DeviceId, action, token), $"{action} issued to {device.Location}.");
    }

    private Task ApplyInsightAsync()
    {
        var insight = SelectedInsight();
        return insight?.Action is not ("TurnOn" or "TurnOff" or "AllValvesOff")
            ? Task.CompletedTask
            : RunActionAsync(token => _api.SendCommandAsync(insight.DeviceId, insight.Action, token), $"Suggested command applied to {insight.DeviceId}.");
    }

    private async Task RunActionAsync(Func<CancellationToken, Task> action, string success)
    {
        if (_actionBusy || !Visible || IsDisposed) return;
        _actionBusy = true;
        UpdateButtons();
        var token = _activeCts?.Token ?? CancellationToken.None;
        try
        {
            await action(token);
            if (!token.IsCancellationRequested) ShowStatus(success, hold: true);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { ShowStatus($"Action failed: {ex.Message}", error: true, hold: true); }
        finally
        {
            _actionBusy = false;
            if (!IsDisposed) UpdateButtons();
        }
        await RefreshAsync();
    }

    private void ShowStatus(string message, bool error = false, bool hold = false)
    {
        if (IsDisposed || !Visible) return;
        _status.Text = message;
        _status.ForeColor = error ? UiTheme.Error : UiTheme.Accent;
        if (hold) _statusHoldUntilUtc = DateTime.UtcNow.AddSeconds(6);
    }

    protected override async void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (!_ready || IsDisposed) return;
        if (Visible)
        {
            _activeCts?.Dispose();
            _activeCts = new CancellationTokenSource();
            _pollTimer.Start();
            if (_recordSearchPending) { _searchTimer.Start(); return; }
            await RefreshAsync();
        }
        else
        {
            _pollTimer.Stop();
            _searchTimer.Stop();
            _activeCts?.Cancel();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _ready = false;
            _pollTimer.Stop();
            _searchTimer.Stop();
            _activeCts?.Cancel();
            _pollTimer.Dispose();
            _searchTimer.Dispose();
            _tooltips.Dispose();
            _activeCts?.Dispose();
            _api.Dispose();
        }
        base.Dispose(disposing);
    }
}
