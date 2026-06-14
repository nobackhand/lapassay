# 2026-06-11 — Complete product, engineering, and strategy audit

Auditor stance: hired to own the product and maximize long-term success. Nothing assumed
correct; existing decisions challenged. Evidence is cited by file. Audited at v0.6.0 +
the modernization branch (`claude/zealous-carson-MWtGe`).

---

## 1. Executive summary

Lapassay is a **fully offline, open-source Windows laptop CPU+GPU benchmark** (CLI +
Avalonia GUI + self-contained HTML reports). Its brand promise — implicitly and in the
maintainer's own planning docs — is **honest numbers**: reproducibility rigor (warmup, GC
isolation, trimmed stdev, DCE sinks, fixed seeds, thread pinning, GPU stable-power-state,
timestamp queries) that mainstream suites don't bother with, plus laptop-specific truth
(battery⇄AC compare, throttle verdict, variance reporting).

The audit's central conclusion: **the product's biggest risks are the places where its own
numbers aren't honest yet.** The flagship CPU kernel measures non-matmul work inside its
timed region; the GPU score can silently mix two different GPUs; VRAM and CPU clocks are
mis-captured/mislabeled; baselines are asserted rather than calibrated; the telemetry
sampler has a data race. None of these is hard to fix, and fixing them is worth more than
any feature, because credibility is the entire moat.

Second conclusion: **distribution is the bottleneck, not features.** There is no publish
pipeline in the repo, no screenshots in the README, no package-manager presence, and the
shared HTML report — the only viral artifact — doesn't even link back to the project.

