# 2026-06-03 — Modernization, UX, and polish

Status: **Phases 1, 2 (all), and 3 shipped except 3.4-Phase-C** (all on `claude/zealous-carson-MWtGe`); **only 3.4 per-adapter GPU remains — deliberately deferred to a Windows machine.** Authored 2026-06-03 against v0.6.0.

> Items below carry `✅ SHIPPED` once landed. Everything shipped so far is verifiable by
> inspection (config / pure logic / cross-platform tests); none of it was built or run in the
> sandbox — the new CI job (2.1) is what actually compiles + tests it on Windows.

## Context

A product/UX/architecture review of v0.6.0. The codebase is healthy — clear structure,
good comments, an "instrument" design language (warm near-black, single amber accent,
JetBrains Mono). So this is **surgical**, not a rewrite. The work is ranked by impact and
sliced into phases that are individually shippable.

### Hard environment constraint (read before starting)

Lapassay is **Windows-only** (D3D12, WMI, RAPL MSRs, registry) and targets `win-x64`.
The web/agent execution sandbox is **Linux with no .NET SDK and an allowlist that blocks
every dotnet SDK download host** (only `nuget.org` is reachable). Therefore:

- You **cannot `dotnet build` / `dotnet test`** from the sandbox.
- Prefer changes verifiable by inspection: pure functions, string/markup/CSS generation,
  the HTML report, label/text changes.
- Anything touching D3D12 / WMI / telemetry / live runs **must be validated on Windows**.
- **Phase 2, item 1 (CI) exists specifically to close this gap** — do it first so every
  later change gets built+tested on a real Windows runner.

### Top problems found (ranked), mapped to phases

1. HTML report (only shareable artifact) not mobile-safe — **Phase 1 ✅**
2. Report visual language is generic/disconnected from the app — **Phase 3**
3. Category labels implemented 3× divergently — **Phase 1 ✅**
4. No tests on report/scoring/compare — **Phase 1 ✅ (report)**, **Phase 2 (scoring/compare)**
5. Dead code in Battery⇄AC start handler — **Phase 1 ✅**
6. CLI parsing is locale-fragile + crashes ungracefully — **Phase 2**
7. Orchestration lives in 480-line code-behind with duplicated deserialize/diff blocks — **Phase 2**
8. `HtmlReport.Hostname()` brittle runId string-parsing — **Phase 3**
9. Undocumented score-bar magic numbers — **Phase 3**
10. Instrument palette hard-coded in 4 files (drift risk) — **Phase 2**

---

## Phase 1 — Quick wins ✅ SHIPPED

Commit: "Unify category labels + make HTML report mobile-safe". No behavior change to
benchmarks/scoring; presentational + consistency only.

- **Single source of truth for category labels.** `BenchmarkCatalog.CategoryLabel()` now
  maps `cpu.integer → "CPU integer"`, `cpu.parallel → "CPU scaling"`, etc. Consumed by the
  GUI score chips (`MainWindowViewModel`), HTML hero chips (`HtmlReport`), and CLI summary
  (`Program.cs`). Deleted two duplicated `PrettyCategory` maps.
- **Mobile-safe HTML report.** Benchmarks/diff/scaling tables wrapped in
  `.table-wrap { overflow-x:auto }` with a `min-width` so columns don't crush; added a
  `@media (max-width:560px)` block (stacked env grid, smaller hero); hero sub-rows wrap.
- **Removed dead code** in `MainWindow.axaml.cs` `OnBatteryAcStartClicked` (no-op ternary
  whose `StatusText` was immediately overwritten).
- **Tests:** `tests/Lapassay.Core.Tests/HtmlReportTests.cs` — smoke, HTML-escaping/injection
  safety, anonymize redaction, shared label, responsive markup. Cross-platform (no Windows
  APIs), but **not yet executed** — run under Phase 2 CI.

**Remaining Phase-1 verification (do on Windows):** `dotnet test` passes incl. the 5 new
tests; open a generated `.html` at ~390px to confirm tables scroll inside their cards.

---

## Phase 2 — Structural improvements

### 2.1 CI (do this first) — build + test on a real Windows runner ✅ SHIPPED

**Why first:** closes the "can't build locally" gap so every later change is verified.

**Files:**

- New `.github/workflows/ci.yml`:
  - `windows-latest` job: `actions/setup-dotnet@v4` (8.0.x) → `dotnet build -c Release` →
    `dotnet test -c Release` (full suite; the Windows-guarded kernel/env tests run here).
  - Optional `ubuntu-latest` job: build `Lapassay.Core` + run only OS-agnostic tests
    (`--filter "FullyQualifiedName~JsonReportTests|FullyQualifiedName~HtmlReportTests"`) to
    catch cross-platform regressions cheaply. Keep it non-blocking if the win-x64 RID makes
    restore awkward.
