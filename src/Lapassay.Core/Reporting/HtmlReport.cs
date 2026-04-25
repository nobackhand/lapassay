using System.Globalization;
using System.Text;
using System.Web;
using Lapassay.Core.Models;

namespace Lapassay.Core.Reporting;

/// <summary>
/// Generates a self-contained HTML report from a <see cref="BenchmarkRun"/> or
/// <see cref="SustainedRun"/>. All CSS is inlined; charts are inline SVG. The
/// resulting file opens in any browser without internet access and is safe to
/// archive or share. Set <c>anonymize: true</c> to redact hostname and the
/// specific CPU/BIOS strings before generating.
/// </summary>
public static class HtmlReport
{
    public static string Generate(BenchmarkRun run, bool anonymize = false)
    {
        var sections = new List<string>
        {
            EnvironmentSection(run.Environment, anonymize),
            BenchmarksSection(run.Benchmarks),
        };
        if (run.ScalingCurve is { Count: > 0 })
            sections.Add(ScalingCurveSection(run.ScalingCurve));

        return BuildPage(
            title: anonymize ? "Lapassay benchmark report" : $"Lapassay — {Hostname(run.RunId)}",
            heroHtml: HeroSingle(run),
            sectionsHtml: sections,
            footerHtml: Footer(run.RunId, run.ToolVersion, anonymize));
    }

    public static string Generate(SustainedRun run, bool anonymize = false) =>
        BuildPage(
            title: anonymize ? "Lapassay sustained run" : $"Lapassay sustained — {Hostname(run.RunId)}",
            heroHtml: HeroSustained(run),
            sectionsHtml: new[]
            {
                EnvironmentSection(run.Environment, anonymize),
                SustainedChartSection(run),
            },
            footerHtml: Footer(run.RunId, run.ToolVersion, anonymize));

    public static string Generate(RunComparison cmp, bool anonymize = false) =>
        BuildPage(
            title: $"Lapassay diff — {cmp.LabelA} vs {cmp.LabelB}",
            heroHtml: HeroDiff(cmp),
            sectionsHtml: new[]
            {
                DiffTableSection(cmp),
            },
            footerHtml: $"<footer>Lapassay diff &middot; {Esc(cmp.LabelA)} vs {Esc(cmp.LabelB)}{(anonymize ? " &middot; anonymized" : "")}</footer>");

    public static void WriteToFile(BenchmarkRun run, string path, bool anonymize = false)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, Generate(run, anonymize));
    }

    public static void WriteToFile(SustainedRun run, string path, bool anonymize = false)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, Generate(run, anonymize));
    }

    public static void WriteToFile(RunComparison cmp, string path, bool anonymize = false)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, Generate(cmp, anonymize));
    }

    // -------------------- page skeleton --------------------

    static string BuildPage(string title, string heroHtml, IEnumerable<string> sectionsHtml, string footerHtml)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\"><head>");
        sb.AppendLine("<meta charset=\"utf-8\">");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.AppendLine($"<title>{Esc(title)}</title>");
        sb.AppendLine("<style>");
        sb.AppendLine(Css);
        sb.AppendLine("</style>");
        sb.AppendLine("</head><body>");
        sb.AppendLine("<main>");
        sb.AppendLine(heroHtml);
        foreach (var s in sectionsHtml) sb.AppendLine(s);
        sb.AppendLine(footerHtml);
        sb.AppendLine("</main>");
        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    const string Css = @"
:root {
    --fg: #1f2328;
    --muted: #59636e;
    --bg: #ffffff;
    --soft: #f6f8fa;
    --border: #d0d7de;
    --blue: #0969DA;
    --green: #1A7F37;
    --red: #CF222E;
    --amber: #9A6700;
}
* { box-sizing: border-box; }
html, body { margin: 0; padding: 0; background: var(--soft); color: var(--fg); font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, system-ui, sans-serif; -webkit-font-smoothing: antialiased; }
main { max-width: 880px; margin: 32px auto; padding: 0 20px 60px; }

.hero { background: var(--blue); color: white; border-radius: 12px; padding: 28px 32px; margin-bottom: 24px; }
.hero.sustained { background: var(--red); }
.hero h1 { margin: 0 0 4px; font-size: 14px; font-weight: 500; opacity: .85; letter-spacing: .04em; text-transform: uppercase; }
.hero .score { font-size: 64px; font-weight: 700; line-height: 1; margin: 4px 0 6px; }
.hero .score-label { font-size: 12px; opacity: .85; letter-spacing: .04em; text-transform: uppercase; }
.hero .subs { display: flex; gap: 28px; margin-top: 14px; }
.hero .sub { }
.hero .sub-value { font-size: 22px; font-weight: 600; }
.hero .sub-label { font-size: 11px; opacity: .85; text-transform: uppercase; letter-spacing: .04em; }
.hero .baseline { font-size: 11px; opacity: .8; margin-top: 12px; }
.category-chips { display: flex; flex-wrap: wrap; gap: 8px; margin-top: 14px; }
.chip { display: inline-flex; align-items: baseline; gap: 8px; background: rgba(255,255,255,0.18); border-radius: 999px; padding: 5px 12px; font-size: 12px; }
.chip-label { opacity: .85; text-transform: uppercase; letter-spacing: .04em; font-size: 10px; }
.chip-score { font-weight: 700; font-size: 14px; }
.hero .verdict-headline { font-size: 22px; font-weight: 600; margin: 4px 0 10px; }
.hero .verdict-detail { font-size: 13px; opacity: .9; line-height: 1.5; }

