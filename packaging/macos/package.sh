#!/usr/bin/env bash
set -euo pipefail

output_directory="${1:-artifacts}"
version="${2:-0.1.0}"

if [[ ! "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "Version must use three numeric components (for example 1.2.3)." >&2
  exit 2
fi

script_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_directory/../.." && pwd)"
if [[ "$output_directory" != /* ]]; then
  output_directory="$repo_root/$output_directory"
fi
mkdir -p "$output_directory"

release_url="https://github.com/eugeneware/ffmpeg-static/releases/download/b6.1.1"
temp_root="$(mktemp -d "${TMPDIR:-/tmp}/reelpress-package.XXXXXX")"
trap 'rm -rf "$temp_root"' EXIT

verified_download() {
  local url="$1"
  local destination="$2"
  local expected_sha="$3"
  curl --fail --location --retry 3 "$url" --output "$destination"
  echo "$expected_sha  $destination" | shasum -a 256 --check
}

x64_publish="$temp_root/publish-osx-x64"
arm64_publish="$temp_root/publish-osx-arm64"

pushd "$repo_root" >/dev/null
dotnet publish src/ReelPress.Desktop/ReelPress.Desktop.csproj \
  --configuration Release \
  --runtime osx-x64 \
  --self-contained true \
  --output "$x64_publish" \
  -p:DebugType=None \
  -p:DebugSymbols=false
dotnet publish src/ReelPress.Desktop/ReelPress.Desktop.csproj \
  --configuration Release \
  --runtime osx-arm64 \
  --self-contained true \
  --output "$arm64_publish" \
  -p:DebugType=None \
  -p:DebugSymbols=false
popd >/dev/null

app_root="$temp_root/ReelPress.app"
macos_root="$app_root/Contents/MacOS"
resources_root="$app_root/Contents/Resources"
mkdir -p "$macos_root" "$resources_root/licenses"
ditto "$x64_publish" "$macos_root"

# Merge every architecture-specific Mach-O pair from the two self-contained
# publishes. Managed assemblies and architecture-neutral resources are shared.
while IFS= read -r -d '' arm_file; do
  relative_path="${arm_file#"$arm64_publish"/}"
  x64_file="$x64_publish/$relative_path"
  target_file="$macos_root/$relative_path"

  if [[ -f "$x64_file" ]] \
      && file "$x64_file" | grep -q 'Mach-O' \
      && file "$arm_file" | grep -q 'Mach-O'; then
    merged_file="$temp_root/merged-$(printf '%s' "$relative_path" | shasum -a 256 | cut -d' ' -f1)"
    lipo -create "$x64_file" "$arm_file" -output "$merged_file"
    chmod +x "$merged_file"
    mv "$merged_file" "$target_file"
  elif [[ ! -e "$target_file" ]]; then
    mkdir -p "$(dirname "$target_file")"
    cp -p "$arm_file" "$target_file"
  fi
done < <(find "$arm64_publish" -type f -print0)

for arch in x64 arm64; do
  runtime_root="$macos_root/runtimes/osx-$arch/native"
  mkdir -p "$runtime_root"
  if [[ "$arch" == "x64" ]]; then
    ffmpeg_sha="ebdddc936f61e14049a2d4b549a412b8a40deeff6540e58a9f2a2da9e6b18894"
    ffprobe_sha="fa3add0ce901f7241abe0dfc0155d958fc834aca3f8ce61f87cc712ae669c1e0"
  else
    ffmpeg_sha="a90e3db6a3fd35f6074b013f948b1aa45b31c6375489d39e572bea3f18336584"
    ffprobe_sha="bb2db6f5d8cef919da12fbf592119a987202a8c060a886f3cab091f9cab90b64"
  fi
  verified_download "$release_url/ffmpeg-darwin-$arch" "$runtime_root/ffmpeg" "$ffmpeg_sha"
  verified_download "$release_url/ffprobe-darwin-$arch" "$runtime_root/ffprobe" "$ffprobe_sha"
  chmod +x "$runtime_root/ffmpeg" "$runtime_root/ffprobe"
done

verified_download \
  "$release_url/win32-x64.LICENSE" \
  "$resources_root/licenses/FFmpeg-GPLv3.txt" \
  "8ceb4b9ee5adedde47b31e975c1d90c73ad27b6b165a1dcd80c7c545eb65b903"
cp "$repo_root/LICENSE" "$resources_root/LICENSE.txt"
cp "$repo_root/packaging/THIRD-PARTY-NOTICES.md" "$resources_root/THIRD-PARTY-NOTICES.md"
cp "$repo_root/packaging/macos/Info.plist" "$app_root/Contents/Info.plist"
/usr/libexec/PlistBuddy -c "Set :CFBundleShortVersionString $version" "$app_root/Contents/Info.plist"
/usr/libexec/PlistBuddy -c "Set :CFBundleVersion $version" "$app_root/Contents/Info.plist"
plutil -lint "$app_root/Contents/Info.plist"

main_arches="$(lipo -archs "$macos_root/ReelPress.Desktop")"
[[ "$main_arches" == *x86_64* && "$main_arches" == *arm64* ]] || {
  echo "Universal app host verification failed: $main_arches" >&2
  exit 1
}
[[ "$(lipo -archs "$macos_root/runtimes/osx-x64/native/ffmpeg")" == *x86_64* ]]
[[ "$(lipo -archs "$macos_root/runtimes/osx-arm64/native/ffmpeg")" == *arm64* ]]

codesign --force --deep --sign - "$app_root"
codesign --verify --deep --strict "$app_root"

# Smoke-test the packaged application and native binaries for the runner's
# current architecture. The lipo checks above verify that the other app host
# is present even when the runner cannot execute it.
app_log="$temp_root/reelpress-smoke.log"
"$macos_root/ReelPress.Desktop" >"$app_log" 2>&1 &
app_pid=$!
sleep 5
if kill -0 "$app_pid" 2>/dev/null; then
  kill "$app_pid"
  wait "$app_pid" 2>/dev/null || true
else
  set +e
  wait "$app_pid"
  app_status=$?
  set -e
  if [[ "$app_status" -ne 0 ]]; then
    cat "$app_log" >&2
    echo "Packaged application smoke test exited with code $app_status." >&2
    exit 1
  fi
fi

if [[ "$(uname -m)" == "arm64" ]]; then
  ffmpeg_output="$("$macos_root/runtimes/osx-arm64/native/ffmpeg" -version)"
  ffprobe_output="$("$macos_root/runtimes/osx-arm64/native/ffprobe" -version)"
else
  ffmpeg_output="$("$macos_root/runtimes/osx-x64/native/ffmpeg" -version)"
  ffprobe_output="$("$macos_root/runtimes/osx-x64/native/ffprobe" -version)"
fi
printf '%s\n' "${ffmpeg_output%%$'\n'*}"
printf '%s\n' "${ffprobe_output%%$'\n'*}"

dmg_root="$temp_root/dmg"
mkdir -p "$dmg_root"
ditto "$app_root" "$dmg_root/ReelPress.app"
ln -s /Applications "$dmg_root/Applications"
dmg_path="$output_directory/ReelPress.dmg"
rm -f "$dmg_path"
hdiutil create -volname ReelPress -srcfolder "$dmg_root" -ov -format UDZO "$dmg_path"
hdiutil verify "$dmg_path"

echo "Created $dmg_path"
