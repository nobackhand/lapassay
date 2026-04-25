using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Lapassay.Gui;

/// <summary>
/// Faint dot lattice rendered behind the entire window. Reads as an
/// "instrument panel graticule" — barely visible, but enough to make
/// the dark background feel intentional rather than blank.
/// </summary>
public sealed class BackgroundGrid : Control
{
    static readonly IBrush DotBrush = new SolidColorBrush(Color.FromArgb(28, 240, 230, 210));

    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<BackgroundGrid, double>(nameof(Spacing), 24.0);

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    static BackgroundGrid()
    {
        SpacingProperty.Changed.AddClassHandler<BackgroundGrid>((c, _) => c.InvalidateVisual());
        AffectsRender<BackgroundGrid>(BoundsProperty);
    }

    public override void Render(DrawingContext ctx)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        var s = Spacing;
        if (w <= 0 || h <= 0 || s <= 0) return;

        // Single 1px square dot per lattice point. Cheap, no antialiasing artifacts at this size.
        const double dotSize = 1.0;
        for (var y = s / 2; y < h; y += s)
        {
            for (var x = s / 2; x < w; x += s)
            {
                ctx.FillRectangle(DotBrush, new Rect(x, y, dotSize, dotSize));
            }
        }
    }
}
