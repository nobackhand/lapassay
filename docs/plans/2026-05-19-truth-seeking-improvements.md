# 2026-05-19 — Truth-seeking improvements

Status: **planned, not yet implemented**. Authored 2026-05-19 against v0.6.0.

## Context

A v0.6.0 run on a Ryzen AI 9 HX 370 + RTX 4070 Laptop laptop surfaced four credibility gaps in the benchmark:

1. **FP16 < FP32 (254 vs 654 score).** `gpu.matmul.fp16` uses `min16float` in `cs_5_1` — it measures FP16 *ALU* throughput, not tensor cores. On Ada NVIDIA, FP32 ALU lanes are often faster than packed FP16 ALU, so the inversion is expected behavior, not a regression. The benchmark silently mislabels it.
2. **Only one GPU tested.** The RTX 4070 was picked, the AMD 890M iGPU was ignored. On a laptop benchmark, "which adapter ran" should be visible and per-adapter results should be available.
3. **Single-shot uncertainty.** `cpu.zstd.level3` showed 15.95% within-run stdev. One run cannot distinguish that from a real change. There's no `--repeat` / IQR support.
4. **Missing context.** No record of AC vs battery, baseline CPU/GPU load, start CPU temp, or admin/Dev-Mode status in the result. A cold-start AC run reads identically to a warm-thermals battery run in the JSON.

Goal: make the benchmark honest about variance, context, and what it's actually measuring — *without* breaking the existing JSON schema for the GUI and Compare tool.

## Decisions locked

- **FP16: relabel-only this round.** A real tensor-core kernel requires swapping `Vortice.D3DCompiler.Compiler.Compile()` for DXC, targeting `cs_6_8`, and writing WaveMatrix / Cooperative Vectors kernels — a separate 2-3 day project. Out of scope here.
- **`--all-gpus` is opt-in.** Default behavior (pick HighPerformance adapter) is preserved. The flag triggers per-adapter runs.
- **Schema bump policy.** `SchemaVersion` goes `1.0 → 1.1` at the start of Phase A and holds through B and C (all additive). Phase D bumps to `1.2` only because the FP16 kernel id changes.

## Schema strategy

Keep the top-level `BenchmarkRun` shape canonical. Add optional trailing fields:

- `BenchmarkResult.Repeats?` (Phase A)
- `BenchmarkResult.Adapter?` (Phase C)
- `Scores.Repeats?` (Phase A)
- `BenchmarkRun.Context?` (Phase B)

System.Text.Json ignores unknown fields by default, so v1.0 readers (older GUI binaries) continue to deserialize v1.1/v1.2 files unchanged. `Value`/`Score` remain the canonical median across all phases — IQR rendering is purely additive.

Rejected alternative: `runs: BenchmarkRun[]` with a top-level `aggregate`. Forces every consumer to branch on schema version and breaks the Compare tool's single-run assumption.

---

## Phase A — `--repeat N` with median + IQR

Wrap `Runner.Run()` in an N-iteration loop, aggregate per-benchmark `Value` across runs into median/p25/p75, re-score from medians.

**Files:**

- `src/Lapassay.Core/Models/Models.cs` (currently lines 53-81)
  - Add `record Repeats(double[] Values, double Median, double P25, double P75)`.
  - Add `record ScoreRepeats(int[] OverallValues, int Median, int P25, int P75, int[]? CpuValues, int[]? GpuValues)`.
  - Extend `BenchmarkResult` with trailing `Repeats? Repeats = null`.
  - Extend `Scores` with trailing `ScoreRepeats? Repeats = null`.
- `src/Lapassay.Core/Runner.cs` (currently lines 15-109)
  - Extend `RunOptions` with `int Repeat = 1, int RepeatCooldownSec = 30`.
  - Add `RunRepeated(RunOptions, log)`: calls `Run()` N times, sleeps `RepeatCooldownSec` between (suppress cooldown if 0). Merges results by id: collect `Value` across runs, sort, compute median/p25/p75, emit a single `BenchmarkRun` whose `Benchmarks[i].Value = median` and `Benchmarks[i].Repeats = new Repeats(...)`. Re-scores from medians via `Scoring.Compute`. Uses last run's `Environment` and `Telemetry` (document this).
  - Bump `SchemaVersion` literal at line 101 from `"1.0"` to `"1.1"`.
