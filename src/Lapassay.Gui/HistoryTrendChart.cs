using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Lapassay.Core.History;

namespace Lapassay.Gui;

/// <summary>
/// Plots Overall / CPU / GPU score trend lines across a list of historical
/// runs. X-axis is run time (linear by index — runs are typically far apart
/// in real time so a true time-axis would crush them; index-axis keeps every
/// run readable).
/// </summary>
public sealed class HistoryTrendChart : Control
{
    static readonly IBrush BgBrush   = new SolidColorBrush(Color.Parse("#16110D"));
    static readonly IBrush DimText   = new SolidColorBrush(Color.Parse("#998B78"));
    static readonly Pen OverallPen = new(new SolidColorBrush(Color.Parse("#F97316")), 2);
    static readonly Pen CpuPen     = new(new SolidColorBrush(Color.Parse("#A3E635")), 1.5) { DashStyle = DashStyle.Dash };
    static readonly Pen GpuPen     = new(new SolidColorBrush(Color.Parse("#D69D45")), 1.5) { DashStyle = DashStyle.Dash };
    static readonly Pen GridPen    = new(new SolidColorBrush(Color.FromArgb(40, 240, 230, 210)), 1);
    static readonly IBrush DotBrush = new SolidColorBrush(Color.Parse("#F97316"));

    public static readonly StyledProperty<IReadOnlyList<HistoryEntry>?> EntriesProperty =
        AvaloniaProperty.Register<HistoryTrendChart, IReadOnlyList<HistoryEntry>?>(nameof(Entries));

    public IReadOnlyList<HistoryEntry>? Entries
    {
        get => GetValue(EntriesProperty);
        set => SetValue(EntriesProperty, value);
    }

    static HistoryTrendChart()
    {
        EntriesProperty.Changed.AddClassHandler<HistoryTrendChart>((c, _) => c.InvalidateVisual());
    }

    public override void Render(DrawingContext ctx)
    {
        var bounds = new Rect(Bounds.Size);
        ctx.FillRectangle(BgBrush, bounds);
        ctx.DrawRectangle(GridPen, bounds);

        var entries = Entries ?? Array.Empty<HistoryEntry>();
        if (entries.Count < 1)
        {
            DrawCenteredText(ctx, "No runs yet — click Refresh, or run a benchmark.", bounds);
            return;
        }
        if (entries.Count == 1)
        {
            DrawCenteredText(ctx, $"Only one run in history (overall {entries[0].Overall}). Run again to see a trend.", bounds);
            return;
        }

        const double padL = 36, padR = 14, padT = 10, padB = 22;
        var plot = new Rect(padL, padT, Math.Max(1, bounds.Width - padL - padR), Math.Max(1, bounds.Height - padT - padB));

        var maxScore = 1;
        foreach (var e in entries)
        {
            if (e.Overall > maxScore) maxScore = e.Overall;
            if (e.CpuScore > maxScore) maxScore = e.CpuScore;
            if (e.GpuScore > maxScore) maxScore = e.GpuScore;
        }
        var yMax = Math.Max(100, Math.Ceiling(maxScore / 100.0) * 100.0);

        // Gridlines + labels
        for (var i = 0; i <= 4; i++)
        {
            var y = plot.Y + plot.Height * i / 4;
            var v = yMax * (4 - i) / 4;
            ctx.DrawLine(GridPen, new Point(plot.X, y), new Point(plot.Right, y));
            DrawText(ctx, ((int)v).ToString(CultureInfo.InvariantCulture),
                new Point(plot.X - 4, y - 6), DimText, 10, alignRight: true);
        }

        double X(int i) => entries.Count == 1
            ? plot.X + plot.Width / 2
            : plot.X + plot.Width * i / (entries.Count - 1);
        double Y(int score) => plot.Bottom - plot.Height * score / yMax;

        var overallPts = new List<Point>(entries.Count);
        var cpuPts = new List<Point>(entries.Count);
        var gpuPts = new List<Point>(entries.Count);
        for (var i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            overallPts.Add(new Point(X(i), Y(e.Overall)));
            if (e.CpuScore > 0) cpuPts.Add(new Point(X(i), Y(e.CpuScore)));
            if (e.GpuScore > 0) gpuPts.Add(new Point(X(i), Y(e.GpuScore)));
        }
        DrawLine(ctx, cpuPts, CpuPen);
        DrawLine(ctx, gpuPts, GpuPen);
        DrawLine(ctx, overallPts, OverallPen);
        // Dots on overall line
        foreach (var p in overallPts) ctx.DrawEllipse(DotBrush, null, p, 3, 3);

        // X-axis labels: first and last run dates
        if (entries.Count >= 2)
        {
            DrawText(ctx, entries[0].Timestamp.ToLocalTime().ToString("MMM d HH:mm"),
                new Point(plot.X, plot.Bottom + 4), DimText, 10);
            var lastLabel = entries[^1].Timestamp.ToLocalTime().ToString("MMM d HH:mm");
            var ft = new FormattedText(lastLabel, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, 10, DimText);
            ctx.DrawText(ft, new Point(plot.Right - ft.Width, plot.Bottom + 4));
        }
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