- Add a status badge to `README.md`.

**Verification:** push a no-op commit → both jobs green; the 5 Phase-1 report tests show as run.

### 2.2 Route ALL JSON reads through `JsonReport.Deserialize` (fixes a latent bug) ✅ SHIPPED

**Bug:** `JsonReport.Opts` sets `NumberHandling = AllowNamedFloatingPointLiterals`, so a run
that serializes `NaN`/`Infinity` (e.g. a degenerate rate when a kernel medians to ~0s) writes
those literal tokens. But **5 call sites build their own `JsonSerializerOptions` without it**
and will throw on such a file:

- `src/Lapassay.Cli/Program.cs:160` (`CompareRuns`), `:245` (`RenderReport`)
- `src/Lapassay.Gui/MainWindow.axaml.cs:302` (`OnHistoryCompareSelectedClicked`),
  `:375` (`OnBatteryAcContinueClicked`), `:464` (`OnCompareClicked`)

**Change:** replace each `JsonSerializer.Deserialize<BenchmarkRun>(File.ReadAllText(p), jsonOpts)`
with `JsonReport.Deserialize(File.ReadAllText(p))`. Delete the local `jsonOpts`. For the
sustained branch in `RenderReport`, add `JsonReport.DeserializeSustained()` (new, using the
same canonical `Opts`).

**Verification:** craft a JSON with an `Infinity` value → `compare`/history/report all load it.
Existing files still load unchanged.

### 2.3 Extract diff orchestration out of code-behind ✅ SHIPPED

`MainWindow.axaml.cs` repeats the same block 3× (history compare, battery-AC diff, file-picker
compare): deserialize A+B → `Compare.Diff` → `HtmlReport.WriteToFile` → open in browser.

**Change:** add `src/Lapassay.Core/Reporting/DiffService.cs`:
```
static string WriteDiff(string pathA, string pathB, string labelA, string labelB, string? outDir = null)
```
returns the written HTML path (loads via `JsonReport.Deserialize` — composes with 2.2).
Code-behind handlers shrink to: compute labels → `DiffService.WriteDiff(...)` → open. Same for
`Program.cs CompareRuns`. This removes ~60 duplicated lines and the divergent options bug class.

**Verification:** all three GUI diff paths + CLI `compare` still produce identical HTML; manual
click-through on Windows.

### 2.4 Harden CLI argument parsing ✅ SHIPPED

`src/Lapassay.Cli/Program.cs`: `int.Parse`/`double.Parse` (`--cpu-n`, `--gpu-n` ~lines 46–47;
`--duration` ~line 90) use the current culture and throw an unhandled `FormatException` with a
stack trace on bad input.

**Change:** a small `TryParseIntArg` / `TryParseDoubleArg` helper using
`CultureInfo.InvariantCulture` + `NumberStyles`; on failure print
`Error: --cpu-n expects an integer, got 'abc'` and `return 2`. Validate ranges (e.g. `--cpu-n`
≥ 64) to match the GUI's `NumericUpDown` bounds.

**Verification:** `lapassay run --cpu-n abc` → friendly message, exit 2 (not a stack trace);
`--duration 1.5` parses on a comma-decimal locale.

### 2.5 Centralize the instrument palette (GUI charts) ✅ SHIPPED

`#16110D / #998B78 / #F97316 / #A3E635 / #F87171 / #D69D45` are re-parsed in
`TelemetryChart.cs`, `SustainedChart.cs`, `HistoryTrendChart.cs` (and defined as tokens in
`App.axaml`). Drift risk.

**Change:** add `src/Lapassay.Gui/InstrumentPalette.cs` with `static readonly` `Color`/`IBrush`/`Pen`
fields (Bg, TextDim, TextFaint, CpuAccent `#F97316`, GpuAccent `#A3E635`, CpuTemp `#F87171`,
GpuTemp `#D69D45`, Grid). Charts reference it. Leave `App.axaml` tokens as the XAML mirror, or
generate both from one source if cheap. **Do not** touch `HtmlReport`'s palette — its light
theme is an intentional, separate concern (see Phase 3.1).

**Verification:** GUI charts render identically (pixel-diff a live run before/after on Windows).

### 2.6 `--repeat N` + median/IQR (credibility) ✅ SHIPPED

