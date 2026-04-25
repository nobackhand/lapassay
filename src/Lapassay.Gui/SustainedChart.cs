using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Lapassay.Core.Models;

namespace Lapassay.Gui;

/// <summary>
/// Lightweight Avalonia control that renders a live line chart of CPU GFLOPS,
/// GPU GFLOPS, CPU temp, and GPU temp from an observable list of SustainedSamples.
/// X-axis is elapsed seconds, two Y-axes (left = GFLOPS, right = °C). Auto-scales
/// to current data range. Redraws on collection change.
/// </summary>
public sealed class SustainedChart : Control
{
    static readonly IBrush BgBrush   = new SolidColorBrush(Color.Parse("#16110D"));
    static readonly IBrush DimText   = new SolidColorBrush(Color.Parse("#998B78"));
    static readonly IBrush FaintText = new SolidColorBrush(Color.Parse("#5A4F43"));
    static readonly Pen CpuPen   = new(new SolidColorBrush(Color.Parse("#F97316")), 2);
    static readonly Pen GpuPen   = new(new SolidColorBrush(Color.Parse("#A3E635")), 2);
    static readonly Pen CpuTPen  = new(new SolidColorBrush(Color.Parse("#F87171")), 1.5) { DashStyle = DashStyle.Dash };
    static readonly Pen GpuTPen  = new(new SolidColorBrush(Color.Parse("#D69D45")), 1.5) { DashStyle = DashStyle.Dash };
    static readonly Pen GridPen  = new(new SolidColorBrush(Color.FromArgb(40, 240, 230, 210)), 1);

    public static readonly StyledProperty<IEnumerable<SustainedSample>?> SamplesProperty =
        AvaloniaProperty.Register<SustainedChart, IEnumerable<SustainedSample>?>(nameof(Samples));

    public IEnumerable<SustainedSample>? Samples
    {
        get => GetValue(SamplesProperty);
        set => SetValue(SamplesProperty, value);
    }

    INotifyCollectionChanged? _bound;

    static SustainedChart()
    {
        SamplesProperty.Changed.AddClassHandler<SustainedChart>((c, e) => c.OnSamplesChanged(e.OldValue as IEnumerable<SustainedSample>, e.NewValue as IEnumerable<SustainedSample>));
    }

    void OnSamplesChanged(IEnumerable<SustainedSample>? oldValue, IEnumerable<SustainedSample>? newValue)
    {
        if (_bound is not null) _bound.CollectionChanged -= OnCollectionChanged;
        _bound = newValue as INotifyCollectionChanged;
        if (_bound is not null) _bound.CollectionChanged += OnCollectionChanged;
        InvalidateVisual();
    }

