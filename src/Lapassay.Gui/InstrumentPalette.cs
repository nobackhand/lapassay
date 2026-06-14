using Avalonia.Media;

namespace Lapassay.Gui;

/// <summary>
/// One definition of the instrument palette used by the custom chart controls
/// (<see cref="TelemetryChart"/>, <see cref="SustainedChart"/>,
/// <see cref="HistoryTrendChart"/>). These hexes mirror the design tokens in
/// <c>App.axaml</c> — keep the two in sync. Centralized here so the charts can't
/// drift from each other (they previously each re-parsed the same six colors).
/// </summary>
internal static class InstrumentPalette
{
    public static readonly Color Bg        = Color.Parse("#16110D"); // ColorSurface
    public static readonly Color TextDim   = Color.Parse("#998B78"); // ColorTextDim
    public static readonly Color TextFaint = Color.Parse("#5A4F43"); // ColorTextFaint
    public static readonly Color Accent    = Color.Parse("#F97316"); // ColorAccent — CPU / overall
    public static readonly Color Ok        = Color.Parse("#A3E635"); // ColorOk     — GPU power
    public static readonly Color Bad       = Color.Parse("#F87171"); // ColorBad    — CPU temp
    public static readonly Color Warn      = Color.Parse("#D69D45"); // gold        — GPU temp
    public static readonly Color GridColor = Color.FromArgb(40, 240, 230, 210);

    public static readonly IBrush BgBrush        = new SolidColorBrush(Bg);
    public static readonly IBrush TextDimBrush   = new SolidColorBrush(TextDim);
    public static readonly IBrush TextFaintBrush = new SolidColorBrush(TextFaint);
    public static readonly Pen GridPen           = new(new SolidColorBrush(GridColor), 1);
}
