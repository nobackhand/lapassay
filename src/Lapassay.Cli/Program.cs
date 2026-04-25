using System.Runtime.Versioning;
using System.Text.Json;
using Lapassay.Core;
using Lapassay.Core.Models;
using Lapassay.Core.Reporting;
using Lapassay.Core.Sustained;

[assembly: SupportedOSPlatform("windows")]

if (args.Length == 0 || args[0] is "--help" or "-h")
{
    PrintUsage();
    return 0;
}

if (args[0] is "--version")
{
    Console.WriteLine("lapassay 0.5.0");
    return 0;
}

if (args[0] == "run") return RunSingle(args[1..]);
if (args[0] == "sustained") return RunSustained(args[1..]);
if (args[0] == "report") return RenderReport(args[1..]);
if (args[0] == "compare") return CompareRuns(args[1..]);

Console.Error.WriteLine($"Unknown command: {args[0]}");
PrintUsage();
return 2;

int RunSingle(string[] cmdArgs)
{
    bool cpu = false, gpu = false;
    bool noHtml = false;
    string? outPath = null;
    int cpuN = 1024, gpuN = 2048;

    for (var i = 0; i < cmdArgs.Length; i++)
    {
        switch (cmdArgs[i])
        {
            case "--cpu": cpu = true; break;
            case "--gpu": gpu = true; break;
            case "--no-html": noHtml = true; break;
            case "--out" when i + 1 < cmdArgs.Length: outPath = cmdArgs[++i]; break;
            case "--cpu-n" when i + 1 < cmdArgs.Length: cpuN = int.Parse(cmdArgs[++i]); break;
            case "--gpu-n" when i + 1 < cmdArgs.Length: gpuN = int.Parse(cmdArgs[++i]); break;
            default:
                Console.Error.WriteLine($"Unknown option: {cmdArgs[i]}");
                return 2;
        }
    }
    if (!cpu && !gpu) { cpu = true; gpu = true; }

    PrintPreflight();
    outPath ??= JsonReport.DefaultPath();

    try
    {
        var run = Runner.Run(new Runner.RunOptions(cpu, gpu, cpuN, gpuN), Console.WriteLine);
        JsonReport.WriteToFile(run, outPath);
        Console.WriteLine();
        Console.WriteLine($"Wrote {outPath}");
        if (!noHtml)
        {
            var htmlPath = Path.ChangeExtension(outPath, ".html");
            HtmlReport.WriteToFile(run, htmlPath);
            Console.WriteLine($"Wrote {htmlPath}");
        }
        PrintSummary(run);
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"ERROR: {ex.Message}");
        Console.Error.WriteLine(ex.StackTrace);
        return 1;
    }
}

int RunSustained(string[] cmdArgs)
{
    double duration = 600;
    bool noHtml = false;
    string? outPath = null;
    for (var i = 0; i < cmdArgs.Length; i++)
    {
        switch (cmdArgs[i])
        {
            case "--duration" when i + 1 < cmdArgs.Length: duration = double.Parse(cmdArgs[++i]); break;
            case "--out" when i + 1 < cmdArgs.Length: outPath = cmdArgs[++i]; break;
            case "--no-html": noHtml = true; break;
            default:
                Console.Error.WriteLine($"Unknown option: {cmdArgs[i]}");
                return 2;
        }
    }

    PrintPreflight();
    outPath ??= JsonReport.DefaultPath(suffix: "sustained");

    try
    {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        var run = SustainedRunner.Run(
            new SustainedRunner.RunOptions(duration),
            onSample: null,
            log: Console.WriteLine,
            cancel: cts.Token);

        JsonReport.WriteToFile(run, outPath);
        Console.WriteLine();
        Console.WriteLine($"Wrote {outPath}");
        if (!noHtml)
        {
            var htmlPath = Path.ChangeExtension(outPath, ".html");
            HtmlReport.WriteToFile(run, htmlPath);
            Console.WriteLine($"Wrote {htmlPath}");
        }
        PrintSustainedSummary(run);
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"ERROR: {ex.Message}");
        Console.Error.WriteLine(ex.StackTrace);
        return 1;
    }
}

