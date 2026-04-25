using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Lapassay.Core.History;

namespace Lapassay.Gui;

public sealed class HistoryViewModel : INotifyPropertyChanged
{
    string _resultsFolder;
    string _hostnameFilter = "";
    string _statusText = "Click Refresh to scan results folder.";
    bool _isLoading;

    public HistoryViewModel()
    {
        _resultsFolder = Path.GetFullPath("results");
    }

    public ObservableCollection<HistoryRow> Rows { get; } = new();
    public ObservableCollection<HistoryRow> SelectedRows { get; } = new();

    public string ResultsFolder
    {
        get => _resultsFolder;
        set { if (Set(ref _resultsFolder, value)) OnPropertyChanged(nameof(ResultsFolderShort)); }
    }
    public string ResultsFolderShort => _resultsFolder;

    public string HostnameFilter { get => _hostnameFilter; set => Set(ref _hostnameFilter, value); }
    public string StatusText { get => _statusText; set => Set(ref _statusText, value); }
    public bool IsLoading { get => _isLoading; set => Set(ref _isLoading, value); }

    public IReadOnlyList<HistoryEntry> EntriesForChart { get; private set; } = Array.Empty<HistoryEntry>();

    public void Refresh()
    {
        IsLoading = true;
        try
        {
            var entries = HistoryScanner.Scan(_resultsFolder);

            // Apply filter
            if (!string.IsNullOrWhiteSpace(_hostnameFilter))
            {
                var f = _hostnameFilter.Trim();
                entries = entries.Where(e =>
                    e.Hostname.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                    e.FileName.Contains(f, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            Rows.Clear();
            // Track previous-by-time for delta computation (entries are sorted newest-first).
            // Build oldest-first for delta calculation, then reverse for display.
            var oldestFirst = entries.OrderBy(e => e.Timestamp).ToList();
            var deltaByPath = new Dictionary<string, int?>();
            int? prevOverall = null;
            foreach (var e in oldestFirst)
            {
                deltaByPath[e.Path] = prevOverall.HasValue ? e.Overall - prevOverall.Value : null;
                prevOverall = e.Overall;
            }

            foreach (var e in entries)
            {
                var deltaText = deltaByPath.TryGetValue(e.Path, out var d) && d.HasValue
                    ? (d.Value > 0 ? $"+{d.Value}" : d.Value.ToString())
                    : "";
                var deltaSign = deltaByPath.TryGetValue(e.Path, out var d2) && d2.HasValue
                    ? Math.Sign(d2.Value)
                    : 0;
                Rows.Add(new HistoryRow(
                    Entry: e,
                    DateText: e.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                    Hostname: e.Hostname,
                    OverallText: e.Overall.ToString(),
                    CpuText: e.CpuScore > 0 ? e.CpuScore.ToString() : "—",
                    GpuText: e.GpuScore > 0 ? e.GpuScore.ToString() : "—",
                    DeltaText: deltaText,
                    DeltaSign: deltaSign,
                    BenchmarkCountText: $"{e.BenchmarkCount} benches"));
            }
            EntriesForChart = oldestFirst;
            OnPropertyChanged(nameof(EntriesForChart));

            StatusText = entries.Count == 0
                ? $"No matching runs found in {_resultsFolder}."
                : $"{entries.Count} run{(entries.Count == 1 ? "" : "s")} from {_resultsFolder}";
        }
        finally
        {
            IsLoading = false;
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

public sealed record HistoryRow(
    HistoryEntry Entry,
    string DateText,
    string Hostname,
    string OverallText,
    string CpuText,
    string GpuText,
    string DeltaText,
    int DeltaSign,
    string BenchmarkCountText);