Third: the **strategic window is "AI laptop" buying decisions.** A DirectML SqueezeNet
score is 2019-era. An NPU benchmark + small-LLM tokens/s benchmark, plus an ARM64 Windows
build (Snapdragon X laptops can't run this x64-only tool at native speed at all), would
make Lapassay the only open, offline benchmark relevant to the current laptop market.

Maturity: pre-1.0, single developer, healthy codebase (~6.6k LOC), tests recently added,
CI recently added (not yet run — branch needs a PR). No business model; this is an
OSS-reputation project, and the audit treats "success" as adoption + trust.

---

## 2. Phase 1 — Understanding (summary)

- **What it does:** 12 benchmark kernels (9 CPU: SIMD matmul, AES, SHA-256, Zstd, FFT,
  Mandelbrot, STREAM triad, pointer-chase latency, core-scaling sweep; 3 GPU: FP32/FP16-ALU
  D3D12 matmul, ONNX/DirectML SqueezeNet), scored vs a "mid-range 2024 laptop = 1000"
  baseline via geometric means. Power/thermal telemetry every 100–250 ms. Four GUI flows:
  Single run, History, Battery⇄AC guided compare, Sustained throttle test. JSON + HTML
  artifacts; `compare` diffs.
- **Target user:** technical laptop owners/buyers/reviewers who distrust closed benchmarks
  and want reproducible numbers without adware suites.
- **Problem solved:** laptop performance is a function of power source, thermals, and DVFS;
  single-shot closed benchmarks hide that. Lapassay measures and exposes it.
- **Business model:** none (MIT). Currency is trust and adoption.
- **Strengths:** measurement rigor unusual at this size; coherent "instrument" design
  language; offline/no-telemetry stance; laptop-specific features no big suite has; honest
  self-criticism in `docs/plans` (the maintainer already flagged FP16 mislabeling and
  single-GPU blindness).
- **Weaknesses:** correctness blind spots in the engine (below); zero distribution
  investment; x64-Windows-only in an increasingly ARM laptop market; baselines uncalibrated;
  no logging/diagnostics; release process undocumented.

---

## 3. Top 20 findings (ranked by severity × reach)

| # | Sev | Finding | Evidence |
|---|-----|---------|----------|
| 1 | **Critical** | **SGEMM transposes B inside the timed region, every iteration.** `Run()` calls `Transpose(_b)` — O(N²) non-matmul work counted in the GFLOPS time, and a fresh 4 MB array (LOH at N=1024) allocated per iteration. 23 iterations ≈ 92 MB LOH churn; GC can fire *inside* timed iterations, despite the harness's careful pre-measurement collect. `_b` never changes after the ctor — the transpose is loop-invariant. Distorts the flagship CPU number and inflates its stdev. | `SgemmKernel.cs:38`, `TimingHarness.cs:23-27` |
| 2 | **Critical** | **GPU score can mix two different GPUs.** D3D12 matmuls pick the HighPerformance adapter (`D3D12Context.cs:54`); the ONNX kernel passes DirectML `deviceId: 0` (`OnnxInferenceKernel.cs:23,32`) — the *default* adapter, which on dual-GPU laptops is often the iGPU. `gpu.compute` (dGPU) and `gpu.ai` (iGPU) then geomean into one "GPU" score with no indication. The maintainer's plan flags adapter visibility generally; this specific cross-kernel mismatch is worse than noted. |
| 3 | High | **Data race in the telemetry sampler.** `HardwareMonitor._samples` is a `List<T>` appended on a background task while `Latest()` (called every sustained-loop iteration) and `Stop()` read it concurrently. `List<T>` is not thread-safe for concurrent read/write — torn reads or internal corruption are possible under load. | `HardwareMonitor.cs:26,65,86-91` |
| 4 | High | **Version/schema drift.** Sustained runs stamp `ToolVersion: "0.4.0"` and `SchemaVersion: "1.0"` while single runs are `0.6.0`/`1.2`; the version literal exists in 5 places (CLI ×2, GUI header, Runner, SustainedRunner). | `SustainedRunner.cs:116-118` vs `Runner.cs:131` |
| 5 | High | **VRAM is wrong for every GPU >4 GB.** WMI `Win32_VideoController.AdapterRAM` is a 32-bit field; an 8 GB RTX 4070 reports ≈4095 MB in every report. DXGI (already a dependency) exposes `DedicatedVideoMemory` correctly. | `EnvironmentCapture.cs:41-49` |
| 6 | High | **CPU clock mislabeled.** WMI `MaxClockSpeed` (the *rated/base* clock) is stored as `MaxTurboMhz` with `BaseClockMhz: 0`; the HTML report then prints it as "base". Two wrongs almost canceling — but the JSON field is misleading for any consumer. | `EnvironmentCapture.cs:30-32`, `HtmlReport.cs` env section |
| 7 | High | **Battery state misclassification.** `BatteryStatus == 1 ? Battery : AC` — statuses 4 (Low) and 5 (Critical), which occur *while discharging*, report as AC. The Battery⇄AC flow's gating and the `onBattery` env capture are both wrong exactly when the battery is low. | `PowerStateDetector.cs`, `EnvironmentCapture.cs:127-141` |
| 8 | High | **No cancel for single runs.** `Runner.RunOptions` has no `CancellationToken`; the GUI offers no Stop on the Single tab (Sustained has one). A user who clicks RUN on a slow machine waits ~a minute or kills the process. | `Runner.cs:15-17`, `MainWindow.axaml` |
| 9 | Med-High | **32 MB working sets defeat their purpose on big-L3 CPUs.** STREAM triad and pointer-chase claim ">>L3" with fixed 32 MB buffers; AMD X3D parts have 96–128 MB L3 → those SKUs measure cache, not DRAM, and get silently inflated memory scores. Size relative to detected L3 (≥4×). | `StreamTriadKernel.cs:19`, `PointerChaseKernel.cs:17` |
| 10 | Med-High | **Baselines are asserted, not calibrated.** Scoring constants carry comments like "decent iGPU" with no provenance (machine, date, run count). The FP16-ALU baseline was just corrected by 2× — evidence the others deserve scrutiny. Trust requires a versioned baseline manifest. | `Scoring.cs:22-40` |
| 11 | Med-High | **Sustained test alternates CPU and GPU instead of loading them concurrently.** A shared-heatpipe laptop that throttles only under combined load passes the test. At minimum document; better, run the loops on separate threads. | `SustainedRunner.cs:60-102` |
| 12 | Med | **HistoryScanner kept both bugs fixed elsewhere this session:** its own `JsonSerializerOptions` without `AllowNamedFloatingPointLiterals` (NaN-bearing runs silently vanish from history) and first-dash hostname truncation (`my-laptop` → `my`). | `HistoryScanner.cs:27-30,81-88` |
| 13 | Med | **CLI accepts out-of-range numerics.** `--cpu-n 0` or negative passes parsing (friendly-parse added this session lacks range checks) and dies inside the kernel; GUI clamps 64–4096 but CLI doesn't. `--repeat` unbounded. | `Program.cs` RunSingle |
| 14 | Med | **O(n²) chart rendering over long runs.** Every appended sample triggers `InvalidateVisual()`; each render replays *all* points with per-segment `DrawLine`. A 60-min sustained run ≈ 18k samples → ~5 redraws/s × 70k line segments late in the run. Decimate (LTTB) or cache geometry. | `TelemetryChart.cs`, `SustainedChart.cs` |
| 15 | Med | **Results location depends on the process CWD.** GUI and CLI write to relative `"results"` — launch from a different Start-In directory (or elevated shell) and history fragments; users "lose" runs. | `JsonReport.DefaultPath`, `HistoryViewModel.cs:21` |
| 16 | Med | **BenchmarkDotNet is a dead dependency** — referenced in Core.csproj, used nowhere (one doc-comment mention). Ships its graph in every self-contained exe. | `Lapassay.Core.csproj` |
| 17 | Med | **Security: WinRing0 kernel driver.** LHM 0.9.6's MSR access uses the WinRing0 driver family with a known vulnerable-driver history (BYOVD/LPE class). README discloses AV flags but not the risk class; track LHM's PawnIO migration and pin the story in SECURITY.md. Also: the app *encourages running as admin*, so every parser (JSON, ONNX) runs elevated. | README, `Lapassay.Core.csproj` |
| 18 | Med | **No release/publish pipeline in the repo.** README promises self-contained single-file exes; no publish profile, args, or release workflow exists — releases depend on the maintainer's shell history. | `.csproj`s, absence of workflow |
| 19 | Med | **Accessibility.** 9px tracked all-caps labels; `TextFaint` #5A4F43 on #0C0A09 ≈ 3.4:1 at tiny sizes (fails WCAG AA); hover-only tooltips; no keyboard-only path audited. HTML report is better but score bars are color-only (gpu/cpu distinction). | `App.axaml`, `MainWindow.axaml` |
| 20 | Low-Med | **Silent failure culture.** Broad `catch { }` across telemetry/WMI/GUI handlers, `async void` handlers, no log file anywhere. A failed run leaves zero diagnostics — the #1 future support cost. | throughout |

Additional notable (not top-20): `PowerStateDetector.WaitForAsync` is dead code duplicated
by a GUI DispatcherTimer; `OnnxInferenceKernel.InferencesPerSecond` duplicated by Runner;
empty-state promises "≈ 30 SECONDS" regardless of hardware; same-second runs collide on
filename (timestamp resolution 1 s); two concurrent instances contend for the LHM driver;
sleep/hibernate mid-sustained-run produces a verdict over a distorted timeline; GPU TDR
(device removal) mid-matmul surfaces as a raw exception string in the log pane.

---

## 4. Product audit

**Right problem?** Yes — and sharpened by the maintainer's own truth-seeking plan. The
2024–26 laptop market (hybrid CPUs, aggressive DVFS, shared thermal budgets, Copilot+
NPUs, ARM entrants) makes "honest, context-aware measurement" *more* valuable, not less.

**Value prop clarity:** Good in the README, **absent in the artifacts users actually see.**
The score card says "BASELINE = MID-RANGE 2024 LAPTOP = 1000" (good) but a score shown
without its *confidence* (Dev Mode off? on battery? N=1?) undermines the whole differentiator.
The product knows when its own number is shaky (Preflight) and doesn't say so *next to the
number*.

**First-run confusion:** Preflight demands admin + Developer Mode — most users will do
neither on first launch. Warnings appear in a banner, then the score renders identically
either way.

**Feature value vs complexity:** Battery⇄AC and Sustained are differentiators — keep.
History is table-stakes retention. The `--cpu-n/--gpu-n` knobs create complexity with
near-zero user value (non-default N makes scores non-comparable; the GUI exposes them
prominently). Recommend demoting them to CLI-only "advanced" flags.

**Key UX issues (problem → why it matters → fix):**
1. *No run cancellation (Single tab)* → users feel trapped; benchmarks are exactly when a
   laptop is least responsive → plumb `CancellationToken` through `RunOptions`, add Stop.
2. *Score lacks confidence context* → the honesty differentiator is invisible →
   "confidence chip" next to the score: `HIGH (admin · dev mode · N=3)` / `LOW (clocks
   unlocked · N=1)`; same badge in HTML hero.
3. *Compare = two sequential file dialogs* → awkward, error-prone ordering → one dialog,
   `AllowMultiple`, auto-order by timestamp (History tab already does this).
4. *HTML report shows raw ids only* (`cpu.fft.c2c.4096`) while the GUI has a "WHAT IT
   MEASURES" column → shared reports are illegible to recipients (the most important
   audience) → add `BenchmarkCatalog.Describe` column to the HTML table.
