using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using SmartX.Core.Models;
using SmartX.Desktop.Controls;
using SmartX.Desktop.Dtos;

namespace SmartX.Desktop.Forms;

/// <summary>Sensor Data Ingestion & Telemetry dashboard: registration with file attachment, live grids, the Pulse Gauge, and recursion/operator-overload demos.</summary>
public class TelemetryForm : ModuleForm
{
    private readonly ApiClient _api = new(ApiClient.DefaultBaseUrl);
    private readonly HashSet<string> _seenPacketIds = new();
    private readonly Queue<string> _seenPacketOrder = new();
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private CancellationTokenSource? _activeCts;
    private bool _refreshing;
    private bool _ready;
    private bool _registrationBusy;

    private string? _selectedFilePath;

    private TextBox _txtMac = null!;
    private GroupBox _registerGroup = null!;
    private TextBox _txtLocation = null!;
    private ComboBox _cmbCategory = null!;
    private Label _lblFile = null!;
    private Label _lblStatus = null!;
    private DataGridView _dgvSensors = null!;
    private DataGridView _dgvTelemetry = null!;
    private PulseGaugeControl _pulseGauge = null!;
    private TextBox _txtValidateResult = null!;
    private TextBox _txtAggregateResult = null!;

    public TelemetryForm()
    {
        Text = "Smart-X - Sensor Data Ingestion & Telemetry";
        ClientSize = new Size(1200, 750);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = UiTheme.Background;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;

        BuildLayout();

        // Auto-refresh timer (Microsoft, n.d.)
        _refreshTimer = new System.Windows.Forms.Timer { Interval = 3000 };
        _refreshTimer.Tick += async (s, e) => await RefreshDataAsync();
        _ready = true;
    }

    private void BuildLayout()
    {
        var btnBack = StyleSecondaryButton(new Button
        {
            Text = "<- Main Menu",
            Location = new Point(15, 15),
            Size = new Size(140, 30)
        });
        btnBack.Click += (s, e) => Close();

        var lblTitle = new Label
        {
            Text = "Sensor Data Ingestion & Telemetry",
            Font = new Font("Segoe UI", 14, FontStyle.Bold),
            ForeColor = UiTheme.Accent,
            AutoSize = true,
            Location = new Point(170, 20)
        };

        Controls.Add(btnBack);
        Controls.Add(lblTitle);

        _registerGroup = BuildRegisterGroup();
        Controls.Add(_registerGroup);
        Controls.Add(BuildSensorsGroup());
        Controls.Add(BuildPulseGroup());
        Controls.Add(BuildTelemetryGroup());
        Controls.Add(BuildToolsGroup());
    }

    // Card container (GroupBox class) (GeeksforGeeks, 2025)
    private static GroupBox CreateCard(string title, Point location, Size size)
    {
        return new GroupBox
        {
            Text = title,
            Location = location,
            Size = size,
            BackColor = UiTheme.Surface,
            ForeColor = UiTheme.Accent,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
        };
    }

    /// <summary>Consistent styling for every non-primary button, so nothing falls back to default system colours.</summary>
    // Button styling (GeeksforGeeks, 2025)
    private static Button StyleSecondaryButton(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.BackColor = UiTheme.ButtonSecondaryBack;
        button.ForeColor = UiTheme.TextPrimary;
        button.Font = new Font("Segoe UI", 9);
        button.FlatAppearance.BorderColor = UiTheme.Border;
        button.FlatAppearance.BorderSize = 1;
        button.Cursor = Cursors.Hand;
        return button;
    }

    private static Button StylePrimaryButton(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.BackColor = UiTheme.Accent;
        button.ForeColor = Color.White;
        button.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        button.FlatAppearance.BorderSize = 0;
        button.Cursor = Cursors.Hand;
        return button;
    }

