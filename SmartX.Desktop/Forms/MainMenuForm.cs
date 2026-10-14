using System.Drawing;
using System.Windows.Forms;

namespace SmartX.Desktop.Forms;

/// <summary>Nonblocking navigation with persistent module instances.</summary>
public class MainMenuForm : Form
{
    private TelemetryForm? _telemetry;
    private CommandStreamForm? _commands;

    public MainMenuForm()
    {
        Text = "Smart-X Gateway Console";
        ClientSize = new Size(560, 400);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = UiTheme.Background;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;

        var title = new Label
        {
            Text = "SMART-X",
            Font = new Font("Segoe UI", 26, FontStyle.Bold),
            ForeColor = UiTheme.Accent,
            AutoSize = true,
            Location = new Point(40, 28)
        };

        var subtitle = new Label
        {
            Text = "Distributed IoT Gateway - Edge Telemetry Platform",
            Font = new Font("Segoe UI", 10),
            ForeColor = UiTheme.TextSecondary,
            AutoSize = true,
            Location = new Point(43, 82)
        };

        var btnTelemetry = CreatePillarButton(
            "Sensor Data Ingestion & Telemetry",
            "Register devices, stream and inspect live readings.",
            130, true);

        btnTelemetry.Click += (s, e) => OpenModule(ref _telemetry, () => new TelemetryForm());

        var btnCommand = CreatePillarButton(
            "Real-Time Command Stream & History",
            "Monitor live devices, issue overrides, undo and explore insights.",
            210, true);
        btnCommand.Click += (s, e) => OpenModule(ref _commands, () => new CommandStreamForm());

        var btnMesh = CreatePillarButton(
            "Network Topology & Mesh Routing",
            "Future module - topology visualisation and mesh routing.",
            290, false);

        Controls.Add(title);
        Controls.Add(subtitle);
        Controls.Add(btnTelemetry);
        Controls.Add(btnCommand);
        Controls.Add(btnMesh);
    }

    private void OpenModule<T>(ref T? module, Func<T> create) where T : ModuleForm
    {
        if (module is null || module.IsDisposed)
        {
            module = create();
            module.BackToMenu += (s, e) => { Show(); Activate(); };
        }
        Hide();
        module.Show();
        module.Activate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _telemetry?.Dispose();
            _commands?.Dispose();
        }
        base.Dispose(disposing);
    }

    // Pillar navigation buttons (GeeksforGeeks, 2025)
    private static Button CreatePillarButton(string title, string description, int top, bool enabled)
    {
        var button = new Button
        {
            Text = $"{title}\n{description}",
            Location = new Point(40, top),
            Size = new Size(480, 60),
            Enabled = enabled,
            FlatStyle = FlatStyle.Flat,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(14, 0, 0, 0),
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            BackColor = enabled ? UiTheme.Surface : UiTheme.DisabledBack,
            ForeColor = enabled ? UiTheme.TextPrimary : UiTheme.DisabledText,
            Cursor = enabled ? Cursors.Hand : Cursors.No
        };

        button.FlatAppearance.BorderColor = enabled ? UiTheme.Accent : UiTheme.Border;
        button.FlatAppearance.BorderSize = enabled ? 2 : 1;

        return button;
    }
}
