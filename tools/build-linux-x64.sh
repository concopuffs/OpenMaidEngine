#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd -- "$script_dir/.." && pwd)"
project_root="$repo_root/godot"
export_directory="$repo_root/build/export/linux-x64"
expected_export_directory="$repo_root/build/export/linux-x64"
package_directory="$repo_root/build/package"
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

if [[ "$export_directory" != "$expected_export_directory" || "$export_directory" == "/" ]]; then
    echo "refusing to replace unexpected export directory: $export_directory" >&2
    exit 1
fi
rm -rf -- "$export_directory"
mkdir -p -- "$export_directory"

XDG_DATA_HOME="$xdg_data_home" "$godot_console" \
    --headless --quit-after 120 --path "$project_root" \
    --export-release "Linux x86_64" "$export_directory/Himegari.x86_64"

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