- `src/Lapassay.Cli/Program.cs` (currently lines 38-52)
  - Add `--repeat N` and `--repeat-cooldown-sec N` cases in the switch. Default 1 / 30.
  - Reject `--repeat` for `sustained` subcommand with an error.
  - `RunSingle` dispatches to `Runner.Run` if N==1 else `Runner.RunRepeated`.
  - Update `PrintUsage` and `PrintSummary` to show "N runs (median ± IQR)" when applicable.
- `src/Lapassay.Core/Reporting/HtmlReport.cs` (currently lines 78-413)
  - When `Repeats != null`, append `± [p25..p75]` after each value cell.
  - Add "N runs" badge in the hero.

**Schema delta:** `schemaVersion: "1.1"`, new optional `repeats` field on each benchmark result and on `scores`.

**Verification:**

- `lapassay run --cpu --repeat 5` produces JSON with 5 values per benchmark; `repeats.median == value`.
- Load output in unmodified GUI (built against schema 1.0); should render unchanged.
- `lapassay compare a.json b.json` where one is N=1 and the other is N=5 — compares medians, no crash.

---

## Phase B — Preflight + RunContext capture

Sample baseline CPU/GPU utilization for 2s before the run, capture start CPU temp, surface power state and available adapters. Store in JSON. Show as badges in HTML hero.

**Files:**

- `src/Lapassay.Core/Models/Models.cs`
  - Add `record RunContext(string PowerState, double BaselineCpuUtilPct, double? BaselineGpuUtilPct, double? StartCpuTempC, List<string> GpuAdaptersAvailable, bool IsAdmin, bool DeveloperMode)`.
  - Extend `BenchmarkRun` with trailing `RunContext? Context = null`.
- `src/Lapassay.Core/Preflight.cs` (currently lines 21-57)
  - Extend `Result` record with the same fields as `RunContext`.
  - Add `Check()` overload that performs a 2-second baseline sample using a transient `HardwareMonitor` + `PerformanceCounter("Processor","% Processor Time","_Total")`.
  - Warnings: `OnBattery`, `BaselineCpuUtil > 10%`, `BaselineGpuUtil > 10%`, `StartCpuTempC > 60°C`.
- `src/Lapassay.Core/Kernels/Gpu/D3D12Context.cs` (currently lines 25-102)
  - Add `public static List<AdapterInfo> EnumerateAll()` returning all non-software hardware adapters. Read-only in Phase B; Phase C uses it for per-adapter contexts.
  - Add `record AdapterInfo(string Name, long VramBytes, bool IsSoftware)`.
- `src/Lapassay.Core/Runner.cs`
  - Accept `Preflight.Result` and copy fields into `BenchmarkRun.Context`.
- `src/Lapassay.Cli/Program.cs` (currently lines 264-273)
  - `PrintPreflight` prints baseline utilization, start temp, adapters available.
- `src/Lapassay.Core/Reporting/HtmlReport.cs`
  - New "context badges" row in the hero: AC/battery, Dev Mode, Admin, start-temp, baseline-util, repeat count, adapters available.
- `src/Lapassay.Cli/Program.cs` (`report --anonymize` path, currently lines 212-228)
  - Strip `GpuAdaptersAvailable` (leaks adapter names) when anonymizing.

**Schema delta:** new optional `context` field on `BenchmarkRun`.

**Verification:**

- Run on battery → expect "on battery" warning + badge.
- Run with a YouTube tab pegging CPU → expect util warning.
- Load a v1.0 JSON in the new GUI → no crash, `Context` is null.

---

## Phase C — Per-adapter GPU runs

When `--all-gpus` is set, enumerate all hardware adapters and run the GPU suite per adapter. Tag each GPU benchmark with `Adapter`. Scoring picks the best per-id for the canonical `Scores.Gpu`.

**Files:**

- `src/Lapassay.Core/Kernels/Gpu/D3D12Context.cs`
  - Split the constructor: factor adapter selection into `EnumerateAll()` (added in Phase B) and a new `D3D12Context(IDXGIAdapter1 adapter, bool enableDebug, bool enableStablePowerState)` constructor.
  - Keep current parameterless ctor as a "pick high-performance" wrapper for back-compat.