5. *N knobs invalidate comparability silently* → runs at N=2048 score against an N=1024
   baseline → either bake N into the benchmark id everywhere (it is) AND refuse to score
   non-baseline N, or hide the knobs.
6. *Results folder ambiguity* (finding 15) → "where did my run go?" → fixed app-data dir +
   "Open folder" already exists.
7. *Empty-state time estimate is static* → trust erosion when it takes 90 s → measure and
   show the last run's duration instead.

---

## 5. Design audit

The "instrument" language (warm near-black, single amber accent, JetBrains Mono, dot
lattice, LED, graticule) is **distinctive and disciplined** — the opposite of
AI-generated-generic. Critiques:

- **Micro-type is overused.** 9px tracked caps appear at 4+ hierarchy levels; below ~10px,
  tracking + caps hurts legibility and accessibility. Collapse to two label sizes (10/11px),
  reserve `TextFaint` for non-essential text only.
- **Contrast debt:** `TextFaint` on `Bg` ≈ 3.4:1; fails AA for the sizes it's used at.
  Lighten one step or restrict to decorative use.
- **Hard-coded one-off colors** still exist in XAML (`#D69D45` inline in MainWindow.axaml
  legend/temp tiles) despite the new `InstrumentPalette` — finish the sweep with a XAML
  token (`WarnBrush`).