The single biggest *trust* feature. Shipped: `Repeats` model field (additive), the pure
`RepeatAggregation.Merge` (median + linear-interpolated p25/p75, re-scored from medians; unit
tested), `Runner.RunRepeated` (thin loop with cooldown), CLI `--repeat`/`--repeat-cooldown-sec`,
console IQR column, HTML IQR cells + "Median of N runs" hero badge. Schema bumped to `1.2`.
GUI intentionally left single-run (no repeat control) to keep that surface unchanged.

---

## Phase 3 — Polish & long-term

### 3.1 Report theme — ✅ SHIPPED (option b: `prefers-color-scheme`)

Chose **(b)**: light stays the default (best for print/share); a `@media (prefers-color-scheme:
dark)` block flips the variable-driven "paper" (page/cards/text/borders/grid) to the app's proven
instrument tokens, plus `<meta name="color-scheme" content="light dark">`. Light mode renders
byte-identically (literal hexes replaced by vars of the same value). The colored hero cards and
saturated data-series colors are unchanged; the SVG gridline (the one near-white element) is
var-driven so it doesn't glow on dark. **Manual check on a dark-mode device still wanted** — the
SVG axis text/lines (mid-gray) were left literal and should read fine but weren't previewed.

### 3.2 Stop string-parsing `runId` for the hostname ✅ SHIPPED (robust-parse variant)

`HtmlReport.Hostname()` split `runId` on the *first* `-` after `Z`, truncating hyphenated
hostnames. Fixed to peel the trailing `-<id>` (last `-`) instead, so `my-laptop` survives;
covered by a test. (`Anonymize()` already used the last `-`, so it was fine.) Chose the
robust-parse over adding a schema field — same fix, zero schema churn.

### 3.3 Document the score-bar constants ✅ SHIPPED

`HtmlReport` benchmark bar uses `b.Score / 15.0` ("1500 = full bar") and the diff bar clamps at
±50% — undocumented magic. Hoist to named `const` with a one-line rationale each.

### 3.4 Per-adapter GPU + FP16-ALU relabel — Phase D ✅ SHIPPED, Phase C deferred

**Phase D (FP16-ALU relabel) ✅ SHIPPED:** kernel id `gpu.matmul.fp16.N` → `gpu.matmul.fp16alu.N`
(Runner), baseline dropped `2000 → 1000` and the misleading "2× FP32" comment replaced (Scoring),
catalog/README labels now say "FP16 ALU / min16float — not tensor cores", kernel XML doc clarifies
it's ALU not tensor cores, and `Compare.Canonical` maps the legacy id so old↔new diffs still pair
(unit tested). Schema `1.2`. **The 1000 baseline is provisional** — recalibrate on the reference
Ryzen AI 9 HX 370 + RTX 4070 laptop.

**Phase C (per-adapter GPU runs) — DEFERRED, do on Windows.** This requires real D3D12 work
(`D3D12Context.EnumerateAll`, per-adapter device contexts, per-adapter telemetry isolation) that
**cannot be verified by inspection** and would risk breaking the GPU device-creation path if done
blind. It needs a Windows machine with ≥2 adapters to develop and validate. Follow Phase C of the
2026-05-19 plan. This is the one remaining plan item, held deliberately.

---

## Critical files (by phase)

- 2.1 `.github/workflows/ci.yml` (new), `README.md`
- 2.2 `src/Lapassay.Core/Reporting/JsonReport.cs`, `Program.cs`, `MainWindow.axaml.cs`
- 2.3 `src/Lapassay.Core/Reporting/DiffService.cs` (new), `MainWindow.axaml.cs`, `Program.cs`
- 2.4 `src/Lapassay.Cli/Program.cs`
- 2.5 `src/Lapassay.Gui/InstrumentPalette.cs` (new) + the 3 chart controls
- 2.6 / 3.4 see `docs/plans/2026-05-19-truth-seeking-improvements.md`

## Functions/utilities to reuse

- `JsonReport.Deserialize` / canonical `JsonReport.Opts` — the *only* sanctioned JSON reader.
- `BenchmarkCatalog.CategoryLabel` / `.Describe` — single source for all display strings.
- `Reporting.Compare.Diff` — unchanged; wrap it in `DiffService` (2.3).
- `Scoring.Compute` / `.ScoreFor` — unchanged; add unit tests (2.x) around them.

## Sequencing

`2.1 (CI)` → `2.2 (JSON safety)` → `2.3 (DiffService)` → `2.4 (CLI)` → `2.5 (palette)` →
`2.6 (--repeat)` → Phase 3. Each is independently shippable and CI-guarded after 2.1.

## Out of scope (for now)

- Full rewrite / framework migration — unjustified; the architecture is sound.
- Tensor-core FP16 kernel (DXC + `cs_6_8`) — tracked in the 2026-05-19 plan.
- Anything that breaks the JSON schema non-additively or removes a feature.
