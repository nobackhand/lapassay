# Lapassay

Windows laptop CPU+GPU benchmark. Produces reproducible scores with environment capture, power/thermal telemetry, and JSON output.

## Status

**Milestone 2 — Full CPU suite** (current). CLI + GUI frontends, shared `Lapassay.Core` engine. 11 passing tests. Next milestones add GPU suite + AI inference (M3), scoring normalization + HTML report (M4), sustained-load throttle test (M5), and polished GUI features (M6).

## Build

```
dotnet build -c Release
```

## Run

Single-shot benchmark:
```
.\src\Lapassay.Cli\bin\Release\net8.0\win-x64\lapassay.exe run --out .\results\run.json
```

Sustained / throttle test (default 10 minutes):
```
.\src\Lapassay.Cli\bin\Release\net8.0\win-x64\lapassay.exe sustained --duration 600
```

GUI:
```
.\src\Lapassay.Gui\bin\Release\net8.0\win-x64\lapassay-gui.exe
```

Options for `run`:
- `--cpu` / `--gpu` (or neither = both), `--out <path>`, `--cpu-n N`, `--gpu-n N`

Options for `sustained`:
- `--duration SEC` (default 600), `--out <path>`. Ctrl-C to stop early.

## For reproducible scores

Two system-level settings matter:

1. **Run as administrator** — required for LibreHardwareMonitor to read RAPL MSRs (CPU package power).
2. **Enable Windows Developer Mode** — required for `ID3D12Device::SetStablePowerState()` to lock GPU clocks. Without this, GPU scores have ~10–30 % run-to-run variance from DVFS. Turn it on in *Settings → System → For developers → Developer Mode*.

Without these, the tool still runs; it just warns and produces noisier numbers.

## What's measured

| Benchmark | Description | Metric |
|---|---|---|
| `cpu.sgemm.fp32.1024` | 1024³ dense FP32 matmul with `System.Numerics.Vector<float>` SIMD, parallelized | GFLOPS |
| `cpu.aes128cbc` | AES-128-CBC encryption of a 16 MB buffer (hits AES-NI when available) | MB/s |
| `cpu.sha256` | SHA-256 of a 16 MB buffer (hits SHA-NI on Intel Goldmont+/AMD Zen+) | MB/s |
| `cpu.zstd.level3` | Zstd level-3 compression over a 4 MB semi-compressible buffer | MB/s |
| `cpu.fft.c2c.4096` | 1D complex Cooley-Tukey radix-2 FFT, n=4096, in-place | MFLOPS |
| `cpu.mandelbrot.2048` | 2048×2048, max 256 iter, `Vector<double>` SIMD, parallelized | Mpix/s |
| `cpu.stream.triad` | McCalpin STREAM Triad, arrays 32 MB each (beyond L3) | GB/s |
| `cpu.latency.pointerchase` | Single-threaded 64-byte-stride shuffled linked-list chase over 32 MB | ns/access |
| `cpu.scaling.efficiency` | Sweeps SGEMM at 1, 2, 4, ..., physical_cores threads. Reports the GFLOPS curve and a single "efficiency at full cores" % | % |
| `gpu.matmul.fp32.2048` | 2048³ D3D12 compute-shader matmul with timestamp-query timing | GFLOPS |
| `gpu.matmul.fp16.2048` | Same matmul with `min16float` — shows FP16 vs FP32 speedup (if the HW supports packed FP16) | GFLOPS |
| `gpu.ai.squeezenet` | SqueezeNet 1.0 inference via ONNX Runtime + DirectML (runs on any Windows GPU/NPU) | inf/s |

Each run records telemetry (CPU/GPU watts + temps + CPU clock) every 100 ms, summarized into the JSON result.

## Tech stack

- .NET 8 / C#
- D3D12 via [Vortice.Windows](https://github.com/amerkoleci/Vortice.Windows)
- CPU/GPU/power telemetry via [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor)
- Environment capture via WMI (`Win32_Processor`, `Win32_VideoController`, `Win32_PhysicalMemory`)

## Roadmap

- ~~**M1** — Walking skeleton (1 CPU kernel + 1 GPU kernel, JSON pipeline, CLI)~~ ✅
- ~~**M2** — Full CPU suite (AES-NI, SHA-256, Zstd, FFT, Mandelbrot, STREAM Triad, pointer-chase)~~ ✅
- ~~**M3** — GPU suite + AI (FP16 matmul, ONNX + DirectML SqueezeNet inference)~~ ✅
- ~~**M4** — Scoring: baseline-normalized per-benchmark scores + geomean CPU/GPU subscores + overall~~ ✅
- ~~**M5** — Sustained/throttle test (configurable-duration loop, first-window vs last-window verdict, live chart in GUI)~~ ✅
- ~~**M6** — Avalonia GUI frontend (pulled forward)~~ ✅
- ~~**M4 polish** — Self-contained HTML report (auto-generated alongside JSON; CLI `report` subcommand with `--anonymize`)~~ ✅
- ~~**Compare** — `lapassay compare a.json b.json` diffs two runs side-by-side, console + HTML diff with per-benchmark Δ% and direction-aware score deltas~~ ✅
- ~~**Per-category subscores** — every run now reports `cpu.integer`, `cpu.float`, `cpu.memory`, `gpu.compute`, `gpu.ai` subscores in the CLI summary, HTML hero (chips), and GUI score card~~ ✅
- ~~**GUI compare picker** — "Compare runs…" button in the Single-run controls; pick any two JSON files, opens HTML diff in browser~~ ✅
- ~~**History dashboard** — new GUI tab scanning `results/`. Trend chart of overall/CPU/GPU score over runs, sortable list with `Δ vs prev` per row, multi-select two rows + "Compare selected" → HTML diff~~ ✅
- ~~**Per-core scaling** — sweeps SGEMM at 1, 2, 4, ..., N threads, reports GFLOPS curve + ideal-linear reference + scaling efficiency %. New `cpu.parallel` category in scoring. HTML report shows inline SVG scaling chart + per-step table~~ ✅
- ~~**Live single-run telemetry chart** — Single-run tab now has a live timeline of CPU/GPU watts and temps that streams as kernels run~~ ✅
- ~~**Battery vs AC auto-compare** — new GUI tab that detects current power state, runs full suite, walks the user through unplugging/plugging in, runs the second suite, generates a side-by-side HTML diff. Polls `Win32_Battery.BatteryStatus` once per 1.5s to detect the swap automatically.~~ ✅
