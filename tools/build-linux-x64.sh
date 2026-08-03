#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd -- "$script_dir/.." && pwd)"
project_root="$repo_root/godot"
export_directory="$repo_root/build/export/linux-x64"
expected_export_directory="$repo_root/build/export/linux-x64"
package_directory="$repo_root/build/package"
managed_publish_directory="$repo_root/build/managed-publish/linux-x64"
managed_proxy_directory="$repo_root/build/managed-publish-proxy/linux-x64"
dotnet_proxy_directory="$repo_root/build/dotnet-export-proxy"
toolchain_root="$repo_root/build/toolchains/godot-4.7-stable-mono-linux-x64"
xdg_data_home="$toolchain_root/xdg-data"

for command in python3 dotnet cc readelf ldd curl sha256sum tar; do
    command -v "$command" >/dev/null 2>&1 || {
        echo "required command was not found: $command" >&2
        exit 1
    }
done

python3 -X utf8 "$script_dir/opcodes_build.py" --build
godot_console="$("$script_dir/bootstrap-godot-linux-x64.sh")"
ffmpeg_sdk="$("$repo_root/native/age_movie_ffmpeg/bootstrap-linux-x64.sh")"
"$repo_root/native/age_movie_ffmpeg/build-linux-x64.sh" "$ffmpeg_sdk"

# Godot keeps the editor resident while it launches dotnet publish. On constrained CI runners, that process
# pair can exceed the job's memory cgroup. Produce the exact ExportRelease/linux-x64 payload first, without
# compiler/build servers, so the memory-heavy compile and editor stages run sequentially.
expected_managed_publish_directory="$repo_root/build/managed-publish/linux-x64"
if [[ "$managed_publish_directory" != "$expected_managed_publish_directory" || "$managed_publish_directory" == "/" ]]; then
    echo "refusing to replace unexpected managed publish directory: $managed_publish_directory" >&2
    exit 1
fi
rm -rf -- "$managed_publish_directory"
mkdir -p -- "$managed_publish_directory"
DOTNET_CLI_USE_MSBUILD_SERVER=0 \
MSBUILDDISABLENODEREUSE=1 \
DOTNET_gcServer=0 \
DOTNET_GCConserveMemory=9 \
dotnet publish "$project_root/Himegari.csproj" \
    --configuration ExportRelease \
    --runtime linux-x64 \
    --self-contained true \
    --output "$managed_publish_directory" \
    -p:GodotTargetPlatform=linuxbsd \
    -p:UseSharedCompilation=false \
    -p:BuildInParallel=false \
    -p:RestoreDisableParallel=true \
    -p:DebugType=None \
    -p:DebugSymbols=false

# With dotnet/embed_build_outputs=false, Godot places publish files outside the PCK. Give the editor only the
# required profile assembly while it creates the real PCK, then stage the complete prepublish after the editor
# exits. This avoids making the memory-heavy editor traverse/hash the self-contained runtime and FFmpeg bundle.
expected_managed_proxy_directory="$repo_root/build/managed-publish-proxy/linux-x64"
if [[ "$managed_proxy_directory" != "$expected_managed_proxy_directory" || "$managed_proxy_directory" == "/" ]]; then
    echo "refusing to replace unexpected managed proxy directory: $managed_proxy_directory" >&2
    exit 1
fi
rm -rf -- "$managed_proxy_directory"
mkdir -p -- "$managed_proxy_directory"
cp -- "$managed_publish_directory/Himegari.dll" "$managed_proxy_directory/Himegari.dll"

real_dotnet="$(command -v dotnet)"
expected_dotnet_proxy_directory="$repo_root/build/dotnet-export-proxy"
if [[ "$dotnet_proxy_directory" != "$expected_dotnet_proxy_directory" || "$dotnet_proxy_directory" == "/" ]]; then
    echo "refusing to replace unexpected dotnet proxy directory: $dotnet_proxy_directory" >&2
    exit 1
fi
rm -rf -- "$dotnet_proxy_directory"
mkdir -p -- "$dotnet_proxy_directory"
cp -- "$script_dir/dotnet_publish_proxy.py" "$dotnet_proxy_directory/dotnet"
chmod +x "$dotnet_proxy_directory/dotnet"

if [[ "$export_directory" != "$expected_export_directory" || "$export_directory" == "/" ]]; then
    echo "refusing to replace unexpected export directory: $export_directory" >&2
    exit 1
fi
rm -rf -- "$export_directory"
mkdir -p -- "$export_directory"

set +e
DOTNET_CLI_USE_MSBUILD_SERVER=0 \
MSBUILDDISABLENODEREUSE=1 \
UseSharedCompilation=false \
BuildInParallel=false \
RestoreDisableParallel=true \
DOTNET_gcServer=0 \
DOTNET_GCConserveMemory=9 \
AGE_REAL_DOTNET="$real_dotnet" \
AGE_PUBLISH_PROJECT="$project_root/Himegari.csproj" \
AGE_PREPUBLISHED_OUTPUT="$managed_proxy_directory" \
AGE_PUBLISH_OUTPUT_ROOT="${TMPDIR:-/tmp}/godot-publish-dotnet" \
AGE_PUBLISH_ASSEMBLY="Himegari.dll" \
PATH="$dotnet_proxy_directory:$PATH" \
XDG_DATA_HOME="$xdg_data_home" "$godot_console" \
    --headless --quit-after 120 --path "$project_root" \
    --export-release "Linux x86_64" "$export_directory/Himegari.x86_64"
export_status=$?
set -e
if [[ $export_status -ne 0 ]]; then
    if [[ $export_status -eq 137 ]]; then
        echo "Godot export was killed with SIGKILL (137); inspect the runner memory cgroup diagnostics." >&2
    fi
    exit "$export_status"
fi

managed_export_directory="$export_directory/data_Himegari_linuxbsd_x86_64"
expected_managed_export_directory="$repo_root/build/export/linux-x64/data_Himegari_linuxbsd_x86_64"
if [[ "$managed_export_directory" != "$expected_managed_export_directory" || "$managed_export_directory" == "/" ]]; then
    echo "refusing to replace unexpected managed export directory: $managed_export_directory" >&2
    exit 1
fi
rm -rf -- "$managed_export_directory"
mkdir -p -- "$managed_export_directory"
cp -a -- "$managed_publish_directory/." "$managed_export_directory/"

python3 -X utf8 "$script_dir/package_linux_x64.py" verify "$export_directory"
mapfile -t package_outputs < <(
    python3 -X utf8 "$script_dir/package_linux_x64.py" package "$export_directory" \
        --output-directory "$package_directory"
)
if [[ ${#package_outputs[@]} -ne 2 ]]; then
    echo "packager returned an unexpected result" >&2
    exit 1
fi
package_root="${package_outputs[0]}"
archive_path="${package_outputs[1]}"
smoke_log="$package_directory/package-smoke.log"
set +e
"$package_root/Himegari.x86_64" --headless -- --package-smoke >"$smoke_log" 2>&1
smoke_status=$?
set -e
cat "$smoke_log"
if [[ $smoke_status -ne 0 ]]; then
    echo "packaged Linux runtime smoke failed with exit code $smoke_status" >&2
    exit "$smoke_status"
fi
if ! grep -Fq "PACKAGE SMOKE OK: opcodes=548 ffmpeg-abi=3" "$smoke_log"; then
    echo "packaged Linux runtime did not report its success marker" >&2
    exit 1
fi

archive_hash="$(sha256sum "$archive_path" | awk '{ print $1 }')"
printf 'Linux x64 package: %s\nSHA-256: %s\n' "$archive_path" "$archive_hash"
