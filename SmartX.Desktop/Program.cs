using SmartX.Desktop.Forms;

namespace SmartX.Desktop;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // Windows Forms application entry point (GeeksforGeeks, 2025)
        Application.Run(new MainMenuForm());
    }
}

// References (Harvard style)
// GeeksforGeeks (2025) 'Button in C#'. Available at: https://www.geeksforgeeks.org/c-sharp/button-in-c-sharp/ (Accessed: 14 September 2026).
// GeeksforGeeks (2025) 'C# - GroupBox Class'. Available at: https://www.geeksforgeeks.org/c-sharp/c-sharp-groupbox-class/ (Accessed: 14 September 2026).
// GeeksforGeeks (2019) 'C# | Graphics.DrawLine() Method | Set - 1'. Available at: https://www.geeksforgeeks.org/c-sharp/c-sharp-graphics-drawline-method-set-1/ (Accessed: 14 September 2026).
// GeeksforGeeks (2025) 'C# | Random.NextBytes() Method'. Available at: https://www.geeksforgeeks.org/c-sharp/c-sharp-random-nextbytes-method/ (Accessed: 14 September 2026).
// GeeksforGeeks (2025) 'ComboBox in C#'. Available at: https://www.geeksforgeeks.org/c-sharp/combobox-in-c-sharp/ (Accessed: 14 September 2026).
// GeeksforGeeks (2022) 'How to Read Data From Website Using C#?'. Available at: https://www.geeksforgeeks.org/c-sharp/how-to-read-data-from-website-using-c-sharp/ (Accessed: 14 September 2026).
// GeeksforGeeks (2025) 'Introduction to C# Windows Forms Applications'. Available at: https://www.geeksforgeeks.org/c-sharp/introduction-to-c-sharp-windows-forms-applications/ (Accessed: 14 September 2026).
// Microsoft (n.d.) 'DataGridView Class (System.Windows.Forms)'. Available at: https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.datagridview (Accessed: 14 September 2026).
// Microsoft (n.d.) 'JSON serialization and deserialization (marshalling and unmarshalling) in .NET - overview'. Available at: https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/overview (Accessed: 14 September 2026).
// Microsoft (n.d.) 'OpenFileDialog Class (System.Windows.Forms)'. Available at: https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.openfiledialog (Accessed: 14 September 2026).
// Microsoft (n.d.) 'Timer Class (System.Windows.Forms)'. Available at: https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.timer (Accessed: 14 September 2026).
