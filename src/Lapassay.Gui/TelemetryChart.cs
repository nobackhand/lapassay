using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Lapassay.Core.Telemetry;

namespace Lapassay.Gui;

/// <summary>
/// Live chart of CPU/GPU watts (left axis) and CPU/GPU temps (right axis) as a
/// continuous timeline across an entire benchmark run. Solid lines = power,
/// dashed = temperature. Auto-scales axes; redraws on collection change.
/// </summary>
public sealed class TelemetryChart : Control
{
    // INSTRUMENT palette — match the App.axaml tokens.
    static readonly IBrush BgBrush   = new SolidColorBrush(Color.Parse("#16110D"));
    static readonly IBrush DimText   = new SolidColorBrush(Color.Parse("#998B78"));
    static readonly IBrush FaintText = new SolidColorBrush(Color.Parse("#5A4F43"));
    static readonly Pen CpuWPen  = new(new SolidColorBrush(Color.Parse("#F97316")), 2);
    static readonly Pen GpuWPen  = new(new SolidColorBrush(Color.Parse("#A3E635")), 2);
    static readonly Pen CpuTPen  = new(new SolidColorBrush(Color.Parse("#F87171")), 1.5) { DashStyle = DashStyle.Dash };
    static readonly Pen GpuTPen  = new(new SolidColorBrush(Color.Parse("#D69D45")), 1.5) { DashStyle = DashStyle.Dash };
    static readonly Pen GridPen  = new(new SolidColorBrush(Color.FromArgb(40, 240, 230, 210)), 1);

    public static readonly StyledProperty<IEnumerable<TelemetrySample>?> SamplesProperty =
        AvaloniaProperty.Register<TelemetryChart, IEnumerable<TelemetrySample>?>(nameof(Samples));

    public IEnumerable<TelemetrySample>? Samples
    {
        get => GetValue(SamplesProperty);
        set => SetValue(SamplesProperty, value);
    }

    INotifyCollectionChanged? _bound;
    DateTimeOffset? _start;

    static TelemetryChart()
    {
        SamplesProperty.Changed.AddClassHandler<TelemetryChart>((c, e) =>
            c.OnSamplesChanged(e.NewValue as IEnumerable<TelemetrySample>));
    }

    void OnSamplesChanged(IEnumerable<TelemetrySample>? newValue)
    {
        if (_bound is not null) _bound.CollectionChanged -= OnCollectionChanged;
        _bound = newValue as INotifyCollectionChanged;
        if (_bound is not null) _bound.CollectionChanged += OnCollectionChanged;
        _start = null;
        InvalidateVisual();
    }

    void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    public override void Render(DrawingContext ctx)
    {
        var bounds = new Rect(Bounds.Size);
        ctx.FillRectangle(BgBrush, bounds);
        ctx.DrawRectangle(GridPen, bounds);

        var samples = Samples is null ? Array.Empty<TelemetrySample>() : System.Linq.Enumerable.ToArray(Samples);
        if (samples.Length < 2)
        {
            DrawCenteredText(ctx, "Live telemetry will appear here once a run starts.", bounds);
            return;
        }

        const double padL = 36, padR = 36, padT = 8, padB = 18;
        var plot = new Rect(padL, padT, Math.Max(1, bounds.Width - padL - padR), Math.Max(1, bounds.Height - padT - padB));

        // Anchor t=0 at the first sample.
        _start ??= samples[0].Timestamp;
        var start = _start.Value;
        var maxT = (samples[^1].Timestamp - start).TotalSeconds;
        if (maxT <= 0) maxT = 1;

        var maxWatts = 5.0;
        var maxTemp = 50.0;
        foreach (var s in samples)
        {
            if (s.CpuPkgWatts.HasValue && s.CpuPkgWatts.Value > maxWatts) maxWatts = s.CpuPkgWatts.Value;
            if (s.GpuWatts.HasValue && s.GpuWatts.Value > maxWatts) maxWatts = s.GpuWatts.Value;
            if (s.CpuTempC.HasValue && s.CpuTempC.Value > maxTemp) maxTemp = s.CpuTempC.Value;
            if (s.GpuTempC.HasValue && s.GpuTempC.Value > maxTemp) maxTemp = s.GpuTempC.Value;
        }
        maxWatts = Math.Ceiling(maxWatts / 5.0) * 5.0;
        maxTemp = Math.Ceiling(maxTemp / 10.0) * 10.0;

        // Gridlines + axis labels (left = W, right = °C)
        for (var i = 0; i <= 3; i++)
        {
            var y = plot.Y + plot.Height * i / 3.0;
            ctx.DrawLine(GridPen, new Point(plot.X, y), new Point(plot.Right, y));
            var w = maxWatts * (3 - i) / 3.0;
            var t = maxTemp * (3 - i) / 3.0;
            DrawText(ctx, $"{w:F0}W", new Point(plot.X - 4, y - 6), DimText, 9, alignRight: true);
            DrawText(ctx, $"{t:F0}°", new Point(plot.Right + 4, y - 6), DimText, 9);
        }

        double X(TelemetrySample s) => plot.X + plot.Width * (s.Timestamp - start).TotalSeconds / maxT;
        double Yw(double w) => plot.Bottom - plot.Height * w / maxWatts;
        double Yt(double t) => plot.Bottom - plot.Height * t / maxTemp;

        var cpuW = new List<Point>(samples.Length);
        var gpuW = new List<Point>(samples.Length);
        var cpuT = new List<Point>(samples.Length);
        var gpuT = new List<Point>(samples.Length);
        foreach (var s in samples)
        {
            var x = X(s);
            if (s.CpuPkgWatts.HasValue) cpuW.Add(new Point(x, Yw(s.CpuPkgWatts.Value)));
            if (s.GpuWatts.HasValue) gpuW.Add(new Point(x, Yw(s.GpuWatts.Value)));
            if (s.CpuTempC.HasValue) cpuT.Add(new Point(x, Yt(s.CpuTempC.Value)));
            if (s.GpuTempC.HasValue) gpuT.Add(new Point(x, Yt(s.GpuTempC.Value)));
        }
        DrawLine(ctx, cpuT, CpuTPen);
        DrawLine(ctx, gpuT, GpuTPen);
        DrawLine(ctx, cpuW, CpuWPen);
        DrawLine(ctx, gpuW, GpuWPen);

        DrawText(ctx, $"{maxT:F0}s", new Point(plot.Right - 26, plot.Bottom + 4), DimText, 9);
    }

    static void DrawLine(DrawingContext ctx, IList<Point> pts, Pen pen)
    {
        if (pts.Count < 2) return;
        for (var i = 1; i < pts.Count; i++) ctx.DrawLine(pen, pts[i - 1], pts[i]);
    }

    static void DrawCenteredText(DrawingContext ctx, string text, Rect bounds)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, 12, DimText);
        ctx.DrawText(ft, new Point((bounds.Width - ft.Width) / 2, (bounds.Height - ft.Height) / 2));
    }

    static void DrawText(DrawingContext ctx, string text, Point at, IBrush brush, double size, bool alignRight = false)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, size, brush);
        var pt = alignRight ? new Point(at.X - ft.Width, at.Y) : at;
        ctx.DrawText(ft, pt);
    }
}
