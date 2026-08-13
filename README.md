# ReelPress

**Local, privacy-first desktop video toolkit for Windows 10/11 & macOS.**

Trim, convert, compress, resize, merge, extract audio & frames, and export GIFs — all through a stackable, FFmpeg-powered operation pipeline with live preview and batch processing. Offline by default, no cloud, no account.

---

## Overview

ReelPress is a small, focused desktop app for the everyday video chores that shouldn't require a heavyweight editor or a sketchy web "converter." You drop in one file or a whole folder, stack a few operations (resize → compress → convert), preview the result, and run the batch. Everything happens locally on your machine using a bundled [FFmpeg](https://ffmpeg.org/) engine.

It is the video-focused sibling of the images toolkit `pixel-press` — same philosophy (stackable pipeline, batch, live preview, offline), applied to video and audio.

## Motivation

- Online video converters are slow, size-limited, ad-riddled, and upload your private footage to someone else's server.
- Full editors (Premiere, DaVinci) are overkill for "make this MP4 smaller" or "grab a GIF from 0:12–0:15."
- FFmpeg is incredibly powerful but the CLI is intimidating for quick, repeatable jobs.

ReelPress wraps the power of FFmpeg in a friendly, batch-first desktop UI that keeps your files on your machine.

## Use cases

- **Shrink a video** to hit an email/Discord/upload size limit (target-size compression).
- **Convert formats** — MP4 ⇄ MKV ⇄ WebM ⇄ MOV, H.264/H.265/VP9/AV1.
- **Trim / cut** a clip to a start–end range without re-encoding (stream copy) when possible.
- **Resize / rescale** to 1080p/720p/480p or a custom resolution, with aspect-ratio safety.
- **Merge / concatenate** several clips into one.
- **Extract audio** to MP3/AAC/WAV/FLAC, or **mute** a video.
- **Extract frames** as PNG/JPG (every N seconds, or a single frame at a timestamp).
- **Export a GIF** or animated WebP from a clip range with fps/width control and palette optimization.
- **Batch-apply** any of the above across a folder with a saved recipe.

## How to use

### Windows 10/11 quickstart

1. Download the latest `reelpress-win-x64.zip` from Releases and unzip (portable), or install the MSIX.
2. Launch **ReelPress**. FFmpeg is bundled — nothing else to install.
3. Drag video files (or a folder) onto the window.
4. Add operations to the pipeline (e.g. *Resize → 720p*, then *Compress → target 25 MB*).
5. Review the live before/after preview and estimated output size.
6. Click **Run** — outputs are written next to your files (or to a chosen output folder).

### macOS quickstart

1. Download `ReelPress.dmg` from Releases, open it, and drag **ReelPress.app** to Applications.
2. On first launch, right-click → **Open** to clear Gatekeeper (unsigned during early milestones).
3. Same workflow: drag in files, stack operations, preview, **Run**.

### Example workflow (CLI)

A headless `reelpress` CLI ships alongside the GUI for scripting:

```bash
# Compress a folder of videos to ~20MB each, converting to H.265 MP4
reelpress run --recipe compress-20mb.json ./footage --out ./compressed

# Trim a clip without re-encoding (fast, lossless stream copy)
reelpress trim input.mp4 --start 00:00:12 --end 00:00:18 --copy --out clip.mp4

# Export an optimized GIF from a range
reelpress gif input.mp4 --start 00:01:05 --end 00:01:08 --fps 15 --width 480 --out demo.gif

# Extract audio to MP3
reelpress audio extract input.mp4 --format mp3 --out track.mp3
```

## Local-AI integration (optional, off by default)

ReelPress works fully in **non-AI mode**. When you opt in, it can talk to a local
[Ollama](https://ollama.com/) or [llama.cpp](https://github.com/ggerganov/llama.cpp)
server over the OpenAI-compatible endpoint at `http://localhost:11434` (or your configured host):

- **Smart thumbnail / poster frame** — pick the most representative or "interesting" frame using a tiny vision model (MiniCPM-V class).
- **Auto-titles & filenames** — suggest descriptive output names from sampled frames/metadata.
- **Chapter/scene hints** — lightweight scene-change summaries for long clips.

All AI is **local-only** and metadata/frame-sampled (no upload). ReelPress probes for a running
server first and **gracefully falls back** to deterministic rules (center frame, token-based naming)
when none is reachable. Recommended tiny models: MiniCPM-V, Llama 3.2, Qwen2.5, Phi-3-mini.

## Current status / milestones

🚧 **Early scaffolding.** See [PLAN.md](./PLAN.md) for the full roadmap.

- [ ] M1 — `ReelPress.Core`: FFmpeg engine wrapper, probe, operation model
- [ ] M2 — Core operations: trim, convert, compress, resize, merge
- [ ] M3 — Extraction: audio, frames, GIF/animated-WebP export
- [ ] M4 — Avalonia desktop UI: pipeline builder, batch queue, live preview
- [ ] M5 — `reelpress` CLI + JSON recipes
- [ ] M6 — Optional local-AI (smart thumbnail / auto-title)
- [ ] M7 — Packaging & CI (Windows zip/MSIX, macOS .app/.dmg)

## License

MIT — see [LICENSE](./LICENSE) once added.