int CompareRuns(string[] cmdArgs)
{
    string? aPath = null, bPath = null, outPath = null;
    bool noHtml = false;
    for (var i = 0; i < cmdArgs.Length; i++)
    {
        switch (cmdArgs[i])
        {
            case "--out" when i + 1 < cmdArgs.Length: outPath = cmdArgs[++i]; break;
            case "--no-html": noHtml = true; break;
            default:
                if (cmdArgs[i].StartsWith("--")) { Console.Error.WriteLine($"Unknown option: {cmdArgs[i]}"); return 2; }
                if (aPath is null) aPath = cmdArgs[i];
                else if (bPath is null) bPath = cmdArgs[i];
                else { Console.Error.WriteLine($"Too many arguments: {cmdArgs[i]}"); return 2; }
                break;
        }
    }

    if (aPath is null || bPath is null)
    {
        Console.Error.WriteLine("Usage: lapassay compare <a.json> <b.json> [--out diff.html] [--no-html]");
        return 2;
    }
    if (!File.Exists(aPath)) { Console.Error.WriteLine($"File not found: {aPath}"); return 1; }
    if (!File.Exists(bPath)) { Console.Error.WriteLine($"File not found: {bPath}"); return 1; }

    var jsonOpts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    BenchmarkRun a, b;
    try
    {
        a = JsonSerializer.Deserialize<BenchmarkRun>(File.ReadAllText(aPath), jsonOpts)!;
        b = JsonSerializer.Deserialize<BenchmarkRun>(File.ReadAllText(bPath), jsonOpts)!;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Failed to parse run JSON (compare requires single-run files, not sustained): {ex.Message}");
        return 1;
    }

    var labelA = Path.GetFileNameWithoutExtension(aPath);
    var labelB = Path.GetFileNameWithoutExtension(bPath);
    var cmp = Compare.Diff(a, b, labelA, labelB);

    PrintCompareConsole(cmp);

    if (!noHtml)
    {
        outPath ??= Path.Combine(Path.GetDirectoryName(bPath) ?? ".", $"diff-{labelA}-vs-{labelB}.html");
        HtmlReport.WriteToFile(cmp, outPath);
        Console.WriteLine();
        Console.WriteLine($"Wrote {outPath}");
    }

    return 0;
}

static void PrintCompareConsole(RunComparison cmp)
{
    Console.WriteLine();
    Console.WriteLine($"Diff: {cmp.LabelA}  →  {cmp.LabelB}");
    Console.WriteLine();
    Console.WriteLine($"  {"Benchmark",-30} {"A",10} {"B",10} {"Δ%",9} {"Score Δ",9}");
    Console.WriteLine($"  {new string('-', 72)}");
    foreach (var d in cmp.PerBenchmark)
    {
        var pct = (d.DeltaPct >= 0 ? "+" : "") + d.DeltaPct.ToString("F1") + "%";
        Console.WriteLine($"  {d.Id,-30} {d.ValueA,10:F1} {d.ValueB,10:F1} {pct,9} {Sign(d.ScoreDelta),9}");
    }
    Console.WriteLine($"  {new string('-', 72)}");
    Console.WriteLine($"  {"Overall",-30} {cmp.RunA.Scores.Overall,10} {cmp.RunB.Scores.Overall,10} {"",9} {Sign(cmp.OverallScoreDelta),9}");
    if (cmp.RunA.Scores.Cpu > 0 || cmp.RunB.Scores.Cpu > 0)
        Console.WriteLine($"  {"CPU",-30} {cmp.RunA.Scores.Cpu,10} {cmp.RunB.Scores.Cpu,10} {"",9} {Sign(cmp.CpuScoreDelta),9}");
    if (cmp.RunA.Scores.Gpu > 0 || cmp.RunB.Scores.Gpu > 0)
        Console.WriteLine($"  {"GPU",-30} {cmp.RunA.Scores.Gpu,10} {cmp.RunB.Scores.Gpu,10} {"",9} {Sign(cmp.GpuScoreDelta),9}");

    static string Sign(int n) => n > 0 ? $"+{n}" : n.ToString();
}

int RenderReport(string[] cmdArgs)
{
    string? input = null;
    string? outPath = null;
    bool anonymize = false;
    for (var i = 0; i < cmdArgs.Length; i++)
    {
        switch (cmdArgs[i])
        {
            case "--out" when i + 1 < cmdArgs.Length: outPath = cmdArgs[++i]; break;
            case "--anonymize": anonymize = true; break;
            default:
                if (input is null && !cmdArgs[i].StartsWith("--")) input = cmdArgs[i];
                else { Console.Error.WriteLine($"Unknown option: {cmdArgs[i]}"); return 2; }
                break;
        }
    }
    if (input is null)
    {
        Console.Error.WriteLine("Usage: lapassay report <input.json> [--out path] [--anonymize]");
        return 2;
    }
    if (!File.Exists(input))
    {
        Console.Error.WriteLine($"File not found: {input}");
        return 1;
    }

    outPath ??= Path.ChangeExtension(input, ".html");
    var json = File.ReadAllText(input);
    using var doc = JsonDocument.Parse(json);
    var root = doc.RootElement;

    var jsonOpts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    if (root.TryGetProperty("verdict", out _))
    {
        var sustained = JsonSerializer.Deserialize<SustainedRun>(json, jsonOpts)
            ?? throw new InvalidOperationException("Failed to parse SustainedRun");
        HtmlReport.WriteToFile(sustained, outPath, anonymize);
    }
    else
    {
        var single = JsonSerializer.Deserialize<BenchmarkRun>(json, jsonOpts)
            ?? throw new InvalidOperationException("Failed to parse BenchmarkRun");
        HtmlReport.WriteToFile(single, outPath, anonymize);
    }

    Console.WriteLine($"Wrote {outPath}{(anonymize ? "  (anonymized)" : "")}");
    return 0;
}