    private GroupBox BuildRegisterGroup()
    {
        var group = CreateCard("Register Sensor", new Point(15, 60), new Size(330, 340));

        var lblMac = new Label { Text = "MAC Address", Location = new Point(15, 25), AutoSize = true, ForeColor = UiTheme.TextPrimary };
        _txtMac = new TextBox { Location = new Point(15, 45), Size = new Size(290, 23) };

        var btnGenMac = StyleSecondaryButton(new Button { Text = "Generate Mock MAC", Location = new Point(15, 73), Size = new Size(290, 26) });
        btnGenMac.Click += (s, e) => _txtMac.Text = GenerateMockMac();

        var lblLocation = new Label { Text = "Deployment Location", Location = new Point(15, 108), AutoSize = true, ForeColor = UiTheme.TextPrimary };
        _txtLocation = new TextBox { Location = new Point(15, 128), Size = new Size(290, 23) };

        var lblCategory = new Label { Text = "Sensor Category", Location = new Point(15, 160), AutoSize = true, ForeColor = UiTheme.TextPrimary };
        // Sensor Category ComboBox (GeeksforGeeks, 2025)
        _cmbCategory = new ComboBox { Location = new Point(15, 180), Size = new Size(290, 23), DropDownStyle = ComboBoxStyle.DropDownList };
        _cmbCategory.Items.AddRange(Enum.GetNames<SensorCategory>());
        if (_cmbCategory.Items.Count > 0) _cmbCategory.SelectedIndex = 0;

        // Attach File (Microsoft, n.d.)
        var btnAttach = StyleSecondaryButton(new Button { Text = "Attach File...", Location = new Point(15, 215), Size = new Size(150, 28) });
        _lblFile = new Label { Text = "(no file selected)", Location = new Point(175, 220), Size = new Size(140, 40), ForeColor = UiTheme.TextSecondary };
        btnAttach.Click += (s, e) => PickAttachment();

        var btnRegister = StylePrimaryButton(new Button { Text = "Register Sensor", Location = new Point(15, 255), Size = new Size(290, 32) });
        btnRegister.Click += async (s, e) => await RegisterSensorAsync();

        _lblStatus = new Label { Text = string.Empty, Location = new Point(15, 292), Size = new Size(290, 40), ForeColor = UiTheme.TextSecondary };

        group.Controls.Add(lblMac);
        group.Controls.Add(_txtMac);
        group.Controls.Add(btnGenMac);
        group.Controls.Add(lblLocation);
        group.Controls.Add(_txtLocation);
        group.Controls.Add(lblCategory);
        group.Controls.Add(_cmbCategory);
        group.Controls.Add(btnAttach);
        group.Controls.Add(_lblFile);
        group.Controls.Add(btnRegister);
        group.Controls.Add(_lblStatus);

        return group;
    }

    private GroupBox BuildSensorsGroup()
    {
        var group = CreateCard("Registered Sensors", new Point(360, 60), new Size(430, 340));

        _dgvSensors = CreateGrid(new Point(15, 25), new Size(400, 300));
        _dgvSensors.Columns.Add("DeviceId", "Device Id");
        _dgvSensors.Columns.Add("MacAddress", "MAC");
        _dgvSensors.Columns.Add("Location", "Location");
        _dgvSensors.Columns.Add("Category", "Category");
        _dgvSensors.Columns.Add("Attachment", "Attachment");

        group.Controls.Add(_dgvSensors);
        return group;
    }

    private GroupBox BuildPulseGroup()
    {
        var group = CreateCard("Live Telemetry Pulse", new Point(805, 60), new Size(380, 340));

        _pulseGauge = new PulseGaugeControl
        {
            Location = new Point(25, 30),
            Size = new Size(330, 260)
        };

        var lblSub = new Label
        {
            Text = "Radar sweep pulses with every packet the gateway receives.",
            Location = new Point(15, 300),
            Size = new Size(350, 30),
            ForeColor = UiTheme.TextSecondary
        };

        group.Controls.Add(_pulseGauge);
        group.Controls.Add(lblSub);
        return group;
    }

    private GroupBox BuildTelemetryGroup()
    {
        var group = CreateCard("Recent Telemetry Feed", new Point(15, 415), new Size(775, 320));

        var btnSendMock = StyleSecondaryButton(new Button { Text = "Send Mock Reading", Location = new Point(600, 20), Size = new Size(160, 28) });
        btnSendMock.Click += async (s, e) => await SendMockReadingAsync();

        _dgvTelemetry = CreateGrid(new Point(15, 55), new Size(745, 245));
        _dgvTelemetry.Columns.Add("Time", "Time (UTC)");
        _dgvTelemetry.Columns.Add("Device", "Device");
        _dgvTelemetry.Columns.Add("Category", "Category");
        _dgvTelemetry.Columns.Add("Value", "Value");
        _dgvTelemetry.Columns.Add("Unit", "Unit");

        group.Controls.Add(btnSendMock);
        group.Controls.Add(_dgvTelemetry);
        return group;
    }

    private GroupBox BuildToolsGroup()
    {
        var group = CreateCard("OOP Concept Demonstrations", new Point(805, 415), new Size(380, 320));

        var btnValidate = StyleSecondaryButton(new Button { Text = "Validate Deployment Tree (Recursion)", Location = new Point(15, 25), Size = new Size(350, 30) });
        btnValidate.Click += async (s, e) => await ValidateDeploymentAsync();

        _txtValidateResult = new TextBox
        {
            Location = new Point(15, 60),
            Size = new Size(350, 80),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 8.5f),
            BackColor = UiTheme.Background,
            ForeColor = UiTheme.TextPrimary
        };