section { background: var(--bg); border: 1px solid var(--border); border-radius: 8px; padding: 20px 24px; margin-bottom: 16px; }
section h2 { margin: 0 0 12px; font-size: 13px; font-weight: 600; letter-spacing: .04em; text-transform: uppercase; color: var(--muted); }

.env-grid { display: grid; grid-template-columns: max-content 1fr; gap: 6px 24px; font-size: 14px; }
.env-grid dt { color: var(--muted); }
.env-grid dd { margin: 0; font-family: ui-monospace, 'Cascadia Mono', Consolas, monospace; font-size: 13px; }

table.bench { width: 100%; border-collapse: collapse; font-size: 14px; }
table.bench th { text-align: left; padding: 8px 8px 10px; font-size: 11px; font-weight: 600; color: var(--muted); text-transform: uppercase; letter-spacing: .04em; border-bottom: 1px solid var(--border); }
table.bench th.num { text-align: right; }
table.bench td { padding: 10px 8px; border-bottom: 1px solid var(--soft); }
table.bench td.num { text-align: right; font-variant-numeric: tabular-nums; font-family: ui-monospace, 'Cascadia Mono', Consolas, monospace; font-size: 13px; }
table.bench td.id { font-family: ui-monospace, 'Cascadia Mono', Consolas, monospace; font-size: 13px; }
table.bench tr:last-child td { border-bottom: none; }

.score-cell { display: flex; align-items: center; justify-content: flex-end; gap: 10px; }
.score-bar { flex: 0 0 80px; height: 6px; background: var(--soft); border-radius: 3px; overflow: hidden; }
.score-bar .fill { height: 100%; background: var(--blue); }
.score-bar.gpu .fill { background: var(--green); }
.score-num { min-width: 42px; text-align: right; font-weight: 600; }