- **HTML report ↔ app dissonance** is now mitigated (dark `prefers-color-scheme` added),
  but the report's GitHub-blue light identity is still generic. Acceptable trade for
  print/share; a custom accent + the JetBrains Mono numeric styling would carry the brand
  without sacrificing legibility.
- **Tab labels** (`BATTERY ⇄ AC`) rely on a glyph that may fall back ugly on some font
  stacks; test or replace with "BATTERY VS AC".
- **Color-only encodings:** chart series and score bars distinguish only by hue (orange/
  lime/red/gold); add dash patterns (already on temps — good) and a pattern or label for
  the HTML bars.

---

## 6. Engineering audit (frontend = GUI, "backend" = engine)

**GUI (Avalonia).** Architecture is honest MVVM-lite: VMs with manual `INotifyPropertyChanged`,
event handlers in code-behind. After this session's `DiffService` extraction the
code-behind is acceptable for the app's size — full command-pattern migration is **not**
justified yet. Real items: the four hand-rolled `Set/OnPropertyChanged` blocks should
collapse into one `ViewModelBase` (or CommunityToolkit.Mvvm source generators); `async
void` handlers need a shared try/log wrapper; `History.Refresh()` does synchronous file IO
on the UI thread (stutters with hundreds of results); chart rendering needs decimation
(finding 14); `BatteryAcViewModel.Cts` is declared but never used (dead).

