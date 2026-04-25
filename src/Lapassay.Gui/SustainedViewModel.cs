using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using Lapassay.Core.Models;

namespace Lapassay.Gui;

public sealed class SustainedViewModel : INotifyPropertyChanged
{
    bool _isRunning;
    int _durationMin = 10;
    double _elapsedSec;
    double _currentCpuGflops;
    double _currentGpuGflops;
    double? _currentCpuTempC;
    double? _currentGpuTempC;
    string _statusText = "Ready. Set duration and click Start.";
    bool _hasVerdict;
    string _verdictHeadline = "";
    string _verdictDetail = "";
    bool _verdictThrottled;
    string _lastJsonPath = "";
    string _lastHtmlPath = "";

    public ObservableCollection<SustainedSample> Samples { get; } = new();

    public bool IsRunning
    {
        get => _isRunning;
        set { if (Set(ref _isRunning, value)) { OnPropertyChanged(nameof(StartButtonEnabled)); OnPropertyChanged(nameof(StopButtonEnabled)); } }
    }
    public bool StartButtonEnabled => !IsRunning;
    public bool StopButtonEnabled => IsRunning;

    public int DurationMin { get => _durationMin; set => Set(ref _durationMin, value); }
    public double ElapsedSec { get => _elapsedSec; set { if (Set(ref _elapsedSec, value)) OnPropertyChanged(nameof(ElapsedText)); } }
    public string ElapsedText => $"{(int)(ElapsedSec / 60):D1}:{(int)(ElapsedSec % 60):D2}";

    public double CurrentCpuGflops { get => _currentCpuGflops; set => Set(ref _currentCpuGflops, value); }
    public double CurrentGpuGflops { get => _currentGpuGflops; set => Set(ref _currentGpuGflops, value); }
    public double? CurrentCpuTempC { get => _currentCpuTempC; set { if (Set(ref _currentCpuTempC, value)) OnPropertyChanged(nameof(CpuTempText)); } }
    public double? CurrentGpuTempC { get => _currentGpuTempC; set { if (Set(ref _currentGpuTempC, value)) OnPropertyChanged(nameof(GpuTempText)); } }
    public string CpuTempText => CurrentCpuTempC.HasValue ? $"{CurrentCpuTempC.Value:F0}°C" : "—";
    public string GpuTempText => CurrentGpuTempC.HasValue ? $"{CurrentGpuTempC.Value:F0}°C" : "—";

    public string StatusText { get => _statusText; set => Set(ref _statusText, value); }

    public bool HasVerdict { get => _hasVerdict; set => Set(ref _hasVerdict, value); }
    public string VerdictHeadline { get => _verdictHeadline; set => Set(ref _verdictHeadline, value); }
    public string VerdictDetail { get => _verdictDetail; set => Set(ref _verdictDetail, value); }
    public bool VerdictThrottled { get => _verdictThrottled; set => Set(ref _verdictThrottled, value); }
    public string LastJsonPath { get => _lastJsonPath; set => Set(ref _lastJsonPath, value); }
    public string LastHtmlPath { get => _lastHtmlPath; set => Set(ref _lastHtmlPath, value); }

    public CancellationTokenSource? Cts { get; set; }

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