.delta-pos { color: var(--green); font-weight: 600; }
.delta-neg { color: var(--red); font-weight: 600; }
.delta-flat { color: var(--muted); }
.diff-bar { display: inline-block; flex: 0 0 100px; height: 6px; background: var(--soft); border-radius: 3px; position: relative; overflow: hidden; vertical-align: middle; }
.diff-bar .center { position: absolute; left: 50%; top: 0; bottom: 0; width: 1px; background: var(--border); }
.diff-bar .fill { position: absolute; top: 0; bottom: 0; }
.diff-bar .fill.pos { background: var(--green); left: 50%; }
.diff-bar .fill.neg { background: var(--red); right: 50%; }
.hero.diff { background: linear-gradient(135deg, #0969DA, #1A7F37); }
.hero .arrow { font-size: 24px; opacity: .7; margin: 0 8px; }

.chart-wrap { padding: 8px 0 0; }
.chart-wrap svg { width: 100%; height: auto; display: block; }
.legend { display: flex; gap: 20px; font-size: 12px; color: var(--muted); margin-top: 8px; flex-wrap: wrap; }
.legend .swatch { display: inline-block; width: 14px; height: 2px; vertical-align: middle; margin-right: 6px; }
.legend .swatch.dash { border-bottom: 2px dashed currentColor; height: 0; }

footer { text-align: center; font-size: 11px; color: var(--muted); margin-top: 24px; padding: 8px; }
footer a { color: var(--muted); }
";

    // -------------------- hero blocks --------------------

    static string HeroSingle(BenchmarkRun run)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<div class=\"hero\">");
        sb.AppendLine("  <h1>Overall score</h1>");
        sb.AppendLine($"  <div class=\"score\">{run.Scores.Overall}</div>");
        sb.AppendLine("  <div class=\"subs\">");
        if (run.Scores.Cpu > 0)
        {
            sb.AppendLine("    <div class=\"sub\">");
            sb.AppendLine("      <div class=\"sub-label\">CPU</div>");
            sb.AppendLine($"      <div class=\"sub-value\">{run.Scores.Cpu}</div>");
            sb.AppendLine("    </div>");
        }
        if (run.Scores.Gpu > 0)
        {
            sb.AppendLine("    <div class=\"sub\">");
            sb.AppendLine("      <div class=\"sub-label\">GPU</div>");
            sb.AppendLine($"      <div class=\"sub-value\">{run.Scores.Gpu}</div>");
            sb.AppendLine("    </div>");
        }
        sb.AppendLine("  </div>");
        if (run.Scores.Categories.Count > 0)
        {
            sb.AppendLine("  <div class=\"category-chips\">");
            foreach (var c in run.Scores.Categories)
            {
                sb.AppendLine($"    <span class=\"chip\"><span class=\"chip-label\">{Esc(PrettyCategory(c.Name))}</span><span class=\"chip-score\">{c.Score}</span></span>");
            }
            sb.AppendLine("  </div>");
        }
        sb.AppendLine("  <div class=\"baseline\">Baseline: mid-range 2024 laptop = 1000.</div>");
        sb.AppendLine("</div>");
        return sb.ToString();
    }

    static string PrettyCategory(string raw) => raw switch
    {
        "cpu.integer"  => "CPU integer",
        "cpu.float"    => "CPU float",
        "cpu.memory"   => "Memory",
        "cpu.parallel" => "CPU parallel",
        "gpu.compute"  => "GPU compute",
        "gpu.ai"       => "GPU AI",
        _              => raw,
    };

    static string HeroDiff(RunComparison cmp)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<div class=\"hero diff\">");
        sb.AppendLine($"  <h1>{Esc(cmp.LabelA)} → {Esc(cmp.LabelB)}</h1>");
        sb.AppendLine("  <div class=\"subs\" style=\"margin-top: 6px; align-items: center;\">");
        sb.AppendLine("    <div class=\"sub\">");
        sb.AppendLine("      <div class=\"sub-label\">A</div>");
        sb.AppendLine($"      <div class=\"sub-value\">{cmp.RunA.Scores.Overall}</div>");
        sb.AppendLine("    </div>");
        sb.AppendLine("    <div class=\"arrow\">→</div>");
        sb.AppendLine("    <div class=\"sub\">");
        sb.AppendLine("      <div class=\"sub-label\">B</div>");
        sb.AppendLine($"      <div class=\"sub-value\">{cmp.RunB.Scores.Overall}</div>");
        sb.AppendLine("    </div>");
        sb.AppendLine("    <div class=\"arrow\" style=\"opacity:1\">=</div>");
        sb.AppendLine("    <div class=\"sub\">");
        sb.AppendLine("      <div class=\"sub-label\">Δ Overall</div>");
        sb.AppendLine($"      <div class=\"sub-value\">{Sign(cmp.OverallScoreDelta)}</div>");
        sb.AppendLine("    </div>");
        sb.AppendLine("  </div>");
        sb.AppendLine("  <div class=\"subs\" style=\"margin-top: 12px;\">");
        if (cmp.RunA.Scores.Cpu > 0 || cmp.RunB.Scores.Cpu > 0)
        {
            sb.AppendLine("    <div class=\"sub\">");
            sb.AppendLine("      <div class=\"sub-label\">CPU Δ</div>");
            sb.AppendLine($"      <div class=\"sub-value\">{cmp.RunA.Scores.Cpu} → {cmp.RunB.Scores.Cpu} ({Sign(cmp.CpuScoreDelta)})</div>");
            sb.AppendLine("    </div>");
        }
        if (cmp.RunA.Scores.Gpu > 0 || cmp.RunB.Scores.Gpu > 0)
        {
            sb.AppendLine("    <div class=\"sub\">");
            sb.AppendLine("      <div class=\"sub-label\">GPU Δ</div>");
            sb.AppendLine($"      <div class=\"sub-value\">{cmp.RunA.Scores.Gpu} → {cmp.RunB.Scores.Gpu} ({Sign(cmp.GpuScoreDelta)})</div>");
            sb.AppendLine("    </div>");
        }
        sb.AppendLine("  </div>");
        sb.AppendLine("</div>");
        return sb.ToString();
    }

    static string DiffTableSection(RunComparison cmp)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<section>");
        sb.AppendLine("  <h2>Per-benchmark delta</h2>");
        sb.AppendLine("  <table class=\"bench\">");
        sb.AppendLine("    <thead><tr>");
        sb.AppendLine("      <th>ID</th>");
        sb.AppendLine($"      <th class=\"num\">{Esc(cmp.LabelA)}</th>");
        sb.AppendLine($"      <th class=\"num\">{Esc(cmp.LabelB)}</th>");
        sb.AppendLine("      <th class=\"num\">Δ%</th>");
        sb.AppendLine("      <th class=\"num\">Score Δ</th>");
        sb.AppendLine("      <th class=\"num\" style=\"width:140px\">Change</th>");
        sb.AppendLine("    </tr></thead><tbody>");

        foreach (var d in cmp.PerBenchmark)
        {
            var dir = d.Direction;
            var deltaCls = dir == 0 ? "delta-flat" : (dir > 0 ? "delta-pos" : "delta-neg");
            var deltaSign = d.DeltaPct >= 0 ? "+" : "";
            // For lower-is-better, flip the sign on the bar so 'better' is always green (right side).
            var fillPct = d.HigherIsBetter ? d.DeltaPct : -d.DeltaPct;
            var clamped = Math.Max(-50.0, Math.Min(50.0, fillPct));
            var halfBar = Math.Abs(clamped); // 0..50 → 0..50% of half
            var fillCls = clamped >= 0 ? "pos" : "neg";

            sb.AppendLine("    <tr>");
            sb.AppendLine($"      <td class=\"id\">{Esc(d.Id)}</td>");
            sb.AppendLine($"      <td class=\"num\">{d.ValueA.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)} <span style=\"opacity:.5\">{Esc(d.Metric)}</span></td>");
            sb.AppendLine($"      <td class=\"num\">{d.ValueB.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}</td>");
            sb.AppendLine($"      <td class=\"num {deltaCls}\">{deltaSign}{d.DeltaPct.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}%</td>");
            sb.AppendLine($"      <td class=\"num {deltaCls}\">{Sign(d.ScoreDelta)}</td>");
            sb.AppendLine("      <td class=\"num\">");
            sb.AppendLine("        <span class=\"diff-bar\">");
            sb.AppendLine("          <span class=\"center\"></span>");
            sb.AppendLine($"          <span class=\"fill {fillCls}\" style=\"width: {halfBar.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}%\"></span>");
            sb.AppendLine("        </span>");
            sb.AppendLine("      </td>");
            sb.AppendLine("    </tr>");
        }

        sb.AppendLine("    </tbody></table>");
        sb.AppendLine("  <p style=\"font-size:11px;color:#59636e;margin-top:10px\">Bars saturate at &plusmn;50%. Green = B improved over A; red = regressed.</p>");
        sb.AppendLine("</section>");
        return sb.ToString();
    }

    static string Sign(int n) => n > 0 ? $"+{n}" : n.ToString();

    static string HeroSustained(SustainedRun run)
    {
        var v = run.Verdict;
        var headline = v.Throttled ? "Throttle detected" : "No significant throttling";
        var sb = new StringBuilder();
        sb.AppendLine($"<div class=\"hero sustained\">");
        sb.AppendLine("  <h1>Sustained throttle test</h1>");
        sb.AppendLine($"  <div class=\"verdict-headline\">{Esc(headline)}</div>");
        sb.AppendLine("  <div class=\"verdict-detail\">");
        sb.AppendLine($"    Duration {run.DurationSec:F0} s &middot; {run.IterationCount} iterations<br>");
        sb.AppendLine($"    CPU: first {v.FirstWindowCpuGflops:F1} → last {v.LastWindowCpuGflops:F1} GFLOPS &nbsp;({v.CpuDropPct:+0.0;-0.0}% drop)<br>");
        sb.AppendLine($"    GPU: first {v.FirstWindowGpuGflops:F1} → last {v.LastWindowGpuGflops:F1} GFLOPS &nbsp;({v.GpuDropPct:+0.0;-0.0}% drop)");
        sb.AppendLine("  </div>");
        sb.AppendLine("</div>");
        return sb.ToString();
    }

    // -------------------- sections --------------------

    static string EnvironmentSection(EnvironmentInfo env, bool anonymize)
    {
        var (cpuModel, gpuLines, ramLine, osLine, biosLine) = anonymize switch
        {
            true => (
                $"{env.Cpu.PhysicalCores}c / {env.Cpu.LogicalCores}t laptop CPU",
                env.Gpu.Select(g => GenericGpu(g.Model)).ToArray(),
                $"{env.Ram.TotalGb} GB",
                $"Windows ({env.Os.PowerPlan} plan, {(env.Os.OnBattery ? "battery" : "AC")})",
                "(redacted)"),
            false => (
                env.Cpu.Model,
                env.Gpu.Select(g => $"{g.Model} ({g.VramMb} MB, driver {g.Driver})").ToArray(),
                $"{env.Ram.TotalGb} GB @ {env.Ram.SpeedMhz} MHz, {env.Ram.Channels} channel",
                $"Windows {env.Os.WindowsBuild} ({env.Os.PowerPlan} plan, {(env.Os.OnBattery ? "battery" : "AC")})",
                env.Os.Bios)
        };

        var sb = new StringBuilder();
        sb.AppendLine("<section>");
        sb.AppendLine("  <h2>System</h2>");
        sb.AppendLine("  <dl class=\"env-grid\">");
        sb.AppendLine($"    <dt>CPU</dt><dd>{Esc(cpuModel)} &middot; base {env.Cpu.MaxTurboMhz} MHz &middot; L3 {env.Cpu.L3CacheMb} MB</dd>");
        foreach (var g in gpuLines)
            sb.AppendLine($"    <dt>GPU</dt><dd>{Esc(g)}</dd>");
        sb.AppendLine($"    <dt>RAM</dt><dd>{Esc(ramLine)}</dd>");
        sb.AppendLine($"    <dt>OS</dt><dd>{Esc(osLine)}</dd>");
        sb.AppendLine($"    <dt>BIOS</dt><dd>{Esc(biosLine)}</dd>");
        sb.AppendLine($"    <dt>Captured</dt><dd>{env.CapturedAt:u}</dd>");
        sb.AppendLine("  </dl>");
        sb.AppendLine("</section>");
        return sb.ToString();
    }

    static string GenericGpu(string raw)
    {
        var lower = raw.ToLowerInvariant();
        if (lower.Contains("nvidia") || lower.Contains("geforce") || lower.Contains("rtx") || lower.Contains("gtx"))
            return "NVIDIA GPU";
        if (lower.Contains("amd") || lower.Contains("radeon"))
            return raw.Contains("integrated", StringComparison.OrdinalIgnoreCase) || lower.Contains("graphics") ? "AMD integrated GPU" : "AMD GPU";
        if (lower.Contains("intel"))
            return "Intel GPU";
        return "GPU";
    }

    static string BenchmarksSection(IEnumerable<BenchmarkResult> benches)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<section>");
        sb.AppendLine("  <h2>Benchmarks</h2>");
        sb.AppendLine("  <table class=\"bench\">");
        sb.AppendLine("    <thead><tr>");
        sb.AppendLine("      <th>ID</th>");
        sb.AppendLine("      <th class=\"num\">Value</th>");
        sb.AppendLine("      <th>Metric</th>");
        sb.AppendLine("      <th class=\"num\">Stdev</th>");
        sb.AppendLine("      <th class=\"num\">Score</th>");
        sb.AppendLine("    </tr></thead><tbody>");
        foreach (var b in benches)
        {
            var stdevPct = b.Stats.Median != 0 ? b.Stats.Stdev / b.Stats.Median * 100 : 0;
            var fillPct = Math.Min(100, Math.Max(0, b.Score / 15.0)); // 1500 score = full bar
            var barClass = b.Kind == "gpu" ? "score-bar gpu" : "score-bar";
            sb.AppendLine("    <tr>");
            sb.AppendLine($"      <td class=\"id\">{Esc(b.Id)}</td>");
            sb.AppendLine($"      <td class=\"num\">{b.Value.ToString("F1", CultureInfo.InvariantCulture)}</td>");
            sb.AppendLine($"      <td>{Esc(b.Metric)}</td>");
            sb.AppendLine($"      <td class=\"num\">{stdevPct.ToString("F1", CultureInfo.InvariantCulture)}%</td>");
            sb.AppendLine("      <td class=\"num\">");
            sb.AppendLine("        <span class=\"score-cell\">");
            sb.AppendLine($"          <span class=\"{barClass}\"><span class=\"fill\" style=\"width: {fillPct.ToString("F1", CultureInfo.InvariantCulture)}%\"></span></span>");
            sb.AppendLine($"          <span class=\"score-num\">{b.Score}</span>");
            sb.AppendLine("        </span>");
            sb.AppendLine("      </td>");
            sb.AppendLine("    </tr>");
        }
        sb.AppendLine("    </tbody>");
        sb.AppendLine("  </table>");
        sb.AppendLine("</section>");
        return sb.ToString();
    }

    static string ScalingCurveSection(IReadOnlyList<ScalingPoint> curve)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<section>");
        sb.AppendLine("  <h2>CPU per-core scaling</h2>");
        sb.AppendLine("  <div class=\"chart-wrap\">");
        sb.AppendLine(BuildScalingSvg(curve));
        sb.AppendLine("  </div>");
        sb.AppendLine("  <div class=\"legend\">");
        sb.AppendLine("    <span><span class=\"swatch\" style=\"background:#0969DA\"></span>Measured GFLOPS</span>");
        sb.AppendLine("    <span style=\"color:#59636e\"><span class=\"swatch dash\"></span>Ideal linear scaling</span>");
        sb.AppendLine("  </div>");
        sb.AppendLine("  <table class=\"bench\" style=\"margin-top:10px\">");
        sb.AppendLine("    <thead><tr><th>Threads</th><th class=\"num\">GFLOPS</th><th class=\"num\">Efficiency</th></tr></thead><tbody>");
        foreach (var p in curve)
        {
            sb.AppendLine("    <tr>");
            sb.AppendLine($"      <td class=\"num\">{p.Threads}</td>");
            sb.AppendLine($"      <td class=\"num\">{p.Gflops.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}</td>");
            sb.AppendLine($"      <td class=\"num\">{p.EfficiencyPct.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}%</td>");
            sb.AppendLine("    </tr>");
        }
        sb.AppendLine("    </tbody></table>");
        sb.AppendLine("</section>");
        return sb.ToString();
    }

    static string BuildScalingSvg(IReadOnlyList<ScalingPoint> curve)
    {
        const int W = 820, H = 240, padL = 44, padR = 14, padT = 12, padB = 28;
        var plotW = W - padL - padR;
        var plotH = H - padT - padB;

        if (curve.Count < 2)
            return $"<svg viewBox=\"0 0 {W} {H}\" xmlns=\"http://www.w3.org/2000/svg\"><text x=\"{W / 2}\" y=\"{H / 2}\" text-anchor=\"middle\" fill=\"#59636e\" font-size=\"12\">Not enough data points</text></svg>";

        var maxThreads = 0;
        var maxGflops = 0.0;
        foreach (var p in curve)
        {
            if (p.Threads > maxThreads) maxThreads = p.Threads;
            if (p.Gflops > maxGflops) maxGflops = p.Gflops;
        }
        // Ideal at maxThreads = single * maxThreads — usually higher than measured. Use whichever is bigger.
        var single = curve[0].Gflops;
        var idealMax = single * maxThreads;
        var yMax = Math.Ceiling(Math.Max(maxGflops, idealMax) / 10.0) * 10.0;
        if (yMax <= 0) yMax = 10;

        double X(int t) => padL + plotW * (t - 1) / Math.Max(1, maxThreads - 1);
        double Y(double g) => padT + plotH - plotH * (g / yMax);

        var sb = new StringBuilder();
        sb.AppendLine($"<svg viewBox=\"0 0 {W} {H}\" xmlns=\"http://www.w3.org/2000/svg\" preserveAspectRatio=\"none\">");

        // Gridlines
        for (var i = 1; i < 4; i++)
        {
            var y = padT + plotH * i / 4.0;
            sb.AppendLine($"  <line x1=\"{padL}\" y1=\"{y.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}\" x2=\"{padL + plotW}\" y2=\"{y.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}\" stroke=\"#eaeef2\" stroke-width=\"1\"/>");
        }
        // Axes
        sb.AppendLine($"  <line x1=\"{padL}\" y1=\"{padT}\" x2=\"{padL}\" y2=\"{padT + plotH}\" stroke=\"#d0d7de\"/>");
        sb.AppendLine($"  <line x1=\"{padL}\" y1=\"{padT + plotH}\" x2=\"{padL + plotW}\" y2=\"{padT + plotH}\" stroke=\"#d0d7de\"/>");

        // Y labels
        for (var i = 0; i <= 4; i++)
        {
            var y = padT + plotH * i / 4.0;
            var v = yMax * (4 - i) / 4.0;
            sb.AppendLine($"  <text x=\"{padL - 6}\" y=\"{(y + 4).ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}\" text-anchor=\"end\" font-size=\"10\" fill=\"#59636e\">{v.ToString("F0", System.Globalization.CultureInfo.InvariantCulture)}</text>");
        }
        // X labels (thread counts)
        foreach (var p in curve)
        {
            var x = X(p.Threads);
            sb.AppendLine($"  <text x=\"{x.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}\" y=\"{padT + plotH + 16}\" text-anchor=\"middle\" font-size=\"10\" fill=\"#59636e\">{p.Threads}t</text>");
        }
        sb.AppendLine($"  <text x=\"{padL + plotW / 2}\" y=\"{H - 4}\" text-anchor=\"middle\" font-size=\"10\" fill=\"#59636e\">threads</text>");
        sb.AppendLine($"  <text x=\"8\" y=\"{padT + plotH / 2}\" text-anchor=\"middle\" font-size=\"10\" fill=\"#59636e\" transform=\"rotate(-90 8 {padT + plotH / 2})\">GFLOPS</text>");

        // Ideal line (dashed gray)
        var idealPath = $"M{X(1).ToString("F1", System.Globalization.CultureInfo.InvariantCulture)} {Y(single).ToString("F1", System.Globalization.CultureInfo.InvariantCulture)} L{X(maxThreads).ToString("F1", System.Globalization.CultureInfo.InvariantCulture)} {Y(idealMax).ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}";
        sb.AppendLine($"  <path d=\"{idealPath}\" stroke=\"#59636e\" stroke-width=\"1.5\" stroke-dasharray=\"4 3\" fill=\"none\"/>");

        // Measured line
        var measuredPath = new StringBuilder();
        for (var i = 0; i < curve.Count; i++)
        {
            measuredPath.Append(i == 0 ? "M" : "L");
            measuredPath.Append(X(curve[i].Threads).ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
            measuredPath.Append(' ');
            measuredPath.Append(Y(curve[i].Gflops).ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
            measuredPath.Append(' ');
        }
        sb.AppendLine($"  <path d=\"{measuredPath}\" stroke=\"#0969DA\" stroke-width=\"2\" fill=\"none\"/>");

        // Dots on measured points
        foreach (var p in curve)
        {
            sb.AppendLine($"  <circle cx=\"{X(p.Threads).ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}\" cy=\"{Y(p.Gflops).ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}\" r=\"3\" fill=\"#0969DA\"/>");
        }

        sb.AppendLine("</svg>");
        return sb.ToString();
    }

    static string SustainedChartSection(SustainedRun run)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<section>");
        sb.AppendLine("  <h2>Throughput &amp; thermals over time</h2>");
        sb.AppendLine("  <div class=\"chart-wrap\">");
        sb.AppendLine(BuildSvgChart(run.Samples, run.DurationSec));
        sb.AppendLine("  </div>");
        sb.AppendLine("  <div class=\"legend\">");
        sb.AppendLine("    <span><span class=\"swatch\" style=\"background:#0969DA\"></span>CPU GFLOPS</span>");
        sb.AppendLine("    <span><span class=\"swatch\" style=\"background:#1A7F37\"></span>GPU GFLOPS</span>");
        sb.AppendLine("    <span style=\"color:#CF222E\"><span class=\"swatch dash\"></span>CPU °C (right axis)</span>");
        sb.AppendLine("    <span style=\"color:#9A6700\"><span class=\"swatch dash\"></span>GPU °C (right axis)</span>");
        sb.AppendLine("  </div>");
        sb.AppendLine("</section>");
        return sb.ToString();
    }

    static string BuildSvgChart(IReadOnlyList<SustainedSample> samples, double durationSec)
    {
        const int W = 820, H = 280, padL = 40, padR = 40, padT = 12, padB = 24;
        var plotW = W - padL - padR;
        var plotH = H - padT - padB;

        if (samples.Count < 2)
            return $"<svg viewBox=\"0 0 {W} {H}\" xmlns=\"http://www.w3.org/2000/svg\"><text x=\"{W / 2}\" y=\"{H / 2}\" text-anchor=\"middle\" fill=\"#59636e\" font-size=\"12\">No data</text></svg>";

        var maxT = Math.Max(durationSec, samples[^1].ElapsedSec);
        var maxGflops = 1.0;
        var maxTemp = 50.0;
        foreach (var s in samples)
        {
            if (s.CpuGflops > maxGflops) maxGflops = s.CpuGflops;
            if (s.GpuGflops > maxGflops) maxGflops = s.GpuGflops;
            if (s.CpuTempC.HasValue && s.CpuTempC.Value > maxTemp) maxTemp = s.CpuTempC.Value;
            if (s.GpuTempC.HasValue && s.GpuTempC.Value > maxTemp) maxTemp = s.GpuTempC.Value;
        }
        maxGflops = Math.Ceiling(maxGflops / 10.0) * 10.0;
        maxTemp = Math.Ceiling(maxTemp / 10.0) * 10.0;

        double X(double t) => padL + plotW * (t / Math.Max(maxT, 1));
        double Yg(double g) => padT + plotH - plotH * (g / maxGflops);
        double Yt(double t) => padT + plotH - plotH * (t / maxTemp);

        var cpuPath = BuildPath(samples, s => (X(s.ElapsedSec), Yg(s.CpuGflops)));
        var gpuPath = BuildPath(samples, s => (X(s.ElapsedSec), Yg(s.GpuGflops)));
        var cpuTPath = BuildPath(samples.Where(s => s.CpuTempC.HasValue), s => (X(s.ElapsedSec), Yt(s.CpuTempC!.Value)));
        var gpuTPath = BuildPath(samples.Where(s => s.GpuTempC.HasValue), s => (X(s.ElapsedSec), Yt(s.GpuTempC!.Value)));

        var sb = new StringBuilder();
        sb.AppendLine($"<svg viewBox=\"0 0 {W} {H}\" xmlns=\"http://www.w3.org/2000/svg\" preserveAspectRatio=\"none\">");

        // Grid
        for (var i = 1; i < 4; i++)
        {
            var y = padT + plotH * i / 4.0;
            sb.AppendLine($"  <line x1=\"{padL}\" y1=\"{y.ToString("F1", CultureInfo.InvariantCulture)}\" x2=\"{padL + plotW}\" y2=\"{y.ToString("F1", CultureInfo.InvariantCulture)}\" stroke=\"#eaeef2\" stroke-width=\"1\"/>");
        }
        // Axes
        sb.AppendLine($"  <line x1=\"{padL}\" y1=\"{padT}\" x2=\"{padL}\" y2=\"{padT + plotH}\" stroke=\"#d0d7de\" stroke-width=\"1\"/>");
        sb.AppendLine($"  <line x1=\"{padL + plotW}\" y1=\"{padT}\" x2=\"{padL + plotW}\" y2=\"{padT + plotH}\" stroke=\"#d0d7de\" stroke-width=\"1\"/>");
        sb.AppendLine($"  <line x1=\"{padL}\" y1=\"{padT + plotH}\" x2=\"{padL + plotW}\" y2=\"{padT + plotH}\" stroke=\"#d0d7de\" stroke-width=\"1\"/>");

        // Y labels (left = GFLOPS, right = °C)
        for (var i = 0; i <= 4; i++)
        {
            var y = padT + plotH * i / 4.0;
            var gflops = maxGflops * (4 - i) / 4.0;
            var temp = maxTemp * (4 - i) / 4.0;
            sb.AppendLine($"  <text x=\"{padL - 6}\" y=\"{(y + 4).ToString("F1", CultureInfo.InvariantCulture)}\" text-anchor=\"end\" font-size=\"10\" fill=\"#59636e\">{gflops.ToString("F0", CultureInfo.InvariantCulture)}</text>");
            sb.AppendLine($"  <text x=\"{padL + plotW + 6}\" y=\"{(y + 4).ToString("F1", CultureInfo.InvariantCulture)}\" text-anchor=\"start\" font-size=\"10\" fill=\"#59636e\">{temp.ToString("F0", CultureInfo.InvariantCulture)}°</text>");
        }
        // X labels (time in seconds)
        for (var i = 0; i <= 4; i++)
        {
            var x = padL + plotW * i / 4.0;
            var sec = maxT * i / 4.0;
            sb.AppendLine($"  <text x=\"{x.ToString("F1", CultureInfo.InvariantCulture)}\" y=\"{padT + plotH + 16}\" text-anchor=\"middle\" font-size=\"10\" fill=\"#59636e\">{sec.ToString("F0", CultureInfo.InvariantCulture)}s</text>");
        }

        // Lines
        if (cpuTPath.Length > 0)
            sb.AppendLine($"  <path d=\"{cpuTPath}\" fill=\"none\" stroke=\"#CF222E\" stroke-width=\"1.5\" stroke-dasharray=\"4 3\"/>");
        if (gpuTPath.Length > 0)
            sb.AppendLine($"  <path d=\"{gpuTPath}\" fill=\"none\" stroke=\"#9A6700\" stroke-width=\"1.5\" stroke-dasharray=\"4 3\"/>");
        sb.AppendLine($"  <path d=\"{cpuPath}\" fill=\"none\" stroke=\"#0969DA\" stroke-width=\"2\"/>");
        sb.AppendLine($"  <path d=\"{gpuPath}\" fill=\"none\" stroke=\"#1A7F37\" stroke-width=\"2\"/>");

        sb.AppendLine("</svg>");
        return sb.ToString();
    }

    static string BuildPath<T>(IEnumerable<T> items, Func<T, (double x, double y)> map)
    {
        var sb = new StringBuilder();
        var first = true;
        foreach (var it in items)
        {
            var (x, y) = map(it);
            sb.Append(first ? "M" : "L");
            sb.Append(x.ToString("F1", CultureInfo.InvariantCulture));
            sb.Append(' ');
            sb.Append(y.ToString("F1", CultureInfo.InvariantCulture));
            sb.Append(' ');
            first = false;
        }
        return sb.ToString();
    }

    // -------------------- footer --------------------

    static string Footer(string runId, string toolVersion, bool anonymize)
    {
        var rid = anonymize ? Anonymize(runId) : runId;
        return $"<footer>Lapassay {Esc(toolVersion)} &middot; run {Esc(rid)}{(anonymize ? " &middot; anonymized" : "")}</footer>";
    }

    static string Hostname(string runId)
    {
        // runId format: "<ISO timestamp>Z-<host>-[sustained-]<8charId>".
        // ISO timestamp itself contains '-', so split on the first 'Z-'.
        var zIdx = runId.IndexOf('Z');
        if (zIdx < 0) return "host";
        var afterZ = runId.Substring(zIdx + 1).TrimStart('-');
        var firstDash = afterZ.IndexOf('-');
        return firstDash < 0 ? afterZ : afterZ.Substring(0, firstDash);
    }

    static string Anonymize(string runId)
    {
        var zIdx = runId.IndexOf('Z');
        if (zIdx < 0) return "redacted";
        var prefix = runId.Substring(0, zIdx + 1);
        var afterZ = runId.Substring(zIdx + 1).TrimStart('-');
        var lastDash = afterZ.LastIndexOf('-');
        var guid = lastDash < 0 ? afterZ : afterZ.Substring(lastDash + 1);
        return $"{prefix}-redacted-{guid}";
    }

    static string Esc(string? s) => HttpUtility.HtmlEncode(s ?? "");
}