**Engine (Core).** Strong bones: pure models, clean kernel separation, the new
`RepeatAggregation`/`DiffService` are pure and tested. The findings table covers the
defects. Structural notes: `Runner`'s `totalKernels = (cpu?9:0)+(gpu?3:0)` is a hand-count
that *will* drift when a kernel is added — derive from a kernel list; per-kernel
`HardwareMonitor` instances open/close the LHM driver 12× per run (global monitor already
exists — reuse with sample-window slicing); `Scoring` silently drops zero-score benchmarks
from the geomean (a skipped SqueezeNet quietly *raises* the GPU score's basis — should
surface "2 of 3 GPU kernels" in the score card).

**Tests.** Kernels have sanity tests (good, incl. FFT roundtrip vs reference); pure logic
gained coverage this session. Missing: `Scoring.Compute` unit tests (geomean, zero-drop
behavior), `SustainedRunner.ComputeVerdict` tests (pure!), `RepeatAggregation` edge (mismatched
benchmark sets across repeats), `HistoryScanner` tests with fixture files. The
`Sha256KernelHashMatchesSystemImplementation` test contains `Assert.True(true)` — a test
that asserts nothing about the hash; rewrite or rename honestly.

**Severity ranking of engineering debt:** 1) engine truth bugs (findings 1–2, 5–7, 9),
2) reliability (3, 8, 20), 3) drift hazards (4, 12, 13, totalKernels), 4) perf (14),
5) hygiene (16, dead code).

---

## 7. Performance audit

- **Benchmark duration** is dominated by design (warmup×8 + measure×15 per CPU kernel).
  Fine. But SGEMM fix (finding 1) will *reduce* measured-iteration time and stdev.
- **GUI:** chart O(n²) (finding 14) is the only real risk; everything else is trivial.
  Startup instantiates LHM `Computer` per monitor — first `Open()` installs/starts the
  driver, adding seconds; a singleton monitor would cut run startup noticeably.
- **Binary size:** removing BenchmarkDotNet (finding 16) and trimming Vortice packages will
  cut the self-contained exe materially (worth measuring in CI with a size budget).
- **HTML report:** a 60-min sustained run serializes ~18k samples into JSON *and* inline
  SVG paths — multi-MB files that browsers chew on. Decimate SVG points (≤2k) and consider
  sample thinning in JSON beyond a threshold.

---

## 8. Security report

1. **WinRing0 driver (BYOVD class)** — finding 17. Document in SECURITY.md; track LHM's
   PawnIO migration; consider making telemetry strictly opt-in under non-admin.
2. **Elevation by default** — the tool teaches users to run it as admin; all file/JSON/ONNX
   parsing then happens elevated. Mitigate: parse untrusted inputs (compare files picked
   from disk) with hardened settings; never load ONNX models from non-app paths
   (`FindModel()` walks up from CWD — an attacker-planted `assets/models/*.onnx` higher in
   the tree would be loaded when running from source; low risk, easy fix: restrict to
   `AppContext.BaseDirectory`).
3. **HTML injection** — covered by tests as of this session (env strings escaped).
4. **Supply chain** — no lockfile/`Directory.Packages.props`, no Dependabot config, no
   pinned actions in CI. Add all three.
5. **No code signing** — unsigned exes + a kernel driver = maximal SmartScreen/AV friction.
   Even a self-funded OV cert materially improves install success.

