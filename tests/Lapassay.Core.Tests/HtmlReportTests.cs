using Lapassay.Core.Models;
using Lapassay.Core.Reporting;

namespace Lapassay.Core.Tests;

/// <summary>
/// Covers the self-contained HTML report. <see cref="HtmlReport.Generate(BenchmarkRun, bool)"/>
/// is pure string generation with no Windows dependencies, so these run on any platform
/// (matching <see cref="JsonReportTests"/>). They lock in the report's contract: the score
/// renders, environment strings are HTML-escaped (no markup injection from a CPU/host name),
/// anonymize actually redacts, category labels are the shared ones, and the markup is the
/// mobile-safe variant.
/// </summary>
public class HtmlReportTests
{
    static BenchmarkRun SampleRun(string cpuModel = "Test CPU", string runId = "2026-04-23T14:32:00Z-testhost-abcdef12") => new(
        SchemaVersion: "1.0",
        Tool: "lapassay",
        ToolVersion: "0.6.0",
        RunId: runId,
        Environment: new EnvironmentInfo(
            Cpu: new CpuInfo(cpuModel, 8, 16, 2400, 4800, 24),
            Gpu: new List<GpuInfo> { new("Test GPU", 8192, "1.2.3") },
            Ram: new RamInfo(32, 4800, 2),
            Os: new OsInfo("26200.1", "BIOS-1.0", "Balanced", false),
            CapturedAt: new DateTimeOffset(2026, 4, 23, 14, 32, 0, TimeSpan.Zero)),
        Scores: new Scores(900, 1100, 996, new List<CategoryScore>
        {
            new("cpu.integer", 850, 3),
            new("gpu.compute", 1200, 2),
        }),
        Benchmarks: new List<BenchmarkResult>
        {
            new("cpu.aes128cbc", "cpu", "mb/s", 3120.0, 1040,
                new BenchmarkStats(15, 3120.0, 12.0, 3100.0, 3140.0), 1.2,
                new TelemetrySummary(38.2, 42.5, null, null, 78, null, 3200, 4200)),
        });

    [Fact]
    public void RendersOverallScoreAndIsAFullHtmlDocument()
    {
        var html = HtmlReport.Generate(SampleRun());

        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.Contains("<meta name=\"viewport\"", html);
        Assert.Contains("996", html);   // overall score
        Assert.Contains("</html>", html);
    }

    [Fact]
    public void UsesSharedCategoryLabels()
    {
        var html = HtmlReport.Generate(SampleRun());

        // "cpu.integer" should surface as the shared human label, never the raw id.
        Assert.Contains("CPU integer", html);
        Assert.Contains("GPU compute", html);
        Assert.DoesNotContain("cpu.integer", html);
    }

    [Fact]
    public void EscapesEnvironmentStringsToPreventMarkupInjection()
    {
        var html = HtmlReport.Generate(SampleRun(cpuModel: "<script>alert('x')</script>"));

        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void AnonymizeRedactsTheCpuModel()
    {
        var html = HtmlReport.Generate(SampleRun(cpuModel: "Ryzen 7 7840U"), anonymize: true);

        Assert.DoesNotContain("Ryzen 7 7840U", html);
        Assert.Contains("laptop CPU", html);
    }

    [Fact]
    public void TablesAreWrappedForHorizontalScrollOnNarrowScreens()
    {
        var html = HtmlReport.Generate(SampleRun());

        Assert.Contains("table-wrap", html);
        Assert.Contains("@media (max-width: 560px)", html);
    }

    [Fact]
    public void OffersAnOptInDarkScheme()
    {
        var html = HtmlReport.Generate(SampleRun());

        Assert.Contains("<meta name=\"color-scheme\" content=\"light dark\">", html);
        Assert.Contains("@media (prefers-color-scheme: dark)", html);
    }

    [Fact]
    public void TitleKeepsHyphenatedHostnamesIntact()
    {
        // Windows hostnames can contain '-'. The title is derived from the runId; the
        // hostname must not be truncated at its first dash.
        var html = HtmlReport.Generate(SampleRun(runId: "2026-04-23T14:32:00Z-my-laptop-abcdef12"));

        Assert.Contains("my-laptop</title>", html);
    }

    [Fact]
    public void ShowsMedianAndIqrWhenRepeatsArePresent()
    {
        var run = SampleRun() with
        {
            Benchmarks = new List<BenchmarkResult>
            {
                new("cpu.aes128cbc", "cpu", "mb/s", 3120.0, 1040,
                    new BenchmarkStats(15, 3120.0, 12.0, 3100.0, 3140.0), 1.2,
                    new TelemetrySummary(null, null, null, null, null, null, null, null),
                    new Repeats(new[] { 3000.0, 3120.0, 3200.0 }, 3120.0, 3050.0, 3180.0)),
            },
        };

        var html = HtmlReport.Generate(run);

        Assert.Contains("Median of 3 runs", html);
        Assert.Contains("3050.0&ndash;3180.0", html);
    }
}