    void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    public override void Render(DrawingContext ctx)
    {
        var bounds = new Rect(Bounds.Size);
        ctx.FillRectangle(BgBrush, bounds);
        ctx.DrawRectangle(GridPen, bounds);

        var samples = Samples is null ? Array.Empty<SustainedSample>() : System.Linq.Enumerable.ToArray(Samples);
        if (samples.Length < 2)
        {
            DrawCenteredText(ctx, "Live chart will populate when sustained run starts", bounds);
            return;
        }

        var pad = 30.0;
        var plot = new Rect(pad + 20, 10, bounds.Width - pad - 50, bounds.Height - pad);
        if (plot.Width <= 0 || plot.Height <= 0) return;

        var maxT = 0.0;
        var maxGflops = 1.0;
        var maxTemp = 50.0;
        foreach (var s in samples)
        {
            if (s.ElapsedSec > maxT) maxT = s.ElapsedSec;
            if (s.CpuGflops > maxGflops) maxGflops = s.CpuGflops;
            if (s.GpuGflops > maxGflops) maxGflops = s.GpuGflops;
            if (s.CpuTempC.HasValue && s.CpuTempC.Value > maxTemp) maxTemp = s.CpuTempC.Value;
            if (s.GpuTempC.HasValue && s.GpuTempC.Value > maxTemp) maxTemp = s.GpuTempC.Value;
        }
        // Round up axis maxes for cleaner ticks.
        maxGflops = Math.Ceiling(maxGflops / 10.0) * 10.0;
        maxTemp = Math.Ceiling(maxTemp / 10.0) * 10.0;

        // Gridlines
        for (var i = 1; i < 5; i++)
        {
            var y = plot.Y + plot.Height * i / 5;
            ctx.DrawLine(GridPen, new Point(plot.X, y), new Point(plot.Right, y));
        }

        // Axis labels (left = GFLOPS, right = °C)
        for (var i = 0; i <= 4; i++)
        {
            var v = maxGflops * (4 - i) / 4;
            var t = maxTemp * (4 - i) / 4;
            var y = plot.Y + plot.Height * i / 4;
            DrawText(ctx, $"{v:F0}", new Point(plot.X - 4, y - 6), DimText, 10, alignRight: true);
            DrawText(ctx, $"{t:F0}°", new Point(plot.Right + 4, y - 6), DimText, 10);
        }

        // Build lines
        var cpuPts = new List<Point>(samples.Length);
        var gpuPts = new List<Point>(samples.Length);
        var cpuTPts = new List<Point>(samples.Length);
        var gpuTPts = new List<Point>(samples.Length);
        foreach (var s in samples)
        {
            var x = plot.X + plot.Width * (s.ElapsedSec / Math.Max(maxT, 1));
            cpuPts.Add(new Point(x, plot.Bottom - plot.Height * (s.CpuGflops / maxGflops)));
            gpuPts.Add(new Point(x, plot.Bottom - plot.Height * (s.GpuGflops / maxGflops)));
            if (s.CpuTempC.HasValue) cpuTPts.Add(new Point(x, plot.Bottom - plot.Height * (s.CpuTempC.Value / maxTemp)));
            if (s.GpuTempC.HasValue) gpuTPts.Add(new Point(x, plot.Bottom - plot.Height * (s.GpuTempC.Value / maxTemp)));
        }
        DrawPolyline(ctx, cpuPts, CpuPen);
        DrawPolyline(ctx, gpuPts, GpuPen);
        DrawPolyline(ctx, cpuTPts, CpuTPen);
        DrawPolyline(ctx, gpuTPts, GpuTPen);

        // Legend (top-right)
        DrawLegend(ctx, plot);
    }

    static void DrawPolyline(DrawingContext ctx, IList<Point> pts, Pen pen)
    {
        if (pts.Count < 2) return;
        for (var i = 1; i < pts.Count; i++)
            ctx.DrawLine(pen, pts[i - 1], pts[i]);
    }

    static void DrawCenteredText(DrawingContext ctx, string text, Rect bounds)
    {
        var ft = new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, Typeface.Default, 12, DimText);
        ctx.DrawText(ft, new Point((bounds.Width - ft.Width) / 2, (bounds.Height - ft.Height) / 2));
    }

    static void DrawText(DrawingContext ctx, string text, Point at, IBrush brush, double size, bool alignRight = false)
    {
        var ft = new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, Typeface.Default, size, brush);
        var pt = alignRight ? new Point(at.X - ft.Width, at.Y) : at;
        ctx.DrawText(ft, pt);
    }

    static void DrawLegend(DrawingContext ctx, Rect plot)
    {
        var entries = new (string label, Pen pen)[]
        {
            ("CPU GFLOPS", CpuPen),
            ("GPU GFLOPS", GpuPen),
            ("CPU °C",     CpuTPen),
            ("GPU °C",     GpuTPen),
        };
        var x = plot.X + 8;
        var y = plot.Y + 4;
        foreach (var (label, pen) in entries)
        {
            ctx.DrawLine(pen, new Point(x, y + 6), new Point(x + 18, y + 6));
            DrawText(ctx, label, new Point(x + 22, y), DimText, 10);
            x += 92;
        }
    }
}