---

## 9. Growth audit (OSS-adoption framing)

The product has **zero growth surface today**. Ranked by impact/effort:
1. **Report footer link** — every shared HTML report should end with "Generated by
   Lapassay — free, offline, open source · github.com/nobackhand/lapassay". The report IS
   the growth loop; currently it's an orphan. (One line.)
2. **README screenshots** — a benchmark with a beautiful GUI and a text-only README is
   leaving its best asset unused. Hero screenshot + report screenshot + 30-sec GIF.
3. **Package managers** — `winget`, Scoop, Chocolatey manifests. Technical users live there.
4. **Automated releases** — tag-triggered publish workflow producing the two exes +
   checksums; removes friction and signals maintenance.
5. **"Copy score card as PNG"** button — the shareable unit for Reddit/Discord/X, where
   laptop-buying advice actually happens.
6. **Community results** — opt-in PRs of anonymized JSONs into a `results-db` repo, with a
   static results-explorer site built in CI. Zero backend, durable moat (open dataset).
7. **Activation metric to design for:** "first run completed and report opened" — currently
   threatened by preflight friction; the confidence-chip approach lets users succeed
   *without* admin/dev-mode while understanding what they'd gain.

Retention is naturally episodic (new laptop, BIOS update, repaste); History + compare are
the right surfaces; nudges would be noise. Don't build them.

---

## 10. Competitive analysis

| Competitor | Their strength | Lapassay's edge |
|---|---|---|
| Cinebench | Brand, one-number simplicity | CPU-only, no telemetry/power context, closed |
| Geekbench | Cross-platform, results browser | Closed, online-tethered results, opaque scoring; Lapassay = offline + open + variance-honest |
| 3DMark/PCMark/Procyon | Polish, industry standing | Paid, heavy, telemetry-laden; gaming-centric |
| OCCT / FurMark / Prime95 | Stress depth | Stress ≠ scored benchmark; no laptop context capture |
| HWiNFO / HWMonitor | Sensor depth | Monitoring only; Lapassay pairs load *with* sensors |
| Phoronix Test Suite | Open, vast | Linux-first; Windows support weak |

**Gaps that matter to the target buyer that nobody owns:** (a) NPU/AI-workload laptop
scoring that's open and offline; (b) ARM64 Windows native benchmarking (Snapdragon X);
(c) storage (NVMe) scoring inside a laptop-holistic suite; (d) battery-vs-AC truth —
Lapassay already owns this, market it. Differentiation strategy: **"the benchmark you can
audit"** — open kernels, versioned baselines with provenance, variance disclosure (IQR),
context capture. No competitor can follow without rebuilding their business model.

---

## 11. AI opportunities (realistic)

**Product-side AI workloads (high value):**
1. **NPU benchmark** — run the existing DirectML path against the NPU adapter where present
   (Ryzen AI, Intel AI Boost, Snapdragon Hexagon); report `npu.*` category. This is *the*
   2026 laptop question and nobody open/offline answers it.
2. **Small-LLM tokens/s** — a tiny quantized transformer via ONNX Runtime (e.g.,
   ~100–500 MB model, prompt+decode split) gives the "will it run local AI" number buyers
   actually want. Offline by construction. Model download must be explicit/opt-in to keep
   the no-network promise (ship-with or one-time fetch with hash pinning).
3. **Replace/augment SqueezeNet** — 1.24 GFLOP/inference is too small to load modern GPUs;
   it partly measures dispatch overhead. Keep for continuity, add a heavier vision model.

**Assistive AI (mostly unnecessary):**
4. **Rule-based result interpretation — not AI, and the best "smart" feature available.**
   The data already captured supports: "Memory score lags CPU by 40% — 1 RAM stick detected
   (single-channel)"; "GPU score 28% lower on battery — OEM caps TGP unplugged"; "scaling
   efficiency 45% — background load or aggressive power plan". Ship as deterministic rules
   in the report. An LLM here adds cost, nondeterminism, and an offline violation — gimmick.
