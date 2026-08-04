#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 1 || $# -gt 2 ]]; then
    echo "usage: $0 <ffmpeg-sdk-root> [output-directory]" >&2
    exit 2
fi

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd -- "$script_dir/../.." && pwd)"
sdk_root="$(cd -- "$1" && pwd)"
output_directory_input="${2:-$repo_root/build/native/win-x64}"
mkdir -p -- "$output_directory_input"
output_directory="$(cd -- "$output_directory_input" && pwd)"
include_directory="$sdk_root/include"
library_directory="$sdk_root/lib"
binary_directory="$sdk_root/bin"
compiler="${MINGW_CC:-x86_64-w64-mingw32-gcc}"
objdump="${MINGW_OBJDUMP:-x86_64-w64-mingw32-objdump}"
shim="$output_directory/age_movie_ffmpeg.dll"

for command in "$compiler" "$objdump" python3; do
    command -v "$command" >/dev/null 2>&1 || {
        echo "required command was not found: $command" >&2
        exit 1
    }
done

for required in \
    "$include_directory/libavformat/avformat.h" \
    "$library_directory/libavformat.dll.a" \
    "$library_directory/libavcodec.dll.a" \
    "$library_directory/libavutil.dll.a" \
    "$library_directory/libswscale.dll.a" \
    "$library_directory/libswresample.dll.a" \
    "$sdk_root/LICENSE.txt"; do
    if [[ ! -e "$required" ]]; then
        echo "missing FFmpeg SDK file: $required" >&2
        exit 1
    fi
done

"$compiler" \
    -std=c11 -O2 -Wall -Wextra -static-libgcc \
    -I "$include_directory" \
    -shared -Wl,--no-undefined -Wl,--no-insert-timestamp \
    -o "$shim" "$script_dir/age_movie.c" \
    -L "$library_directory" \
    -lavformat -lavcodec -lavutil -lswscale -lswresample

runtime_libraries=(
    avformat-62.dll
    avcodec-62.dll
    avutil-60.dll
    swscale-9.dll
    swresample-6.dll
)
for runtime in "${runtime_libraries[@]}"; do
    source_library="$binary_directory/$runtime"
    if [[ ! -f "$source_library" ]]; then
        echo "missing FFmpeg runtime library: $source_library" >&2
        exit 1
    fi
    cp -- "$source_library" "$output_directory/$runtime"
done
cp -- "$sdk_root/LICENSE.txt" "$output_directory/FFmpeg-LICENSE.txt"

python3 -X utf8 "$repo_root/tools/verify_windows_native.py" \
    "$output_directory" \
    --objdump "$objdump" \
    --report "$output_directory/verification.json"

printf '%s\n' "$shim"