- `src/Lapassay.Core/Runner.cs` (currently lines 226-291)
  - `MakeGpuContext(IDXGIAdapter1? adapter = null)`.
  - `RunGpuFp32Matmul`, `RunGpuFp16Matmul`, `RunOnnxSqueezenet` accept an `IDXGIAdapter1?`.
  - New `RunGpuSuite(IDXGIAdapter1 adapter) → List<BenchmarkResult>` that runs all three GPU kernels against one adapter and tags each result with `Adapter = adapter.Description1.Description`.
  - When `RunOptions.AllGpus`, iterate `D3D12Context.EnumerateAll()` and call `RunGpuSuite` per adapter.
  - Scoring: pick best `Score` per benchmark id across adapters for the canonical `Scores.Gpu`. All per-adapter results are preserved in `Benchmarks`.
- `src/Lapassay.Core/Models/Models.cs`
  - Extend `BenchmarkResult` with trailing `string? Adapter = null`.
- `src/Lapassay.Cli/Program.cs`
  - Add `--all-gpus` flag.
  - Add `--gpu-adapter <name>` flag for explicit single-adapter selection.
- `src/Lapassay.Core/Reporting/HtmlReport.cs`
  - Group GPU rows by adapter; add adapter sub-headers.
- `src/Lapassay.Core/Compare.cs` (find exact path via Glob during implementation)
  - Match benchmarks by `(Id, Adapter)` not just `Id`. Critical: without this, single-GPU v1.0 diffs against multi-GPU v1.1 files silently collide.
- `src/Lapassay.Core/Telemetry/HardwareMonitor.cs`
  - Per-adapter run: filter `HardwareType.Gpu*` matches to the active adapter's vendor (from `Description1.Description`).
  - If two adapters share a vendor (rare on laptops), log a warning that GPU telemetry is ambiguous.

**Schema delta:** new optional `adapter` field on `BenchmarkResult`.

**Verification:**

- Ryzen AI 9 HX 370 + RTX 4070 with `--all-gpus` → JSON has 6 GPU `BenchmarkResult` entries (3 kernels × 2 adapters); HTML has two GPU sections; `Scores.Gpu` reflects RTX.
- Single-GPU desktop with or without `--all-gpus` → unchanged behavior.
- Compare two multi-adapter runs → diffs are per-adapter, not collapsed.

---

## Phase D — FP16 ALU relabel + baseline correction

Rename the kernel to clarify it measures ALU, not tensor cores. Drop the baseline to match reality. Flag follow-up for the real tensor-core kernel.

**Files:**

- `src/Lapassay.Core/Runner.cs` (line 78 + RunGpuFp16Matmul body)
  - Change kernel id from `gpu.matmul.fp16.{n}` to `gpu.matmul.fp16alu.{n}`. Update progress label.
- `src/Lapassay.Core/Scoring/Scoring.cs` (currently lines 22-40, baseline at line 38)
  - Rename baseline key to match new id.
  - Drop baseline from 2000 GFLOPS to ~1000 GFLOPS (tune after a calibration pass on the reference Ryzen AI 9 HX 370 + RTX 4070 laptop). Add comment that this is ALU throughput, not tensor cores.
- `src/Lapassay.Core/Reporting/HtmlReport.cs`
  - Display name "FP16 ALU matmul (not tensor cores)" with a tooltip linking to the schema description.
- `src/Lapassay.Cli/Program.cs` (currently lines 317-322 in `PrintSummary`)
  - Same friendly label.
- `src/Lapassay.Core/Kernels/Gpu/Fp16MatmulKernel.cs`
  - XML doc comment clarifying `min16float` / `cs_5_1` semantics.
  - Flag follow-up to port to DXC + `cs_6_8` for the Cooperative Vectors tensor-core path.
- `src/Lapassay.Core/Runner.cs` line 101
  - Bump `SchemaVersion` to `"1.2"`.
- `src/Lapassay.Core/Compare.cs`
  - Add a one-line legacy mapping `gpu.matmul.fp16.{n}` ↔ `gpu.matmul.fp16alu.{n}` for cross-version diffs.
- `README.md`
  - Update the "What's measured" table to label the FP16 kernel as ALU-path.

**Schema delta:** `schemaVersion: "1.2"`, kernel id changes.

**Verification:**

