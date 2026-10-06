#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd -- "$script_dir/../.." && pwd)"
destination="${1:-$repo_root/build/ffmpeg-sdk}"
manifest="$script_dir/dependency-win64.json"

manifest_value() {
    python3 -c 'import json, sys; print(json.load(open(sys.argv[1], encoding="utf-8"))[sys.argv[2]])' \
        "$manifest" "$1"
}

for command in python3 curl sha256sum awk wc; do
    command -v "$command" >/dev/null 2>&1 || {
        echo "required command was not found: $command" >&2
        exit 1
    }
done

archive="$(manifest_value archive)"
url="$(manifest_value url)"
expected_hash="$(manifest_value sha256)"
expected_size="$(manifest_value size)"
expected_version="$(manifest_value ffmpeg_version)"
archive_root="${archive%.zip}"
download_directory="$repo_root/build/downloads"
archive_path="$download_directory/$archive"
sdk_root="$destination/$archive_root"

mkdir -p -- "$download_directory" "$destination"
if [[ ! -f "$archive_path" ]]; then
    curl --fail --location --retry 3 --remove-on-error --output "$archive_path" "$url"
fi

actual_size="$(wc -c < "$archive_path")"
if [[ "$actual_size" != "$expected_size" ]]; then
    echo "FFmpeg archive size mismatch: expected $expected_size, got $actual_size" >&2
    exit 1
fi
actual_hash="$(sha256sum "$archive_path" | awk '{ print $1 }')"
if [[ "$actual_hash" != "$expected_hash" ]]; then
    echo "FFmpeg archive SHA-256 mismatch: expected $expected_hash, got $actual_hash" >&2
    exit 1
fi

if [[ ! -d "$sdk_root" ]]; then
    python3 -m zipfile -e "$archive_path" "$destination"
fi

for required in \
    "$sdk_root/include/libavformat/avformat.h" \
    "$sdk_root/include/libavutil/ffversion.h" \
    "$sdk_root/lib/libavformat.dll.a" \
    "$sdk_root/lib/libavcodec.dll.a" \
    "$sdk_root/lib/libavutil.dll.a" \
    "$sdk_root/lib/libswscale.dll.a" \
    "$sdk_root/lib/libswresample.dll.a" \
    "$sdk_root/LICENSE.txt"; do
    if [[ ! -e "$required" ]]; then
        echo "FFmpeg SDK file was not found after extraction: $required" >&2
        exit 1
    fi
done

# The SDK ships no programs; its installed version header is the authoritative version.
reported="$(sed -n 's/^#define FFMPEG_VERSION "\(.*\)"$/\1/p' "$sdk_root/include/libavutil/ffversion.h")"
if [[ "$reported" != "$expected_version" ]]; then
    echo "unexpected FFmpeg build: ${reported:-unknown} (expected $expected_version)" >&2
    exit 1
fi

printf '%s\n' "$sdk_root"