static void PrintPreflight()
{
    var preflight = Preflight.Check();
    if (!preflight.Ok)
    {
        Console.WriteLine("Preflight warnings:");
        foreach (var m in preflight.Messages) Console.WriteLine("  " + m);
        Console.WriteLine();
    }
}

static void PrintUsage()
{
    Console.WriteLine("Lapassay 0.6.0 — Windows laptop CPU+GPU benchmark");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  lapassay run       [--cpu] [--gpu] [--out <path>] [--cpu-n N] [--gpu-n N] [--no-html]");
    Console.WriteLine("  lapassay sustained [--duration SEC] [--out <path>] [--no-html]");
    Console.WriteLine("  lapassay report    <input.json> [--out path] [--anonymize]");
    Console.WriteLine("  lapassay compare   <a.json> <b.json> [--out diff.html] [--no-html]");
    Console.WriteLine();
    Console.WriteLine("Notes:");
    Console.WriteLine("  * `run` and `sustained` write a JSON file and (by default) a self-contained HTML report next to it.");
    Console.WriteLine("  * Use `report --anonymize` to render an HTML stripped of hostname / CPU model string for sharing.");
    Console.WriteLine("  * `compare` diffs two single-shot runs (BIOS update, AC vs battery, before/after a tweak).");
    Console.WriteLine("  * Sustained run accepts Ctrl-C for early exit; partial JSON + HTML are still written.");
}

static void PrintSummary(BenchmarkRun run)
{
    Console.WriteLine();
    Console.WriteLine("╔══════════════════════════════════════════════╗");
    Console.WriteLine($"║  Overall score:  {run.Scores.Overall,-6}                      ║");
    if (run.Scores.Cpu > 0)
        Console.WriteLine($"║    CPU:  {run.Scores.Cpu,-6}                              ║");
    if (run.Scores.Gpu > 0)
        Console.WriteLine($"║    GPU:  {run.Scores.Gpu,-6}                              ║");
    Console.WriteLine("║  (baseline: mid-range 2024 laptop = 1000)    ║");
    Console.WriteLine("╚══════════════════════════════════════════════╝");
    if (run.Scores.Categories.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("  Subscores by category:");
        foreach (var c in run.Scores.Categories)
            Console.WriteLine($"    {c.Name,-15} {c.Score,4}   ({c.BenchmarkCount} kernels)");
    }
    Console.WriteLine();
    Console.WriteLine($"Host: {Environment.MachineName}");
    Console.WriteLine($"CPU:  {run.Environment.Cpu.Model} ({run.Environment.Cpu.PhysicalCores}c / {run.Environment.Cpu.LogicalCores}t)");
    foreach (var g in run.Environment.Gpu)
        Console.WriteLine($"GPU:  {g.Model}");
    Console.WriteLine($"RAM:  {run.Environment.Ram.TotalGb} GB @ {run.Environment.Ram.SpeedMhz} MHz");
    Console.WriteLine();
    Console.WriteLine($"  {"Benchmark",-30} {"Metric",-10} {"Value",10} {"Score",6} {"Stdev%",8}");
    Console.WriteLine($"  {new string('-', 72)}");
    foreach (var b in run.Benchmarks)
    {
        var stdevPct = b.Stats.Median != 0 ? b.Stats.Stdev / b.Stats.Median * 100 : 0;
        Console.WriteLine($"  {b.Id,-30} {b.Metric,-10} {b.Value,10:F1} {b.Score,6} {stdevPct,7:F2}%");
    }
}

static void PrintSustainedSummary(SustainedRun run)
{
    var v = run.Verdict;
    Console.WriteLine();
    Console.WriteLine("╔══════════════════════════════════════════════════════╗");
    Console.WriteLine(v.Throttled
        ? "║  ⚠ THROTTLE DETECTED                                 ║"
        : "║  ✓ No significant throttling                         ║");
    Console.WriteLine($"║  Duration: {run.DurationSec,5:F0}s   Iterations: {run.IterationCount,5}            ║");
    Console.WriteLine("╠══════════════════════════════════════════════════════╣");
    Console.WriteLine($"║  CPU:  first {v.FirstWindowCpuGflops,6:F1}  last {v.LastWindowCpuGflops,6:F1}  drop {v.CpuDropPct,5:F1}%  ║");
    Console.WriteLine($"║  GPU:  first {v.FirstWindowGpuGflops,6:F1}  last {v.LastWindowGpuGflops,6:F1}  drop {v.GpuDropPct,5:F1}%  ║");
    Console.WriteLine("╚══════════════════════════════════════════════════════╝");
}
