#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 1 || $# -gt 2 ]]; then
    echo "usage: $0 <ffmpeg-sdk-root> [output-directory]" >&2
    exit 2
fi

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd -- "$script_dir/../.." && pwd)"
sdk_root="$(cd -- "$1" && pwd)"
output_directory_input="${2:-$repo_root/build/native/linux-x64}"
mkdir -p -- "$output_directory_input"
output_directory="$(cd -- "$output_directory_input" && pwd)"
include_directory="$sdk_root/include"
library_directory="$sdk_root/lib"
shim="$output_directory/libage_movie_ffmpeg.so"
manifest="$script_dir/dependency-linux-x64.json"

for command in cc readelf ldd python3 sort; do
    command -v "$command" >/dev/null 2>&1 || {
        echo "required command was not found: $command" >&2
        exit 1
    }
done

for required in \
    "$include_directory/libavformat/avformat.h" \
    "$library_directory/libavformat.so" \
    "$library_directory/libavcodec.so" \
    "$library_directory/libavutil.so" \
    "$library_directory/libswscale.so" \
    "$library_directory/libswresample.so" \
    "$sdk_root/LICENSE.txt"; do
    if [[ ! -e "$required" ]]; then
        echo "missing FFmpeg SDK file: $required" >&2
        exit 1
    fi
done

cc \
    -std=c11 -fPIC -fvisibility=hidden -O2 -Wall -Wextra \
    -I "$include_directory" \
    -shared -Wl,-z,defs -Wl,-z,origin -Wl,--enable-new-dtags -Wl,-rpath,'$ORIGIN' \
    -o "$shim" "$script_dir/age_movie.c" \
    -L "$library_directory" \
    -lavformat -lavcodec -lavutil -lswscale -lswresample

runtime_libraries=(
    libavformat.so.62
    libavcodec.so.62
    libavutil.so.60
    libswscale.so.9
    libswresample.so.6
)
for runtime in "${runtime_libraries[@]}"; do
    source_library="$library_directory/$runtime"
    if [[ ! -e "$source_library" ]]; then
        echo "missing FFmpeg runtime library: $source_library" >&2
        exit 1
    fi
    cp --dereference -- "$source_library" "$output_directory/$runtime"
done
cp -- "$sdk_root/LICENSE.txt" "$output_directory/FFmpeg-LICENSE.txt"

expected_outputs=(libage_movie_ffmpeg.so "${runtime_libraries[@]}")
shopt -s nullglob
for existing in "$output_directory"/*.so*; do
    existing_name="$(basename -- "$existing")"
    expected=false
    for expected_name in "${expected_outputs[@]}"; do
        if [[ "$existing_name" == "$expected_name" ]]; then
            expected=true
            break
        fi
    done
    if [[ "$expected" != true ]]; then
        echo "unexpected stale shared library in output directory: $existing" >&2
        exit 1
    fi
done

if ! readelf -h "$shim" | grep -Eq 'Class:[[:space:]]+ELF64'; then
    echo "native movie shim is not an ELF64 binary: $shim" >&2
    exit 1
fi
if ! readelf -h "$shim" | grep -Eq 'Machine:[[:space:]]+Advanced Micro Devices X86-64'; then
    echo "native movie shim is not an x86-64 binary: $shim" >&2
    exit 1
fi
if ! readelf -d "$shim" | grep -Fq '$ORIGIN'; then
    echo "native movie shim does not have an \$ORIGIN runtime search path: $shim" >&2
    exit 1
fi

ldd_output="$(ldd "$shim")"
if [[ "$ldd_output" == *"not found"* ]]; then
    printf '%s\n' "$ldd_output" >&2
    echo "native movie bundle has unresolved shared-library dependencies" >&2
    exit 1
fi
for runtime in "${runtime_libraries[@]}"; do
    resolution="$(printf '%s\n' "$ldd_output" | grep -F "$runtime =>" || true)"
    if [[ "$resolution" != *"=> $output_directory/$runtime "* ]]; then
        printf '%s\n' "$ldd_output" >&2
        echo "$runtime did not resolve from the bundle directory" >&2
        exit 1
    fi
done

minimum_glibc="$(python3 -c \
    'import json, sys; print(json.load(open(sys.argv[1], encoding="utf-8"))["minimum_glibc"])' \
    "$manifest")"
versioned_outputs=("$shim")
for runtime in "${runtime_libraries[@]}"; do
    versioned_outputs+=("$output_directory/$runtime")
done
highest_glibc="$(readelf --version-info "${versioned_outputs[@]}" \
    | grep -Eo 'GLIBC_[0-9]+(\.[0-9]+)*' | sort -Vu | tail -n 1 || true)"
if [[ -z "$highest_glibc" ]]; then
    echo "could not determine the bundle's glibc symbol-version requirement" >&2
    exit 1
fi
highest_glibc="${highest_glibc#GLIBC_}"
newest_glibc="$(printf '%s\n%s\n' "$minimum_glibc" "$highest_glibc" | sort -Vu | tail -n 1)"
if [[ "$newest_glibc" != "$minimum_glibc" ]]; then
    echo "bundle requires glibc $highest_glibc, newer than the pinned $minimum_glibc baseline" >&2
    exit 1
fi

printf '%s\n' "$shim"