5. Cloud AI anything — **rejected**; violates the core trust promise.

---

## 12. QA audit — likely bugs & test scenarios

Confirmed-by-inspection bugs: findings 1–7, 9, 12, 13 plus `Assert.True(true)` test.

High-value scenarios to script/manually run:
1. Desktop without battery → Battery⇄AC tab behavior (`PowerState.Unknown` path), env
   `onBattery` correctness.
2. Battery at <10% (status 4/5) → power detection (finding 7), second-pass gating.
3. >64 logical processors → `SetThreadAffinityMask` mask overflow (1UL<<n) and processor
   groups (Threadripper laptops exist).
4. ARM64 Windows → x64 binaries emulate (slow, misleading scores) — at minimum detect and
   warn "emulated — scores not comparable".
5. Hybrid P/E-core CPUs → pinning to CPU 0, scaling sweep at 1,2,4…physical cores crossing
   the P/E boundary — verify curve interpretation.
6. Sleep/lid-close during sustained run → elapsed-time semantics (QPC across S3/Modern
   Standby), verdict over a gap, chart rendering of the discontinuity.
7. GPU TDR/device-removed mid-matmul → user-facing error quality; app survives?
8. Two instances concurrently → LHM driver contention; same-second filename collision
   (timestamp resolution = 1 s).
9. Results folder read-only / OneDrive-synced / UNC → write failures surfaced?
10. 200+ result files → History refresh UI stall (sync IO), trend-chart density.
11. Run with CPU box unchecked & GPU checked (and vice versa) → score card layout, geomean
    semantics, HTML hero.
12. SqueezeNet model file deleted → skip path produces Value 0 → confirm GPU score basis
    (silent 2-kernel geomean) is acceptable/labeled.
13. `--repeat 3` with thermal throttling between passes → IQR width sanity; cooldown obeyed.
14. Anonymized report → grep artifact for hostname/CPU/BIOS strings (automate as a test).
15. Non-ASCII machine names → file names, runId parsing, HTML title.
16. JSON tampered (`scores` removed / nulls) → GUI history, compare, report — graceful?
17. High-DPI (150/200%) + 820×640 min window → layout truncation, chart text.
18. Keyboard-only and screen-reader pass over the GUI (tab order, automation peers).
19. AV quarantines WinRing0 mid-run → telemetry nulls handled (designed) — verify no hang in
    `Computer.Open()`.
20. Clock skew / timezone change mid-run → runId, History ordering, trend chart.

---

## 13. Technical roadmap

### Critical (fix before any release)
| Item | Impact | Effort | Risk |
|---|---|---|---|
| Hoist SGEMM transpose to ctor (finding 1) | Flagship score truth | XS | Low — but **invalidates prior CPU scores**; bump kernel id or note in changelog |
| Record + reconcile GPU adapter across kernels (min: store adapter name per result; ideal: pass DXGI adapter → DML device) | GPU score truth | S–M | Med (DML device selection API) |
| Lock around `HardwareMonitor._samples` | Crash/corruption | XS | None |
| Single `LapassayVersion` const + fix sustained schema/tool stamps | Data integrity | XS | None |
| VRAM via DXGI; clock fields relabeled (`BaseClockMhz` actually base) | Report truth | S | Low |
| Battery status set {1,4,5} = discharging | Feature correctness | XS | None |
| CLI range validation (`--cpu-n` ≥ 64 ≤ 4096, `--repeat` 1–20, duration > 0) | Crash prevention | XS | None |

### High impact
- Cancellation for single runs (token through `RunOptions`, Stop button).
- Confidence chip (GUI + HTML) from Preflight + repeat count.
- Open a PR so the new CI actually builds/tests everything; add publish workflow
  (tag → self-contained, single-file, trimmed exes + SHA256SUMS); winget manifest.
