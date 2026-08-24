# Third-party notices

## FFmpeg / FFprobe

ReelPress distributions bundle FFmpeg and FFprobe from the pinned
[`eugeneware/ffmpeg-static` b6.1.1 release](https://github.com/eugeneware/ffmpeg-static/releases/tag/b6.1.1).
Those static builds are configured with GPL components (including x264/x265) and are
therefore distributed under **GNU GPL version 3**. The GPL applies to the bundled
FFmpeg executables, not to ReelPress's separate MIT-licensed application code.

- FFmpeg source: <https://github.com/FFmpeg/FFmpeg>
- Static-build source and recipes: <https://github.com/eugeneware/ffmpeg-static>
- Exact binary release: <https://github.com/eugeneware/ffmpeg-static/releases/tag/b6.1.1>
- FFmpeg licensing guide: <https://ffmpeg.org/legal.html>

Each installer also contains `licenses/FFmpeg-GPLv3.txt`. If the bundled build is
changed, its configure flags and redistribution terms must be reviewed before release.
