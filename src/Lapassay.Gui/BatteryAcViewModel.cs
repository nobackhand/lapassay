using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Threading;
using Lapassay.Core.Telemetry;

namespace Lapassay.Gui;

public enum BatteryAcState
{
    Idle,
    RunningFirst,
    AwaitingSwitch,
    RunningSecond,
    Complete,
    Error,
}

[SupportedOSPlatform("windows")]
public sealed class BatteryAcViewModel : INotifyPropertyChanged
{
    BatteryAcState _state = BatteryAcState.Idle;
    PowerState _currentPower = PowerState.Unknown;
    PowerState _firstRunPower = PowerState.Unknown;
    PowerState _secondRunPowerExpected = PowerState.Unknown;
    string _statusText = "Detect power state and click Start.";
    string _firstScoreText = "";
    string _secondScoreText = "";
    string _diffPath = "";
    string _firstJsonPath = "";
    string _secondJsonPath = "";

    public BatteryAcState State
    {
        get => _state;
        set
        {
            if (Set(ref _state, value))
            {
                OnPropertyChanged(nameof(IsStartEnabled));
                OnPropertyChanged(nameof(IsContinueEnabled));
                OnPropertyChanged(nameof(IsViewComparisonEnabled));
                OnPropertyChanged(nameof(IsBusy));
            }
        }
    }
    public PowerState CurrentPower
    {
        get => _currentPower;
        set { if (Set(ref _currentPower, value)) OnPropertyChanged(nameof(CurrentPowerText)); }
    }
    public string CurrentPowerText => PowerStateDetector.Describe(_currentPower);
    public PowerState FirstRunPower { get => _firstRunPower; set => Set(ref _firstRunPower, value); }
    public PowerState SecondRunPowerExpected
    {
        get => _secondRunPowerExpected;
        set { if (Set(ref _secondRunPowerExpected, value)) OnPropertyChanged(nameof(SecondRunPowerExpectedText)); }
    }
    public string SecondRunPowerExpectedText => PowerStateDetector.Describe(_secondRunPowerExpected);

    public string StatusText { get => _statusText; set => Set(ref _statusText, value); }
    public string FirstScoreText { get => _firstScoreText; set => Set(ref _firstScoreText, value); }
    public string SecondScoreText { get => _secondScoreText; set => Set(ref _secondScoreText, value); }
    public string DiffPath { get => _diffPath; set => Set(ref _diffPath, value); }
    public string FirstJsonPath { get => _firstJsonPath; set => Set(ref _firstJsonPath, value); }
    public string SecondJsonPath { get => _secondJsonPath; set => Set(ref _secondJsonPath, value); }

    public bool IsStartEnabled => _state == BatteryAcState.Idle || _state == BatteryAcState.Complete || _state == BatteryAcState.Error;
    public bool IsContinueEnabled => _state == BatteryAcState.AwaitingSwitch && _currentPower == _secondRunPowerExpected;
    public bool IsViewComparisonEnabled => _state == BatteryAcState.Complete && !string.IsNullOrEmpty(_diffPath);
    public bool IsBusy => _state == BatteryAcState.RunningFirst || _state == BatteryAcState.RunningSecond;

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