- HistoryScanner: reuse `JsonReport` options + fixed hostname parse; async refresh.
- HTML: add description column; decimate sustained SVG; footer repo link.
- Working-set sizing ≥4× detected L3 (bump kernel ids — comparability break, do with #1).
- Remove BenchmarkDotNet; add `Directory.Packages.props` + Dependabot + pinned actions.
- README screenshots.

### Medium
- Concurrent (threaded) sustained CPU+GPU mode (label which mode produced the verdict).
- `Scoring` surfaces partial-category basis ("2/3 kernels"); tests for Compute/ComputeVerdict.
- Singleton HardwareMonitor; per-kernel telemetry via window slicing.
- File logging (single rolling file) + global async-void exception wrapper.
- Results dir → `%LOCALAPPDATA%\Lapassay\results` with migration + README note.
- Accessibility pass (contrast, min font sizes, keyboard).
- Rule-based insights v1 (single-channel RAM, battery delta, scaling-efficiency hints).
- `ViewModelBase` consolidation; delete dead code (`WaitForAsync`, `BatteryAcViewModel.Cts`).

### Nice-to-have
- Score-card-as-PNG export; baseline manifest with provenance; reference-results table in
  the report ("vs known machines"); Scoop/Chocolatey; report brand refinement.

---

## 14. 30-day / 90-day / 1-year

**30 days — "make the numbers true, make it installable":** all Critical items; CI green
via PR; publish workflow + winget; README screenshots + report footer link; cancellation;
confidence chip; HistoryScanner fixes. Exit criterion: a stranger can discover, install,
run, and share a *defensible* result without reading the README.

**90 days — "earn the trust positioning":** per-adapter GPU runs (existing plan Phase C);
NPU adapter targeting for the ONNX kernel; concurrent sustained mode; dynamic working
sets + kernel-id versioning policy; baseline manifest v1 (recalibrated on ≥3 reference
machines, provenance recorded); storage (NVMe seq/random) benchmark; rule-based insights;
accessibility + logging. Exit criterion: "the benchmark you can audit" claim is literally
true — every score traceable to versioned kernels + versioned baselines.

**1 year — "the open benchmark for the AI-laptop era":** small-LLM tokens/s benchmark;
ARM64 Windows native build (D3D12/DirectML/`Vector<T>` all viable; replaces x64 intrinsics
paths where needed) — first open benchmark with honest Snapdragon X vs x86 laptop numbers;
community results dataset + static explorer site; signed binaries; v1.0 with a frozen,
documented schema and baseline governance. Strategic position: when a reviewer or buyer
asks "but how do these AI laptops *actually* compare, and can I verify it?" — Lapassay is
the only credible answer.

---

## 15. Hidden opportunities

1. **The open results dataset is the moat, not the app.** Geekbench's defensibility is its
   browser. An MIT-licensed, schema-stable, community-contributed corpus of laptop results
   (with context: power state, thermals, variance) would be unique public data — academics,
   reviewers, and r/SuffolkLaptops alike would cite it.
2. **OEM/firmware regression watch:** `compare` across BIOS versions is already built; a
   curated "this BIOS update cost 8% sustained GPU" feed is press-worthy content that
   markets itself.
3. **"Storage + RAM honesty" niche:** soldered single-channel RAM and bait-and-switch SSDs
   are the top laptop gotchas; Lapassay already detects channel count — lean in (insights,
   NVMe bench) and become the pre-purchase verification tool.
4. **Simplification candidate:** GUI N knobs (move to CLI); consider whether `--gpu-n`
   belongs at all given scoring only recognizes 2048.
5. **Rejected pivots:** cloud sync/accounts (kills the trust story), paid tier (kills
   adoption; this is a reputation asset), cross-platform GUI (Avalonia tempts; the value is
   Windows-laptop-specific truth — Linux gaming handhelds are a *maybe*, later).
