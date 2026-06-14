# Changelog

All notable changes to Lapassay are documented here. Versions follow
[semantic versioning](https://semver.org); the JSON `schemaVersion` is tracked
separately and only ever changes additively.

## [0.7.0] — 2026-06-14

> **Scores are not directly comparable to 0.6.0.** Two methodology changes shifted
> numbers on purpose (see *Measurement accuracy* below). The `compare` command maps
> the renamed FP16 kernel across versions automatically.

### Measurement accuracy
- **SGEMM**: the B-matrix transpose is no longer recomputed inside the timed region.
  It ran on every measured iteration — counting O(N²) setup in the GFLOPS figure and
  allocating a 4 MB large-object-heap array each time (so a GC could fire mid-measurement).
  CPU `sgemm` values rise slightly and their run-to-run spread tightens.
- **GPU adapter reconciliation**: the D3D12 matmuls pick the high-performance adapter
  while the ONNX/DirectML kernel previously used `deviceId 0` (DXGI's *default* adapter,
  often the iGPU on dual-GPU laptops) — silently blending two chips into one GPU score.
  The AI kernel now targets the same adapter, and every GPU result records which adapter
  ran it (shown in the report, redacted in anonymized exports).
- **FP16 kernel renamed** `gpu.matmul.fp16` → `gpu.matmul.fp16alu`: it measures `min16float`
  ALU throughput, not tensor cores, so on some GPUs it is at or below FP32 — expected, not a
  regression. Its baseline dropped from 2000 to a provisional 1000 (pending recalibration).
- **Environment capture**: VRAM now read via DXGI (WMI's 32-bit field capped every dGPU at
  ~4 GB); the WMI rated clock is recorded as the base clock instead of being mislabeled as
  max turbo. Battery Low/Critical states are no longer reported as "on AC".

### Added
- **Confidence indicator** beside every score (GUI, HTML report, CLI): HIGH / MEDIUM / LOW,
  driven by GPU clock locking (Developer Mode) and repeat count, with power source and
  telemetry access disclosed. Backed by a new additive `context` field (schema 1.3).
- **`--repeat N`** runs the suite N times and reports per-benchmark median + IQR (p25–p75),
  with a cooldown between passes (`--repeat-cooldown-sec`, default 30).
- **Run cancellation**: a STOP button on the single-run tab and Ctrl-C support in the CLI
  `run` command. No partial result is written.
- **HTML report**: dark-mode support (`prefers-color-scheme`), a "what it measures" column,
  per-GPU-row adapter disclosure, mobile-safe scrollable tables, and a link back to the project.
- Tag-triggered **release pipeline** (self-contained binaries + `SHA256SUMS`), CI on every
  PR, and Dependabot.

### Fixed
- Data race in the telemetry sampler; the telemetry monitor no longer leaks when a kernel throws.
- Several JSON read paths used non-canonical options and would reject runs containing
  `NaN`/`Infinity` values; all reads now go through one loader.
- Hyphenated hostnames were truncated at the first dash in reports and history.
- Sustained reports for long runs are decimated (chart only) so the file stays shareable.
- CLI numeric flags are range-checked instead of crashing inside a kernel.
- Version string was duplicated across five files (sustained runs stamped a stale `0.4.0`);
  there is now a single source of truth.

### Removed
- Unused BenchmarkDotNet dependency (it shipped inside every self-contained binary).

[0.7.0]: https://github.com/nobackhand/lapassay/releases/tag/v0.7.0
