# Packaging and releases

ReelPress ships self-contained .NET 8 desktop builds with pinned FFmpeg/FFprobe
executables. GitHub Actions is the supported release environment because the
native installers must be built on their target operating system.

## Outputs

| Platform | Command | Outputs |
| --- | --- | --- |
| Windows x64 | `./packaging/windows/package.ps1 -OutputDirectory ./artifacts -PackageVersion 1.2.3.0` | `reelpress-win-x64.zip`, signed `ReelPress_1.2.3.0_x64.msix`, public `ReelPress-test.cer` |
| macOS x64 + arm64 | `./packaging/macos/package.sh ./artifacts 1.2.3` | universal `ReelPress.app` inside `ReelPress.dmg` |

The Windows package script requires the Windows 10/11 SDK (`makeappx.exe` and
`signtool.exe`), PowerShell, Python 3, and the .NET 8 SDK. The macOS script
requires Xcode command-line tools, Python 3, and the .NET 8 SDK.

Both scripts:

1. Publish ReelPress as a self-contained desktop app.
2. Download the pinned FFmpeg b6.1.1 binaries.
3. Verify every download against a committed SHA-256 digest before packaging.
4. Put binaries under `runtimes/<rid>/native`, where `FfmpegBinaryResolver`
   discovers them before consulting `PATH`.
5. Smoke-test the bundled FFmpeg and FFprobe binaries.

The macOS script publishes complete `osx-x64` and `osx-arm64` payloads and
builds a universal Mach-O launcher that selects the native payload, then ad-hoc
signs the resulting app bundle. Complete payloads are required because the
self-contained .NET assemblies contain architecture-specific runtime code.
The desktop project uses Avalonia 11.3.20 because its SkiaSharp/HarfBuzzSharp
macOS native assets include both x64 and arm64 slices.
It builds a compressed DMG with an Applications shortcut. Notarization and an
Apple Developer ID signature are intentionally deferred. On first launch,
right-click **ReelPress.app**, choose **Open**, then confirm **Open**.

### Windows test certificate

The CI-created MSIX is signed with an ephemeral self-signed test certificate.
To install it, import the adjacent `ReelPress-test.cer` into **Local
Machine → Trusted People** (or Current User → Trusted People), then open the
MSIX. Production releases should replace this step with a stable, protected
code-signing certificate. Because the fallback certificate changes on every
build, test-signed MSIX upgrades may require uninstalling the previous build
and trusting the new certificate; the portable zip is the recommended early
release format.

## CI and tagged releases

`.github/workflows/ci-release.yml` runs all xUnit projects on
`windows-latest` and `macos-latest`, with the pinned FFmpeg build on `PATH` so
Core integration tests execute rather than silently skip. Once both matrix
legs pass, target-native jobs build and verify the zip, MSIX, and DMG.

Push an exact semantic tag such as `v1.2.3` to create/update the matching GitHub
Release and attach:

- `reelpress-win-x64.zip`
- `ReelPress_1.2.3.0_x64.msix`
- `ReelPress-test.cer`
- `ReelPress.dmg`

## FFmpeg licensing

The pinned static build enables GPL components such as x264/x265 and is
therefore treated as **GPLv3**, not LGPL. The installers contain the GPLv3
license and [`THIRD-PARTY-NOTICES.md`](./THIRD-PARTY-NOTICES.md), including
links to the exact binary release, build recipes, and FFmpeg source. ReelPress
itself remains a separate MIT-licensed application. Review the build's
configure flags and redistribution terms before changing the FFmpeg source.
