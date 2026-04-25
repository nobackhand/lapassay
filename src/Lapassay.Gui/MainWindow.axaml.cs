using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Lapassay.Core;
using Lapassay.Core.Models;
using Lapassay.Core.Reporting;
using Lapassay.Core.Sustained;
using Lapassay.Core.Telemetry;

namespace Lapassay.Gui;

[SupportedOSPlatform("windows")]
public partial class MainWindow : Window
{
    MainWindowViewModel Vm => (MainWindowViewModel)DataContext!;

    DispatcherTimer? _powerPoll;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel();
        // Pre-populate the history tab so it shows past runs the moment the user switches to it.
        try { Vm.History.Refresh(); } catch { }
        // Detect power state once and then poll every 1.5s — drives the Battery vs AC tab.
        Vm.BatteryAc.CurrentPower = PowerStateDetector.Current();
        _powerPoll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        _powerPoll.Tick += (_, _) =>
        {
            var s = PowerStateDetector.Current();
            if (s != Vm.BatteryAc.CurrentPower)
            {
                Vm.BatteryAc.CurrentPower = s;
                if (Vm.BatteryAc.State == BatteryAcState.AwaitingSwitch &&
                    s == Vm.BatteryAc.SecondRunPowerExpected)
                {
                    Vm.BatteryAc.StatusText = $"Power state switched to {PowerStateDetector.Describe(s)}. Click Continue to run the second pass.";
                }
            }
        };
        _powerPoll.Start();
    }

    // ===================== Single-shot run =====================

    async void OnRunClicked(object? sender, RoutedEventArgs e)
    {
        if (Vm.IsRunning) return;
        if (!Vm.RunCpu && !Vm.RunGpu)
        {
            Vm.AppendLog("Nothing to do — enable at least one of CPU or GPU.");
            return;
        }

        Vm.IsRunning = true;
        Vm.LogContent = "";
        Vm.Results.Clear();
        Vm.LiveSamples.Clear();

        var options = new Runner.RunOptions(
            Cpu: Vm.RunCpu,
            Gpu: Vm.RunGpu,
            CpuN: Vm.CpuN,
            GpuN: Vm.GpuN,
            OnTelemetrySample: sample => Dispatcher.UIThread.Post(() => Vm.LiveSamples.Add(sample)));
        var outPath = JsonReport.DefaultPath();

        try
        {
            var run = await Task.Run(() => Runner.Run(options, line =>
                Dispatcher.UIThread.Post(() => Vm.AppendLog(line))));

            JsonReport.WriteToFile(run, outPath);
            var htmlPath = Path.ChangeExtension(outPath, ".html");
            HtmlReport.WriteToFile(run, htmlPath);
            Vm.LastOutputPath = Path.GetFullPath(outPath);
            Vm.LastHtmlPath = Path.GetFullPath(htmlPath);
            Vm.HasReport = true;
            Vm.ReplaceResults(run.Benchmarks);
            Vm.SetScores(run.Scores);
            Vm.AppendLog($"\n✔ Wrote {outPath}");
            Vm.AppendLog($"✔ Wrote {htmlPath}");
        }
        catch (Exception ex)
        {
            Vm.AppendLog($"\n✖ ERROR: {ex.Message}");
        }
        finally
        {
            Vm.IsRunning = false;
            ScrollLogToBottom();
        }
    }

    void OnOpenFolderClicked(object? sender, RoutedEventArgs e)
    {
        var folder = Path.GetFullPath("results");
        Directory.CreateDirectory(folder);
        try
        {
            Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Vm.AppendLog($"Could not open folder: {ex.Message}");
        }
    }

    void OnViewReportClicked(object? sender, RoutedEventArgs e)
    {
        OpenInDefaultBrowser(Vm.LastHtmlPath);
    }

    async void OnViewShareableReportClicked(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(Vm.LastOutputPath) || !File.Exists(Vm.LastOutputPath))
        {
            Vm.AppendLog("No run available — click Run first.");
            return;
        }
        try
        {
            // Re-render the JSON to a separate anonymized HTML.
            var anonPath = Vm.LastOutputPath.Replace(".json", "-anonymized.html");
            await Task.Run(() =>
            {
                var jsonText = File.ReadAllText(Vm.LastOutputPath);
                var run = JsonReport.Deserialize(jsonText);
                HtmlReport.WriteToFile(run, anonPath, anonymize: true);
            });
            Vm.AppendLog($"✔ Wrote anonymized {anonPath}");
            OpenInDefaultBrowser(anonPath);
        }
        catch (Exception ex)
        {
            Vm.AppendLog($"Could not generate anonymized report: {ex.Message}");
        }
    }

    void OpenInDefaultBrowser(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            Vm.AppendLog("No report available — click Run first.");
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Vm.AppendLog($"Could not open report: {ex.Message}");
        }
    }

    void ScrollLogToBottom()
    {
        var scroll = this.FindControl<ScrollViewer>("LogScroll");
        scroll?.ScrollToEnd();
    }

    // ===================== Sustained run =====================

    async void OnSustainedStartClicked(object? sender, RoutedEventArgs e)
    {
        var s = Vm.Sustained;
        if (s.IsRunning) return;

        s.IsRunning = true;
        s.HasVerdict = false;
        s.VerdictHeadline = "";
        s.VerdictDetail = "";
        s.Samples.Clear();
        s.ElapsedSec = 0;
        s.CurrentCpuGflops = 0;
        s.CurrentGpuGflops = 0;
        s.CurrentCpuTempC = null;
        s.CurrentGpuTempC = null;
        s.StatusText = "Running...";

        var cts = new CancellationTokenSource();
        s.Cts = cts;

        var durationSec = s.DurationMin * 60.0;
        var outPath = JsonReport.DefaultPath(suffix: "sustained");

        try
        {
            var run = await Task.Run(() => SustainedRunner.Run(
                new SustainedRunner.RunOptions(durationSec),
                onSample: sample => Dispatcher.UIThread.Post(() =>
                {
                    s.Samples.Add(sample);
                    s.ElapsedSec = sample.ElapsedSec;
                    s.CurrentCpuGflops = sample.CpuGflops;
                    s.CurrentGpuGflops = sample.GpuGflops;
                    s.CurrentCpuTempC = sample.CpuTempC;
                    s.CurrentGpuTempC = sample.GpuTempC;
                }),
                log: _ => { },
                cancel: cts.Token));

            JsonReport.WriteToFile(run, outPath);
            var htmlPath = Path.ChangeExtension(outPath, ".html");
            HtmlReport.WriteToFile(run, htmlPath);
            s.LastJsonPath = Path.GetFullPath(outPath);
            s.LastHtmlPath = Path.GetFullPath(htmlPath);
            s.HasVerdict = true;
            s.VerdictThrottled = run.Verdict.Throttled;
            s.VerdictHeadline = run.Verdict.Throttled
                ? "⚠ Throttle detected"
                : "✓ No significant throttling";
            s.VerdictDetail =
                $"CPU first→last: {run.Verdict.FirstWindowCpuGflops:F1} → {run.Verdict.LastWindowCpuGflops:F1} GFLOPS ({run.Verdict.CpuDropPct:F1}% drop)   |   " +
                $"GPU: {run.Verdict.FirstWindowGpuGflops:F1} → {run.Verdict.LastWindowGpuGflops:F1} GFLOPS ({run.Verdict.GpuDropPct:F1}% drop)   |   " +
                $"{run.IterationCount} iterations in {run.DurationSec:F0}s";
            s.StatusText = $"Done. Wrote {outPath}";
        }
        catch (Exception ex)
        {
            s.HasVerdict = true;
            s.VerdictHeadline = "✖ Error";
            s.VerdictDetail = ex.Message;
        }
        finally
        {
            s.IsRunning = false;
            s.Cts = null;
            cts.Dispose();
        }
    }

    void OnSustainedStopClicked(object? sender, RoutedEventArgs e)
    {
        Vm.Sustained.Cts?.Cancel();
    }

    void OnSustainedViewReportClicked(object? sender, RoutedEventArgs e)
    {
        OpenInDefaultBrowser(Vm.Sustained.LastHtmlPath);
    }

    void OnHistoryRefreshClicked(object? sender, RoutedEventArgs e)
    {
        Vm.History.Refresh();
        // Auto-derive HTML report path: same name with .html extension. If missing, generate it.
        // For now we just rely on the existing report next to each JSON.
    }

    void OnHistoryOpenReportClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string jsonPath) return;
        var htmlPath = Path.ChangeExtension(jsonPath, ".html");
        if (!File.Exists(htmlPath))
        {
            // Generate on demand from the JSON.
            try
            {
                var json = File.ReadAllText(jsonPath);
                var run = JsonReport.Deserialize(json);
                HtmlReport.WriteToFile(run, htmlPath);
            }
            catch (Exception ex)
            {
                Vm.AppendLog($"Could not generate report: {ex.Message}");
                return;
            }
        }
        OpenInDefaultBrowser(htmlPath);
    }

    void OnHistoryCompareSelectedClicked(object? sender, RoutedEventArgs e)
    {
        var listBox = this.FindControl<ListBox>("HistoryList");
        if (listBox is null) return;
        var picked = listBox.SelectedItems?.OfType<HistoryRow>().ToList() ?? new List<HistoryRow>();
        if (picked.Count != 2)
        {
            Vm.AppendLog($"Compare needs exactly 2 selected runs (you have {picked.Count}).");
            return;
        }
        // Order: older first → newer second so the diff arrow reads chronologically.
        var ordered = picked.OrderBy(r => r.Entry.Timestamp).ToList();
        var pathA = ordered[0].Entry.Path;
        var pathB = ordered[1].Entry.Path;
        try
        {
            var jsonOpts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            var runA = JsonSerializer.Deserialize<BenchmarkRun>(File.ReadAllText(pathA), jsonOpts)!;
            var runB = JsonSerializer.Deserialize<BenchmarkRun>(File.ReadAllText(pathB), jsonOpts)!;
            var labelA = Path.GetFileNameWithoutExtension(pathA);
            var labelB = Path.GetFileNameWithoutExtension(pathB);
            var cmp = Lapassay.Core.Reporting.Compare.Diff(runA, runB, labelA, labelB);
            var outPath = Path.Combine(Path.GetDirectoryName(pathB) ?? ".", $"diff-{labelA}-vs-{labelB}.html");
            HtmlReport.WriteToFile(cmp, outPath);
            OpenInDefaultBrowser(outPath);
        }
        catch (Exception ex)
        {
            Vm.AppendLog($"Compare failed: {ex.Message}");
        }
    }

    async void OnBatteryAcStartClicked(object? sender, RoutedEventArgs e)
    {
        var b = Vm.BatteryAc;
        if (b.IsBusy) return;

        // Detect now and capture the starting state.
        var startPower = PowerStateDetector.Current();
        if (startPower == PowerState.Unknown)
        {
            b.StatusText = "Could not detect power state — does this machine have a battery?";
            b.State = BatteryAcState.Error;
            return;
        }
        b.CurrentPower = startPower;
        b.FirstRunPower = startPower;
        b.SecondRunPowerExpected = startPower == PowerState.OnAc ? PowerState.OnBattery : PowerState.OnAc;
        b.FirstScoreText = "";
        b.SecondScoreText = "";
        b.FirstJsonPath = "";
        b.SecondJsonPath = "";
        b.DiffPath = "";

        b.State = BatteryAcState.RunningFirst;
        b.StatusText = $"Running on {PowerStateDetector.Describe(startPower)}…";

        var firstRun = await RunOnceWithLiveTelemetry($"battery-ac-{PowerStateDetector.Describe(startPower).ToLower()}");
        if (firstRun is null) { b.State = BatteryAcState.Error; return; }
        b.FirstScoreText = ScoresLine(firstRun.Value.run);
        b.FirstJsonPath = firstRun.Value.savedJson;

        b.State = BatteryAcState.AwaitingSwitch;
        b.StatusText = $"First run done. Now switch to {PowerStateDetector.Describe(b.SecondRunPowerExpected)} (un{(b.SecondRunPowerExpected == PowerState.OnBattery ? "" : "")}plug the charger) and click Continue.";
        if (b.SecondRunPowerExpected == PowerState.OnBattery)
            b.StatusText = "First run done. Now UNPLUG the charger and click Continue.";
        else
            b.StatusText = "First run done. Now PLUG IN the charger and click Continue.";
    }

    async void OnBatteryAcContinueClicked(object? sender, RoutedEventArgs e)
    {
        var b = Vm.BatteryAc;
        if (b.State != BatteryAcState.AwaitingSwitch) return;
        if (b.CurrentPower != b.SecondRunPowerExpected)
        {
            b.StatusText = $"Still detecting {PowerStateDetector.Describe(b.CurrentPower)} — switch power first, then click Continue.";
            return;
        }

        b.State = BatteryAcState.RunningSecond;
        b.StatusText = $"Running on {PowerStateDetector.Describe(b.SecondRunPowerExpected)}…";

        var secondRun = await RunOnceWithLiveTelemetry($"battery-ac-{PowerStateDetector.Describe(b.SecondRunPowerExpected).ToLower()}");
        if (secondRun is null) { b.State = BatteryAcState.Error; return; }
        b.SecondScoreText = ScoresLine(secondRun.Value.run);
        b.SecondJsonPath = secondRun.Value.savedJson;

        // Generate diff: first vs second.
        try
        {
            var jsonOpts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            var runA = JsonSerializer.Deserialize<BenchmarkRun>(File.ReadAllText(b.FirstJsonPath), jsonOpts)!;
            var runB = JsonSerializer.Deserialize<BenchmarkRun>(File.ReadAllText(b.SecondJsonPath), jsonOpts)!;
            var labelA = PowerStateDetector.Describe(b.FirstRunPower);
            var labelB = PowerStateDetector.Describe(b.SecondRunPowerExpected);
            var cmp = Lapassay.Core.Reporting.Compare.Diff(runA, runB, labelA, labelB);
            var outPath = Path.Combine(Path.GetDirectoryName(b.SecondJsonPath) ?? "results",
                $"battery-ac-{labelA.ToLower()}-vs-{labelB.ToLower()}.html");
            HtmlReport.WriteToFile(cmp, outPath);
            b.DiffPath = Path.GetFullPath(outPath);
            b.StatusText = $"Done. {labelA} → {labelB}: overall {runA.Scores.Overall} → {runB.Scores.Overall} " +
                           $"({cmp.OverallScoreDelta:+0;-0;0}).";
            b.State = BatteryAcState.Complete;
        }
        catch (Exception ex)
        {
            b.StatusText = $"Diff failed: {ex.Message}";
            b.State = BatteryAcState.Error;
        }
    }

    void OnBatteryAcViewComparisonClicked(object? sender, RoutedEventArgs e)
    {
        OpenInDefaultBrowser(Vm.BatteryAc.DiffPath);
    }

    async Task<(BenchmarkRun run, string savedJson)?> RunOnceWithLiveTelemetry(string suffix)
    {
        Vm.LiveSamples.Clear();
        var outPath = JsonReport.DefaultPath(suffix: suffix);
        try
        {
            var run = await Task.Run(() => Runner.Run(
                new Runner.RunOptions(
                    Cpu: true,
                    Gpu: true,
                    OnTelemetrySample: s => Dispatcher.UIThread.Post(() => Vm.LiveSamples.Add(s))),
                _ => { /* logs are noisy; rely on the chart + status banner instead */ }));
            JsonReport.WriteToFile(run, outPath);
            HtmlReport.WriteToFile(run, Path.ChangeExtension(outPath, ".html"));
            return (run, Path.GetFullPath(outPath));
        }
        catch (Exception ex)
        {
            Vm.BatteryAc.StatusText = $"Run failed: {ex.Message}";
            return null;
        }
    }

    static string ScoresLine(BenchmarkRun run)
    {
        var s = run.Scores;
        return $"Overall {s.Overall}    CPU {s.Cpu}    GPU {s.Gpu}";
    }

    async void OnCompareClicked(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null) return;

        IStorageFolder? resultsFolder = null;
        try { resultsFolder = await topLevel.StorageProvider.TryGetFolderFromPathAsync(Path.GetFullPath("results")); }
        catch { /* ignore */ }

        var jsonFilter = new FilePickerFileType("Lapassay JSON") { Patterns = new[] { "*.json" } };

        var pickedA = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Compare runs — pick the BASELINE (A)",
            AllowMultiple = false,
            SuggestedStartLocation = resultsFolder,
            FileTypeFilter = new[] { jsonFilter },
        });
        if (pickedA.Count == 0) return;

        var pickedB = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Compare runs — pick the NEW run (B)",
            AllowMultiple = false,
            SuggestedStartLocation = resultsFolder,
            FileTypeFilter = new[] { jsonFilter },
        });
        if (pickedB.Count == 0) return;

        var pathA = pickedA[0].Path.LocalPath;
        var pathB = pickedB[0].Path.LocalPath;

        try
        {
            var jsonOpts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            BenchmarkRun runA, runB;
            await Task.Run(() =>
            {
                runA = JsonSerializer.Deserialize<BenchmarkRun>(File.ReadAllText(pathA), jsonOpts)!;
                runB = JsonSerializer.Deserialize<BenchmarkRun>(File.ReadAllText(pathB), jsonOpts)!;
                var labelA = Path.GetFileNameWithoutExtension(pathA);
                var labelB = Path.GetFileNameWithoutExtension(pathB);
                var cmp = Lapassay.Core.Reporting.Compare.Diff(runA, runB, labelA, labelB);
                var outPath = Path.Combine(Path.GetDirectoryName(pathB) ?? ".", $"diff-{labelA}-vs-{labelB}.html");
                HtmlReport.WriteToFile(cmp, outPath);
                Process.Start(new ProcessStartInfo { FileName = outPath, UseShellExecute = true });
            });
        }
        catch (Exception ex)
        {
            Vm.AppendLog($"Compare failed: {ex.Message}");
        }
    }
}
