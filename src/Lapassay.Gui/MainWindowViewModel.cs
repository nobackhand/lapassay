using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using Lapassay.Core;
using Lapassay.Core.Models;
using Lapassay.Core.Telemetry;

namespace Lapassay.Gui;

[SupportedOSPlatform("windows")]
public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    bool _runCpu = true;
    bool _runGpu = true;
    int _cpuN = 1024;
    int _gpuN = 2048;
    bool _isRunning;
    string _logContent = "Ready. Click Run to start.\n";
    string _lastOutputPath = "";
    string _lastHtmlPath = "";
    bool _hasReport;
    bool _hasPreflightWarnings;
    int _overallScore;
    int _cpuScore;
    int _gpuScore;
    bool _hasScores;

    public MainWindowViewModel()
    {
        PreflightMessages = new ObservableCollection<string>();
        Results = new ObservableCollection<ResultRow>();
        RefreshPreflight();
    }

    public bool RunCpu { get => _runCpu; set => Set(ref _runCpu, value); }
    public bool RunGpu { get => _runGpu; set => Set(ref _runGpu, value); }
    public int CpuN { get => _cpuN; set => Set(ref _cpuN, value); }
    public int GpuN { get => _gpuN; set => Set(ref _gpuN, value); }
    public bool IsRunning
    {
        get => _isRunning;
        set { if (Set(ref _isRunning, value)) OnPropertyChanged(nameof(RunButtonLabel)); }
    }
    public string RunButtonLabel => IsRunning ? "Running…" : "Run";
    public string LogContent { get => _logContent; set => Set(ref _logContent, value); }
    public string LastOutputPath { get => _lastOutputPath; set => Set(ref _lastOutputPath, value); }
    public string LastHtmlPath { get => _lastHtmlPath; set => Set(ref _lastHtmlPath, value); }
    public bool HasReport { get => _hasReport; set => Set(ref _hasReport, value); }
    public bool HasPreflightWarnings { get => _hasPreflightWarnings; set => Set(ref _hasPreflightWarnings, value); }

    public ObservableCollection<string> PreflightMessages { get; }
    public ObservableCollection<ResultRow> Results { get; }
    public ObservableCollection<TelemetrySample> LiveSamples { get; } = new();
    public SustainedViewModel Sustained { get; } = new();
    public HistoryViewModel History { get; } = new();
    public BatteryAcViewModel BatteryAc { get; } = new();

    public int OverallScore { get => _overallScore; set => Set(ref _overallScore, value); }
    public int CpuScore { get => _cpuScore; set => Set(ref _cpuScore, value); }
    public int GpuScore { get => _gpuScore; set => Set(ref _gpuScore, value); }
    public bool HasScores { get => _hasScores; set => Set(ref _hasScores, value); }
    public bool HasCpuScore => _cpuScore > 0;
    public bool HasGpuScore => _gpuScore > 0;

    public ObservableCollection<CategoryRow> CategoryRows { get; } = new();

    public void SetScores(Lapassay.Core.Models.Scores scores)
    {
        OverallScore = scores.Overall;
        CpuScore = scores.Cpu;
        GpuScore = scores.Gpu;
        HasScores = scores.Overall > 0;
        OnPropertyChanged(nameof(HasCpuScore));
        OnPropertyChanged(nameof(HasGpuScore));

        CategoryRows.Clear();
        foreach (var c in scores.Categories)
            CategoryRows.Add(new CategoryRow(PrettyCategory(c.Name), c.Score.ToString()));
    }

    static string PrettyCategory(string raw) => raw switch
    {
        "cpu.integer"  => "CPU int",
        "cpu.float"    => "CPU FP",
        "cpu.memory"   => "Memory",
        "cpu.parallel" => "Scaling",
        "gpu.compute"  => "GPU compute",
        "gpu.ai"       => "GPU AI",
        _              => raw,
    };

    public void RefreshPreflight()
    {
        var pf = Preflight.Check();
        PreflightMessages.Clear();
        foreach (var m in pf.Messages) PreflightMessages.Add(m);
        HasPreflightWarnings = PreflightMessages.Count > 0;
    }

    public void AppendLog(string line)
    {
        LogContent += line.EndsWith('\n') ? line : line + "\n";
    }

    public void ReplaceResults(IEnumerable<BenchmarkResult> results)
    {
        Results.Clear();
        foreach (var r in results)
        {
            var stdevPct = r.Stats.Median != 0 ? r.Stats.Stdev / r.Stats.Median * 100 : 0;
            Results.Add(new ResultRow(r.Id, r.Metric, $"{r.Value:F1}", r.Score.ToString(), $"{stdevPct:F2}%"));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}

public sealed record ResultRow(string Id, string Metric, string ValueText, string ScoreText, string StdevPctText);
public sealed record CategoryRow(string Label, string ScoreText);
