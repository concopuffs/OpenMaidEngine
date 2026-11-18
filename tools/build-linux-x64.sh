#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd -- "$script_dir/.." && pwd)"
project_root="$repo_root/godot"
export_directory="$repo_root/build/export/linux-x64"
expected_export_directory="$repo_root/build/export/linux-x64"
package_directory="$repo_root/build/package"
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
ffmpeg_sdk="$("$repo_root/native/ome_movie_ffmpeg/bootstrap-linux-x64.sh")"
"$repo_root/native/ome_movie_ffmpeg/build-linux-x64.sh" "$ffmpeg_sdk"

# The engine is one NativeAOT library (docs/platform-portability.md, "Fewer files in a package"). NativeAOT compiles
# for its own system only, so tools/publish_engine.py builds it beforehand: in the release workflow's own job for this
# target, or by hand on a matching system. Take only a publish of this checkout's commit.
engine_publish_directory="$repo_root/build/engine-publish/linux-x64"
python3 -X utf8 "$script_dir/publish_engine.py" --runtime linux-x64 --check

# With dotnet/embed_build_outputs=false, Godot places publish files outside the PCK. Godot's export publishes the
# project once more: the proxy hands it only the library while it creates the real PCK, and the data folder is staged
# after the editor exits.
expected_managed_proxy_directory="$repo_root/build/managed-publish-proxy/linux-x64"
if [[ "$managed_proxy_directory" != "$expected_managed_proxy_directory" || "$managed_proxy_directory" == "/" ]]; then
    echo "refusing to replace unexpected managed proxy directory: $managed_proxy_directory" >&2
    exit 1
fi
rm -rf -- "$managed_proxy_directory"
mkdir -p -- "$managed_proxy_directory"
cp -- "$engine_publish_directory/OME.so" "$managed_proxy_directory/OME.so"

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
OME_REAL_DOTNET="$real_dotnet" \
OME_PUBLISH_PROJECT="$project_root/OME.csproj" \
OME_PREPUBLISHED_OUTPUT="$managed_proxy_directory" \
OME_PUBLISH_OUTPUT_ROOT="${TMPDIR:-/tmp}/godot-publish-dotnet" \
OME_PUBLISH_ASSEMBLY="OME.so" \
OME_PUBLISH_RUNTIME="linux-x64" \
PATH="$dotnet_proxy_directory:$PATH" \
XDG_DATA_HOME="$xdg_data_home" "$godot_console" \
    --headless --quit-after 120 --path "$project_root" \
    --export-release "Linux x86_64" "$export_directory/OME"
export_status=$?
set -e
if [[ $export_status -ne 0 ]]; then
    if [[ $export_status -eq 137 ]]; then
        echo "Godot export was killed with SIGKILL (137); inspect the runner memory cgroup diagnostics." >&2
    fi
    exit "$export_status"
fi

managed_export_directory="$export_directory/data_OME_linuxbsd_x86_64"
expected_managed_export_directory="$repo_root/build/export/linux-x64/data_OME_linuxbsd_x86_64"
if [[ "$managed_export_directory" != "$expected_managed_export_directory" || "$managed_export_directory" == "/" ]]; then
    echo "refusing to replace unexpected managed export directory: $managed_export_directory" >&2
    exit 1
fi
rm -rf -- "$managed_export_directory"
mkdir -p -- "$managed_export_directory"
cp -a -- "$engine_publish_directory/." "$managed_export_directory/"
# The FFmpeg shim and its libraries, which this job built (native/ome_movie_ffmpeg/build-linux-x64.sh).
cp -a -- "$repo_root/build/native/linux-x64/"*.so* "$repo_root/build/native/linux-x64/FFmpeg-LICENSE.txt" \
    "$repo_root/build/native/linux-x64/FFmpeg-SOURCE.txt" "$managed_export_directory/"

# The launcher beside the engine (launcher plan §9.2): its own publish, a step of its own after Godot's export has
# exited, so their memory peaks never overlap on the runner (§3.7).
python3 -X utf8 "$script_dir/publish_launcher.py" --runtime linux-x64 --into "$export_directory"

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
# The smoke holds until its standard input closes; its line must reach the pipe while it runs (launcher plan §4.12).
python3 -X utf8 "$script_dir/package_linux_x64.py" smoke "$package_root" --log "$smoke_log"
smoke_status=$?
set -e
cat "$smoke_log"
if [[ $smoke_status -ne 0 ]]; then
    echo "packaged Linux runtime smoke failed with exit code $smoke_status" >&2
    exit "$smoke_status"
fi
# The engine's smoke, the launcher's check with no display, and the hand-over (launcher plan §9.4).
for marker in "PACKAGE SMOKE OK: opcodes=548 ffmpeg-abi=3" "LAUNCHER CHECK OK: checks=6" "HAND-OVER OK"; do
    if ! grep -Fq "$marker" "$smoke_log"; then
        echo "packaged Linux runtime did not report its success marker: $marker" >&2
        exit 1
    fi
done

archive_hash="$(sha256sum "$archive_path" | awk '{ print $1 }')"
printf 'Linux x64 package: %s\nSHA-256: %s\n' "$archive_path" "$archive_hash"
