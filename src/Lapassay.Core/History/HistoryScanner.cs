using System.Text.Json;
using Lapassay.Core.Models;

namespace Lapassay.Core.History;

/// <summary>
/// Lightweight summary row for a past benchmark run, derived from a `*.json` on disk.
/// Used to build a history table without keeping the full BenchmarkRun in memory.
/// </summary>
public record HistoryEntry(
    string Path,
    string FileName,
    DateTimeOffset Timestamp,
    string Hostname,
    string CpuModel,
    string GpuModel,
    int Overall,
    int CpuScore,
    int GpuScore,
    IReadOnlyList<CategoryScore> Categories,
    int BenchmarkCount,
    bool IsSustained,
    bool IsAnonymized);

public static class HistoryScanner
{
    static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Scan `folder` for *.json files and return one HistoryEntry per parseable run.
    /// Diff files (filename starts with `diff-`) and sustained runs (have a `verdict` field) are
    /// skipped — only single-shot BenchmarkRun JSONs are returned.</summary>
    public static List<HistoryEntry> Scan(string folder)
    {
        var results = new List<HistoryEntry>();
        if (!Directory.Exists(folder)) return results;

        foreach (var path in Directory.EnumerateFiles(folder, "*.json", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(path);
            if (name.StartsWith("diff-", StringComparison.OrdinalIgnoreCase)) continue;

            try
            {
                var json = File.ReadAllText(path);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("verdict", out _)) continue; // sustained — skip
                if (!doc.RootElement.TryGetProperty("benchmarks", out _)) continue;

                var run = JsonSerializer.Deserialize<BenchmarkRun>(json, Opts);
                if (run is null) continue;

                var anon = name.Contains("-anonymized", StringComparison.OrdinalIgnoreCase);
                results.Add(new HistoryEntry(
                    Path: Path.GetFullPath(path),
                    FileName: name,
                    Timestamp: run.Environment.CapturedAt,
                    Hostname: HostFromRunId(run.RunId),
                    CpuModel: run.Environment.Cpu.Model,
                    GpuModel: run.Environment.Gpu.Count > 0 ? run.Environment.Gpu[0].Model : "—",
                    Overall: run.Scores.Overall,
                    CpuScore: run.Scores.Cpu,
                    GpuScore: run.Scores.Gpu,
                    Categories: run.Scores.Categories,
                    BenchmarkCount: run.Benchmarks.Count,
                    IsSustained: false,
                    IsAnonymized: anon));
            }
            catch
            {
                // Malformed / older-schema file — skip silently.
            }
        }

        results.Sort((a, b) => b.Timestamp.CompareTo(a.Timestamp));
        return results;
    }

    static string HostFromRunId(string runId)
    {
        var zIdx = runId.IndexOf('Z');
        if (zIdx < 0) return "—";
        var afterZ = runId.Substring(zIdx + 1).TrimStart('-');
        var dash = afterZ.IndexOf('-');
        return dash < 0 ? afterZ : afterZ.Substring(0, dash);
    }
}
