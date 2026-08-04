#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd -- "$script_dir/.." && pwd)"
manifest="$script_dir/godot-linux-x64.json"
template_target="${1:-linux-x64}"
toolchain_root="$repo_root/build/toolchains/godot-4.7-stable-mono-linux-x64"
xdg_data_home="$toolchain_root/xdg-data"
download_dir="$repo_root/build/downloads"

manifest_value() {
    python3 -c 'import json, sys; from functools import reduce; data=json.load(open(sys.argv[1], encoding="utf-8")); print(reduce(lambda value, key: value[key], sys.argv[2:], data))' \
        "$manifest" "$@"
}

for command in python3 curl sha256sum find awk; do
    command -v "$command" >/dev/null 2>&1 || {
        echo "required command was not found: $command" >&2
        exit 1
    }
done

archive="$(manifest_value editor archive)"
url="$(manifest_value editor url)"
expected_hash="$(manifest_value editor sha256)"
expected_size="$(manifest_value editor size)"
executable_name="$(manifest_value editor executable)"
template_version="$(manifest_value godot_version)"
archive_path="$download_dir/$archive"
editor_root="$toolchain_root/editor"
template_root="$xdg_data_home/godot/export_templates/$template_version"

case "$template_target" in
    linux-x64) template_name="linux_release.x86_64" ;;
    windows-x64) template_name="windows_release_x86_64.exe" ;;
    *)
        echo "unsupported Godot template target: $template_target" >&2
        exit 2
        ;;
esac

mkdir -p -- "$download_dir" "$editor_root" "$template_root"
if [[ ! -f "$archive_path" ]]; then
    curl --fail --location --retry 3 --output "$archive_path" "$url"
fi
actual_size="$(wc -c < "$archive_path")"
if [[ "$actual_size" != "$expected_size" ]]; then
    echo "Godot editor archive size mismatch: expected $expected_size, got $actual_size" >&2
    exit 1
fi
actual_hash="$(sha256sum "$archive_path" | awk '{ print $1 }')"
if [[ "$actual_hash" != "$expected_hash" ]]; then
    echo "Godot editor archive SHA-256 mismatch: expected $expected_hash, got $actual_hash" >&2
    exit 1
fi

mapfile -t editors < <(find "$editor_root" -type f -name "$executable_name" -print)
if [[ ${#editors[@]} -eq 0 ]]; then
    python3 -c 'import pathlib, sys, zipfile; zipfile.ZipFile(sys.argv[1]).extractall(pathlib.Path(sys.argv[2]))' \
        "$archive_path" "$editor_root"
    mapfile -t editors < <(find "$editor_root" -type f -name "$executable_name" -print)
fi
if [[ ${#editors[@]} -ne 1 ]]; then
    echo "expected exactly one $executable_name under $editor_root, found ${#editors[@]}" >&2
    exit 1
fi
editor="${editors[0]}"
chmod +x "$editor"

python3 -X utf8 "$script_dir/install_godot_templates.py" \
    --manifest "$manifest" --destination "$template_root" --member "$template_name" >&2

reported="$(XDG_DATA_HOME="$xdg_data_home" "$editor" --headless --version)"
reported="${reported%%$'\n'*}"
if [[ "$reported" != 4.7.stable.mono* ]]; then
    echo "unexpected Godot editor version: $reported" >&2
    exit 1
fi

printf '%s\n' "$editor"
