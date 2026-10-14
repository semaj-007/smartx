using System.Drawing;

namespace SmartX.Desktop;

/// <summary>Central colour palette for the dashboard's light theme, so nothing is left on WinForms' default system colours.</summary>
public static class UiTheme
{
    public static readonly Color Background = Color.FromArgb(244, 246, 244);  // page background
    public static readonly Color Surface = Color.White;                       // card / groupbox / grid background
    public static readonly Color TextPrimary = Color.FromArgb(28, 32, 30);    // body text
    public static readonly Color TextSecondary = Color.FromArgb(100, 108, 102); // hints / captions
    public static readonly Color Accent = Color.FromArgb(18, 128, 74);        // brand green
    public static readonly Color Error = Color.FromArgb(178, 42, 42);         // status errors (dark enough for a light bg)
    public static readonly Color Border = Color.FromArgb(214, 218, 212);
    public static readonly Color ButtonSecondaryBack = Color.FromArgb(248, 249, 247);
    public static readonly Color DisabledBack = Color.FromArgb(237, 238, 235);
    public static readonly Color DisabledText = Color.FromArgb(168, 172, 166);
    public static readonly Color GridHeaderBack = Color.FromArgb(232, 240, 234);
}
