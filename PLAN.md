# ReelPress — Project Plan

## Scope

A cross-platform (Windows 10/11 + macOS) desktop video utility that performs the common,
practical video/audio chores through a **stackable FFmpeg-powered operation pipeline** with
**batch processing**, live preview, and saveable recipes. Offline and privacy-first by default;
optional local-AI assistance that never leaves the machine.

**In scope**

- Trim/cut (stream-copy when possible; re-encode when needed)
- Convert containers/codecs (MP4/MKV/WebM/MOV; H.264/H.265/VP9/AV1; audio AAC/Opus/MP3)
- Compress (CRF/quality **and** target-file-size via bitrate estimation)
- Resize/rescale (presets 1080p/720p/480p + custom, aspect-ratio safe, pad/crop options)
- Merge/concatenate multiple clips
- Extract audio (MP3/AAC/WAV/FLAC) and mute
- Extract frames (interval or single timestamp; PNG/JPG)
- Export GIF / animated WebP (range, fps, width, palette optimization)
- Batch across a folder with a saved JSON recipe
- Headless CLI mirroring GUI operations
- Optional local-AI: smart thumbnail/poster frame, auto-title/filename

**Non-goals** (see below)

## Architecture / tech approach

- **Language/runtime:** .NET 8.
- **UI:** **Avalonia (MVVM)** — chosen for true cross-platform Windows + macOS support
  (WPF is Windows-only and was rejected for that reason).
- **Core library `ReelPress.Core` (UI-free, unit-tested):**
  - `IFfmpegEngine` / `FfmpegEngine` — wraps the bundled `ffmpeg`/`ffprobe` binaries,
    builds arg lists, streams progress (parses `-progress pipe:` / stderr time), supports
    cancellation. No shell string interpolation — args passed as arrays.
  - `IMediaProbe` / `MediaProbe` — `ffprobe` JSON → normalized `MediaInfo`
    (streams, codecs, duration, resolution, fps, bitrate, audio channels).
  - `IVideoOperation` pipeline — each op (`Trim`, `Convert`, `Compress`, `Resize`,
    `Merge`, `ExtractAudio`, `ExtractFrames`, `ExportGif`, `Mute`) contributes filter/args
    and validates against `MediaInfo`. Ops compose into a single ffmpeg invocation where
    feasible, else chained temp stages.
  - `PipelineRunner` — bounded-concurrency batch executor with progress, cancellation,
    per-item `BatchResult` (success/skip/fail + output path + log tail).
  - `IRecipeStore` — JSON recipes/presets under `%APPDATA%\reelpress` /
    `~/Library/Application Support/reelpress`.
  - `IVideoAiService` — optional; talks to Ollama/llama.cpp OpenAI-compatible localhost
    endpoint. Reachability probe + deterministic fallback. Off by default.
- **FFmpeg distribution:** bundle per-OS static `ffmpeg`/`ffprobe` binaries (win-x64,
  osx-arm64/x64) resolved at runtime; document the licensing (LGPL/GPL build choice).
- **Testing:** xUnit against `ReelPress.Core` (arg-builder unit tests + probe-parse fixtures;
  a small integration suite generates a tiny test clip via ffmpeg `testsrc`).
- **CLI:** `reelpress` console project sharing `ReelPress.Core`; verbs `run/trim/convert/
  compress/resize/merge/audio/frames/gif/probe` with `--json` output and scripting exit codes.

## Milestones

1. **M1 — Core engine foundation:** `IFfmpegEngine`, `IMediaProbe`, `MediaInfo` model,
   binary resolution, progress/cancel plumbing, arg-array safety. Unit tests.
2. **M2 — Core operations:** Trim (copy + re-encode), Convert, Compress (CRF + target-size),
   Resize. Validation + tests.
3. **M3 — Extraction/export:** Extract audio, Mute, Extract frames, Export GIF/animated WebP
   (two-pass palette). Merge/concatenate.
4. **M4 — Desktop UI:** Avalonia shell, drag-drop intake, pipeline builder, batch queue,
   live before/after preview + estimated output size, progress/cancel.
5. **M5 — CLI + recipes:** `reelpress` verbs, JSON recipe load/save, presets.
6. **M6 — Optional local-AI:** `IVideoAiService` smart thumbnail + auto-title, probe +
   graceful fallback, off by default, local-only.
7. **M7 — Packaging & CI:** Windows portable zip + MSIX, macOS universal `.app`/`.dmg`,
   GitHub Actions matrix (windows-latest + macos-latest), bundle ffmpeg runtimes.

## Non-goals

- Full non-linear timeline editing, multi-track compositing, transitions, or color grading.
- Screen/webcam **recording** (capture) — this is a processing tool, not a recorder.
- Cloud upload, accounts, or any required network service for core value.
- Streaming/broadcast (RTMP) or DRM-protected content handling.
- Hardware-encoder tuning beyond opportunistic NVENC/VideoToolbox use where available.

## Packaging / distribution target

- **Windows 10/11:** self-contained win-x64 portable **zip** + **MSIX** installer.
- **macOS:** universal (arm64 + x64) **.app** bundled into a **.dmg** (notarization deferred
  past early milestones; document right-click-Open workaround).
- **CI:** GitHub Actions build/test matrix on `windows-latest` + `macos-latest`, artifacts
  attached to tagged releases; bundle the correct ffmpeg/ffprobe binaries per OS.
