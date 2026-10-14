namespace SmartX.Desktop.Forms;

/// <summary>Modeless module navigation keeps each dashboard's controls and state alive.</summary>
public class ModuleForm : Form
{
    public event EventHandler? BackToMenu;

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            BackToMenu?.Invoke(this, EventArgs.Empty);
        }
        base.OnFormClosing(e);
    }
}
