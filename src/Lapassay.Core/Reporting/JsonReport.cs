using System.Text.Json;
using System.Text.Json.Serialization;
using Lapassay.Core.Models;

namespace Lapassay.Core.Reporting;

public static class JsonReport
{
    static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public static string Serialize(BenchmarkRun run) => JsonSerializer.Serialize(run, Opts);
    public static BenchmarkRun Deserialize(string json) =>
        JsonSerializer.Deserialize<BenchmarkRun>(json, Opts)
        ?? throw new InvalidOperationException("Deserialize returned null");

    public static void WriteToFile(BenchmarkRun run, string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, Serialize(run));
    }

    public static string Serialize(SustainedRun run) => JsonSerializer.Serialize(run, Opts);
    public static void WriteToFile(SustainedRun run, string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, Serialize(run));
    }

    public static string DefaultPath(string? outDir = null, string suffix = "")
    {
        var dir = outDir ?? "results";
        var host = Environment.MachineName.ToLowerInvariant();
        var ts = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssZ");
        var name = string.IsNullOrEmpty(suffix) ? $"{ts}-{host}.json" : $"{ts}-{host}-{suffix}.json";
        return Path.Combine(dir, name);
    }
}