        var btnAggregate = StyleSecondaryButton(new Button { Text = "Simulate Aggregate Load (Operators)", Location = new Point(15, 150), Size = new Size(350, 30) });
        btnAggregate.Click += async (s, e) => await SimulateAggregateAsync();

        _txtAggregateResult = new TextBox
        {
            Location = new Point(15, 185),
            Size = new Size(350, 110),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 8.5f),
            BackColor = UiTheme.Background,
            ForeColor = UiTheme.TextPrimary
        };

        group.Controls.Add(btnValidate);
        group.Controls.Add(_txtValidateResult);
        group.Controls.Add(btnAggregate);
        group.Controls.Add(_txtAggregateResult);
        return group;
    }

    // Registered Sensors / Telemetry grid (Microsoft, n.d.)
    private static DataGridView CreateGrid(Point location, Size size)
    {
        var grid = new DataGridView
        {
            Location = location,
            Size = size,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AutoGenerateColumns = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = UiTheme.Surface,
            BorderStyle = BorderStyle.None,
            GridColor = UiTheme.Border,
            EnableHeadersVisualStyles = false
        };

        grid.ColumnHeadersDefaultCellStyle.BackColor = UiTheme.GridHeaderBack;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = UiTheme.TextPrimary;
        grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = UiTheme.GridHeaderBack;
        grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = UiTheme.TextPrimary;

        grid.DefaultCellStyle.BackColor = UiTheme.Surface;
        grid.DefaultCellStyle.ForeColor = UiTheme.TextPrimary;
        grid.DefaultCellStyle.SelectionBackColor = UiTheme.Accent;
        grid.DefaultCellStyle.SelectionForeColor = Color.White;

        return grid;
    }

    private void PickAttachment()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Attach device configuration, photo, or log file",
            Filter = "All supported files|*.json;*.txt;*.log;*.jpg;*.jpeg;*.png|All files|*.*"
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _selectedFilePath = dialog.FileName;
            _lblFile.Text = Path.GetFileName(dialog.FileName);
        }
    }

    // Generate Mock MAC (GeeksforGeeks, 2025)
    private static string GenerateMockMac()
    {
        var bytes = new byte[6];
        Random.Shared.NextBytes(bytes);
        return string.Join(":", bytes.Select(b => b.ToString("X2")));
    }

    private async Task RegisterSensorAsync()
    {
        if (_registrationBusy || IsDisposed) return;
        if (string.IsNullOrWhiteSpace(_txtMac.Text) || string.IsNullOrWhiteSpace(_txtLocation.Text) || _cmbCategory.SelectedItem is null)
        {
            _lblStatus.ForeColor = UiTheme.Error;
            _lblStatus.Text = "MAC, location and category are all required.";
            return;
        }

        var attachment = _selectedFilePath;
        SensorRow? registered = null;
        _registrationBusy = true;
        _registerGroup.Enabled = false;
        try
        {
            var category = _cmbCategory.SelectedItem.ToString()!;
            registered = await _api.RegisterSensorAsync(_txtMac.Text.Trim(), _txtLocation.Text.Trim(), category)
                ?? throw new InvalidOperationException("The API returned no registered device.");

            if (IsDisposed) return;

            // A completed submission is reconciled even when its retained module is hidden.
            _txtMac.Clear();
            _txtLocation.Clear();
            _selectedFilePath = null;
            _lblFile.Text = "(no file selected)";

            if (attachment is not null)
                await _api.UploadAttachmentAsync(registered.DeviceId, attachment);

            if (IsDisposed) return;
            _lblStatus.ForeColor = UiTheme.Accent;
            _lblStatus.Text = $"Registered {registered.DeviceId}" + (attachment is not null ? " with attachment." : ".");

            await RefreshDataAsync();
        }
        catch (Exception ex)
        {
            if (IsDisposed) return;
            _lblStatus.ForeColor = UiTheme.Error;
            _lblStatus.Text = registered is null ? $"Registration failed: {ex.Message}" : $"Registered {registered.DeviceId}; attachment failed: {ex.Message}";
        }
        finally
        {
            _registrationBusy = false;
            if (!IsDisposed) _registerGroup.Enabled = true;
        }
    }

    private async Task SendMockReadingAsync()
    {
        try
        {
            var sensors = await _api.GetSensorsAsync();
            if (sensors.Count == 0) return;

            var sensor = sensors[Random.Shared.Next(sensors.Count)];
            var (valueType, value, unit) = GenerateMockReading(sensor.Category);

            await _api.IngestTelemetryAsync(sensor.DeviceId, sensor.Category, valueType, value, unit);
            await RefreshDataAsync();
        }
        catch (Exception ex)
        {
            if (IsDisposed) return;
            _lblStatus.ForeColor = UiTheme.Error;
            _lblStatus.Text = $"Could not send mock reading: {ex.Message}";
        }
    }

    private static (string valueType, string value, string unit) GenerateMockReading(string category)
    {
        return category switch
        {
            nameof(SensorCategory.SoilMoisture) => ("float", Math.Round(Random.Shared.NextDouble() * 100, 1).ToString(CultureInfo.InvariantCulture), "%"),
            nameof(SensorCategory.Temperature) => ("float", Math.Round(15 + Random.Shared.NextDouble() * 20, 1).ToString(CultureInfo.InvariantCulture), "C"),
            nameof(SensorCategory.Humidity) => ("float", Math.Round(Random.Shared.NextDouble() * 100, 1).ToString(CultureInfo.InvariantCulture), "%RH"),
            nameof(SensorCategory.PowerMeter) => ("int", Random.Shared.Next(50, 2500).ToString(), "W"),
            nameof(SensorCategory.LightLevel) => ("int", Random.Shared.Next(0, 10000).ToString(), "lux"),
            nameof(SensorCategory.ValveState) => ("bool", (Random.Shared.Next(2) == 1).ToString(), ""),
            _ => ("float", "0", "")
        };
    }

    private async Task ValidateDeploymentAsync()
    {
        try
        {
            var json = await _api.ValidateDeploymentAsync();
            if (IsDisposed) return;
            _txtValidateResult.Text = PrettyPrint(json);
        }
        catch (Exception ex)
        {
            if (IsDisposed) return;
            _txtValidateResult.Text = $"Could not reach API: {ex.Message}";
        }
    }

    private async Task SimulateAggregateAsync()
    {
        try
        {
            var json = await _api.AggregateAsync();
            if (IsDisposed) return;
            _txtAggregateResult.Text = PrettyPrint(json);
        }
        catch (Exception ex)
        {
            if (IsDisposed) return;
            _txtAggregateResult.Text = $"Could not reach API: {ex.Message}";
        }
    }

    // Pretty-print JSON (Microsoft, n.d.)
    private static string PrettyPrint(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return System.Text.Json.JsonSerializer.Serialize(doc.RootElement, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return json;
        }
    }

    private async Task RefreshDataAsync()
    {
        if (_refreshing || !Visible || IsDisposed) return;
        _refreshing = true;
        var token = _activeCts?.Token ?? CancellationToken.None;
        try
        {
            var sensorTask = _api.GetSensorsAsync(token);
            var telemetryTask = _api.GetTelemetryAsync(cancellationToken: token);
            await Task.WhenAll(sensorTask, telemetryTask);
            var sensors = await sensorTask;
            var telemetry = await telemetryTask;

            if (IsDisposed || !Visible || token.IsCancellationRequested) return;

            var selectedId = _dgvSensors.SelectedRows.Count > 0 ? _dgvSensors.SelectedRows[0].Cells[0].Value?.ToString() : null;
            _dgvSensors.Rows.Clear();
            foreach (var sensor in sensors)
            {
                _dgvSensors.Rows.Add(sensor.DeviceId, sensor.MacAddress, sensor.Location, sensor.Category, sensor.AttachmentFileName ?? "-");
            }
            foreach (DataGridViewRow row in _dgvSensors.Rows)
                if (row.Cells[0].Value?.ToString() == selectedId)
                {
                    _dgvSensors.CurrentCell = row.Cells[0];
                    row.Selected = true;
                    break;
                }

            _dgvTelemetry.Rows.Clear();
            foreach (var packet in telemetry)
            {
                _dgvTelemetry.Rows.Add(packet.TimestampUtc.ToString("HH:mm:ss"), packet.DeviceId, packet.Category, packet.Value, packet.Unit);

                if (_seenPacketIds.Add(packet.PacketId))
                {
                    _seenPacketOrder.Enqueue(packet.PacketId);
                    if (_seenPacketOrder.Count > 2048) _seenPacketIds.Remove(_seenPacketOrder.Dequeue());
                    _pulseGauge.RegisterPing();
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (IsDisposed || !Visible) return;
            _lblStatus.ForeColor = UiTheme.Error;
            _lblStatus.Text = $"Live refresh failed; retrying: {ex.Message}";
        }
        finally { _refreshing = false; }
    }

    protected override async void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (!_ready || _refreshTimer is null || IsDisposed) return;
        if (Visible)
        {
            _activeCts?.Dispose();
            _activeCts = new CancellationTokenSource();
            _refreshTimer.Start();
            await RefreshDataAsync();
        }
        else
        {
            _refreshTimer.Stop();
            _activeCts?.Cancel();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _ready = false;
            _refreshTimer?.Stop();
            _activeCts?.Cancel();
            _refreshTimer?.Dispose();
            _activeCts?.Dispose();
            _api.Dispose();
        }
        base.Dispose(disposing);
    }
}
