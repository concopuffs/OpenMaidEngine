#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd -- "$script_dir/.." && pwd)"
project_root="$repo_root/godot"
export_directory="$repo_root/build/export/windows-x64"
expected_export_directory="$repo_root/build/export/windows-x64"
package_directory="$repo_root/build/package/windows-x64"
managed_publish_directory="$repo_root/build/managed-publish/win-x64"
managed_proxy_directory="$repo_root/build/managed-publish-proxy/win-x64"
dotnet_proxy_directory="$repo_root/build/dotnet-export-proxy"
toolchain_root="$repo_root/build/toolchains/godot-4.7-stable-mono-linux-x64"
xdg_data_home="$toolchain_root/xdg-data"
compiler="${MINGW_CC:-x86_64-w64-mingw32-gcc}"
objdump="${MINGW_OBJDUMP:-x86_64-w64-mingw32-objdump}"

for command in python3 dotnet "$compiler" "$objdump" curl sha256sum awk; do
    command -v "$command" >/dev/null 2>&1 || {
        echo "required command was not found: $command" >&2
        exit 1
    }
done

python3 -X utf8 "$script_dir/opcodes_build.py" --build
godot_console="$("$script_dir/bootstrap-godot-linux-x64.sh" windows-x64)"
ffmpeg_sdk="$("$repo_root/native/age_movie_ffmpeg/bootstrap-win64.sh")"
"$repo_root/native/age_movie_ffmpeg/build-win64.sh" "$ffmpeg_sdk"

# Keep the memory-heavy self-contained publish outside the resident Godot editor, as on Linux.
expected_managed_publish_directory="$repo_root/build/managed-publish/win-x64"
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
    --runtime win-x64 \
    --self-contained true \
    --output "$managed_publish_directory" \
    -p:GodotTargetPlatform=windows \
    -p:UseSharedCompilation=false \
    -p:BuildInParallel=false \
    -p:RestoreDisableParallel=true \
    -p:DebugType=None \
    -p:DebugSymbols=false

expected_managed_proxy_directory="$repo_root/build/managed-publish-proxy/win-x64"
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
AGE_PUBLISH_RUNTIME="win-x64" \
PATH="$dotnet_proxy_directory:$PATH" \
XDG_DATA_HOME="$xdg_data_home" "$godot_console" \
    --headless --quit-after 120 --path "$project_root" \
    --export-release "Windows x86_64" "$export_directory/Himegari.exe"
export_status=$?
set -e
if [[ $export_status -ne 0 ]]; then
    if [[ $export_status -eq 137 ]]; then
        echo "Godot export was killed with SIGKILL (137); inspect the runner memory cgroup diagnostics." >&2
    fi
    exit "$export_status"
fi

managed_export_directory="$export_directory/data_Himegari_windows_x86_64"
expected_managed_export_directory="$repo_root/build/export/windows-x64/data_Himegari_windows_x86_64"
if [[ "$managed_export_directory" != "$expected_managed_export_directory" || "$managed_export_directory" == "/" ]]; then
    echo "refusing to replace unexpected managed export directory: $managed_export_directory" >&2
    exit 1
fi
rm -rf -- "$managed_export_directory"
mkdir -p -- "$managed_export_directory"
cp -a -- "$managed_publish_directory/." "$managed_export_directory/"

python3 -X utf8 "$script_dir/package_windows_x64.py" verify "$export_directory" --objdump "$objdump"
mapfile -t package_outputs < <(
    python3 -X utf8 "$script_dir/package_windows_x64.py" package "$export_directory" \
        --objdump "$objdump" --output-directory "$package_directory"
)
if [[ ${#package_outputs[@]} -ne 2 ]]; then
    echo "packager returned an unexpected result" >&2
    exit 1
fi
archive_path="${package_outputs[1]}"
archive_hash="$(sha256sum "$archive_path" | awk '{ print $1 }')"
printf 'Windows x64 package: %s\nSHA-256: %s\n' "$archive_path" "$archive_hash"