- RTX 4070 score for FP16 ALU should land ~1000 (slightly below FP32, expected for non-tensor path).
- AMD 890M iGPU should land ~500–800.
- Cross-version Compare (v1.0 file vs v1.2 file) maps the renamed kernel correctly.

---

## Cross-cutting concerns

1. **`--repeat` × `sustained`:** sustained already iterates internally; reject `--repeat` for that subcommand explicitly in the CLI parser.
2. **`--repeat` × `--all-gpus`:** N × M kernel passes. Document. Allow the combination but show wall-clock estimate.
3. **`--repeat` thermal carry-over:** default `--repeat-cooldown-sec 30`. Without cooldown, IQR under-reports variance because successive runs share thermal state.
4. **Dev Mode off + N=1:** results are unreliable. Surface this combo in the hero ("Dev Mode off + N=1: results unreliable; consider `--repeat 3` or enable Dev Mode").
5. **GUI progress hook:** `Runner.Run` fires `OnKernelStart(index, total)`. With `--repeat N`, set `total = baseTotal * N` and increment `kernelIndex` across the outer loop so the progress bar doesn't reset.
6. **Anonymize mode:** `Context.GpuAdaptersAvailable` and `BenchmarkResult.Adapter` both leak adapter names. Strip in `report --anonymize`.

---

## Critical files to modify

- `src/Lapassay.Core/Runner.cs`
- `src/Lapassay.Core/Models/Models.cs`
- `src/Lapassay.Core/Kernels/Gpu/D3D12Context.cs`
- `src/Lapassay.Core/Scoring/Scoring.cs`
- `src/Lapassay.Core/Reporting/HtmlReport.cs`
- `src/Lapassay.Core/Preflight.cs`
- `src/Lapassay.Core/Telemetry/HardwareMonitor.cs`
- `src/Lapassay.Core/Kernels/Gpu/Fp16MatmulKernel.cs`
- `src/Lapassay.Cli/Program.cs`
- `src/Lapassay.Core/Compare.cs` (path to confirm via Glob)
- `README.md`

## Functions/utilities to reuse

- `Scoring.Scoring.Compute` — re-score from medians in Phase A.
- `Scoring.Scoring.ScoreFor` — per-benchmark scoring stays unchanged.
- `PowerStateDetector.GetCurrent` — already wired for AC/battery in Phase B.
- `HardwareMonitor.Snapshot()` — read CPU temp at t=0 in Phase B preflight.
- `EnvironmentCapture.Capture()` — list of GPUs already in `EnvironmentInfo.Gpu`; Phase B's `GpuAdaptersAvailable` derives from `D3D12Context.EnumerateAll()` (filters to D3D12-capable + non-software adapters, which `EnvironmentInfo.Gpu` does not).
- `JsonReport.Serialize/Deserialize` — unchanged; camelCase policy + unknown-field tolerance carries the additive schema bumps.

## End-to-end verification

1. **Phase A:** `lapassay.exe run --cpu --gpu --repeat 5 --out results/a.json` → check JSON for `repeats` arrays of length 5, `schemaVersion: "1.1"`. Open `a.html` → IQR shown after each value. Open in existing GUI build → no crash, renders without IQR.
2. **Phase B:** Yank power, then `lapassay.exe run --cpu` → expect "on battery" warning + badge in HTML. `Context.startCpuTempC` present in JSON.
3. **Phase C:** `lapassay.exe run --gpu --all-gpus --out results/c.json` → JSON has 6 GPU benchmark entries (3 kernels × 2 adapters), each with `adapter` field. HTML has two GPU sections. `Scores.Gpu` reflects RTX.
4. **Phase D:** Same `--all-gpus` run; expect FP16 ALU score on RTX ~1000 (not 254), label reads "FP16 ALU matmul (not tensor cores)". Compare a v1.0 file against a v1.2 file → renamed kernel matches via the legacy mapping.

## Out of scope (future projects)

- **DXC + cs_6_8 + Cooperative Vectors tensor-core kernel** (replaces FP16 ALU as the canonical FP16 benchmark; FP16 ALU stays as a separate kernel for ALU-throughput truth).
- **Per-adapter GPU telemetry isolation when two same-vendor GPUs are present** (rare; currently warned and sums powers).
- **CPU pinning beyond CPU 0** (single-thread kernels could benefit from explicit P-core pinning on hybrid Intel chips — non-issue on this Zen 5/5c laptop).
