#!/usr/bin/env bash
# Build the project's minimal LGPL FFmpeg SDK for one target from the pinned official release tarball.
#
#   build-ffmpeg-sdk.sh <linux-x64|win64> [output-directory]
#
# Inputs:  ffmpeg-source.json (source pin, configure flags), dependency-linux-x64.json (glibc baseline).
# Outputs: <output>/ome-ffmpeg-<version>-mpeg1-r<rev>-<target>-lgpl-shared.{tar.xz|zip}, its .sha256, and
#          <output>/BUILD-CONFIG-<target>.txt. The SDK keeps the include/ lib/ bin/ LICENSE.txt layout the
#          bootstraps consume and adds BUILD-CONFIG.txt. Linux must run in a glibc-2.28 (manylinux_2_28)
#          environment; win64 cross-compiles with the MinGW-w64 toolchain.
set -euo pipefail

target="${1:-}"
case "$target" in
    linux-x64 | win64) ;;
    *)
        echo "usage: $0 <linux-x64|win64> [output-directory]" >&2
        exit 2
        ;;
esac

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd -- "$script_dir/../.." && pwd)"
output_directory="${2:-$repo_root/build/ffmpeg-sdk-dist}"
source_manifest="$script_dir/ffmpeg-source.json"

for command in python3 curl sha256sum tar make; do
    command -v "$command" >/dev/null 2>&1 || {
        echo "required command was not found: $command" >&2
        exit 1
    }
done

json() {
    python3 -c 'import json, sys; value = json.load(open(sys.argv[1], encoding="utf-8"))
for key in sys.argv[2:]: value = value[key]
print("\n".join(value) if isinstance(value, list) else value)' "$@"
}

version="$(json "$source_manifest" version)"
source_archive="$(json "$source_manifest" archive)"
source_url="$(json "$source_manifest" url)"
source_size="$(json "$source_manifest" size)"
source_sha256="$(json "$source_manifest" sha256)"
license_file="$(json "$source_manifest" license_file)"
mirror_version="$(json "$source_manifest" mirror_version)"
mapfile -t common_flags < <(json "$source_manifest" configure_flags)
mapfile -t target_flags < <(json "$source_manifest" target_configure_flags "$target")

download_dir="$repo_root/build/downloads"
work_dir="$repo_root/build/ffmpeg-sdk-work/$target"
source_path="$download_dir/$source_archive"
sdk_name="ome-ffmpeg-${mirror_version#ome-}-$target-lgpl-shared"
mkdir -p -- "$download_dir" "$output_directory"

# 1. Pinned source: download once, then require the exact size and SHA-256 on every run.
if [[ ! -f "$source_path" ]]; then
    curl --fail --location --retry 3 --output "$source_path.partial" "$source_url"
    mv -- "$source_path.partial" "$source_path"
fi
actual_size="$(wc -c < "$source_path" | tr -d ' ')"
actual_sha256="$(sha256sum "$source_path" | awk '{ print $1 }')"
if [[ "$actual_size" != "$source_size" || "$actual_sha256" != "$source_sha256" ]]; then
    echo "FFmpeg source does not match the pin: size $actual_size/$source_size, sha256 $actual_sha256" >&2
    exit 1
fi

# 2. Fresh, unmodified source tree.
rm -rf -- "$work_dir"
mkdir -p -- "$work_dir"
tar -xJf "$source_path" -C "$work_dir"
source_tree="$work_dir/ffmpeg-$version"
stage="$work_dir/stage"
install_root="$stage/ome-ffmpeg"

# 3. Configure with only the manifest's flags; a fixed prefix keeps build paths out of the libraries.
configure_line=(./configure --prefix=/ome-ffmpeg "${common_flags[@]}" "${target_flags[@]}")
(
    cd -- "$source_tree"
    "${configure_line[@]}"
    make -j"$(nproc)"
    make install DESTDIR="$stage"
)

# 4. Target checks.
libraries=(avformat avcodec avutil swscale swresample)
if [[ "$target" == linux-x64 ]]; then
    minimum_glibc="$(json "$script_dir/dependency-linux-x64.json" minimum_glibc)"
    shared_objects=()
    for library in "${libraries[@]}"; do
        resolved="$(readlink -f -- "$install_root/lib/lib$library.so")"
        strip --strip-unneeded -- "$resolved"
        shared_objects+=("$resolved")
    done
    highest_glibc="$(readelf --version-info "${shared_objects[@]}" \
        | grep -Eo 'GLIBC_[0-9]+(\.[0-9]+)*' | sed 's/^GLIBC_//' | sort -Vu | tail -n 1)"
    newest="$(printf '%s\n%s\n' "$minimum_glibc" "$highest_glibc" | sort -V | tail -n 1)"
    if [[ "$newest" != "$minimum_glibc" ]]; then
        echo "FFmpeg libraries require glibc $highest_glibc, newer than the $minimum_glibc baseline" >&2
        exit 1
    fi
    platform_report="glibc symbol ceiling: $highest_glibc (baseline $minimum_glibc)"
    rm -rf -- "$install_root/lib/pkgconfig" "$install_root/share"
else
    objdump_tool="x86_64-w64-mingw32-objdump"
    command -v "$objdump_tool" >/dev/null || { echo "required command was not found: $objdump_tool" >&2; exit 1; }
    x86_64-w64-mingw32-strip --strip-unneeded -- "$install_root"/bin/*.dll
    imports="$("$objdump_tool" -p "$install_root"/bin/*.dll | awk '/DLL Name:/ { print tolower($3) }' | sort -u)"
    allowed='^(avformat-[0-9]+|avcodec-[0-9]+|avutil-[0-9]+|swscale-[0-9]+|swresample-[0-9]+|kernel32|msvcrt|ucrtbase|bcrypt|advapi32|user32|api-ms-win-[a-z0-9-]+)\.dll$'
    unexpected="$(grep -Ev "$allowed" <<< "$imports" || true)"
    if [[ -n "$unexpected" ]]; then
        echo "FFmpeg DLLs import non-system libraries:" >&2
        echo "$unexpected" >&2
        exit 1
    fi
    platform_report="DLL imports: $(tr '\n' ' ' <<< "$imports")"
    rm -rf -- "$install_root/lib/pkgconfig" "$install_root/share"
    # FFmpeg installs the MSVC import libraries beside the DLLs; the SDK layout keeps them in lib/.
    mv -- "$install_root"/bin/*.lib "$install_root/lib/"
fi

# 5. SDK layout + provenance.
cp -- "$source_tree/$license_file" "$install_root/LICENSE.txt"
compiler="$(if [[ "$target" == win64 ]]; then x86_64-w64-mingw32-gcc --version; else ${CC:-cc} --version; fi | head -n 1)"
build_config="$install_root/BUILD-CONFIG.txt"
{
    echo "FFmpeg $version - Open Maid Engine minimal LGPL build ($target, $mirror_version)"
    echo
    echo "Source: $source_url"
    echo "Source SHA-256: $source_sha256 ($source_size bytes)"
    echo "Source changes: none. The release tarball is built unmodified; changes.diff would be empty."
    echo "License: LGPL version 2.1 or later ($license_file, shipped here as LICENSE.txt)."
    echo
    echo "Configure line:"
    printf '  %s\n' "${configure_line[*]}"
    echo
    echo "Compiler: $compiler"
    echo "$platform_report"
} > "$build_config"

# 6. Deterministic archive.
python3 - "$install_root" "$output_directory" "$sdk_name" "$target" <<'PY'
import hashlib
import io
import sys
import tarfile
import zipfile
from pathlib import Path

root, output, name, target = Path(sys.argv[1]), Path(sys.argv[2]), sys.argv[3], sys.argv[4]
# Regular files first, then symlinks, so extractors that cannot create dangling links (e.g. on Windows) succeed.
entries = sorted(path for path in root.rglob("*") if path.is_file() and not path.is_symlink())
entries += sorted(path for path in root.rglob("*") if path.is_symlink())
EPOCH = 0
if target == "linux-x64":
    archive = output / f"{name}.tar.xz"
    with tarfile.open(archive, "w:xz", format=tarfile.PAX_FORMAT) as bundle:
        for path in entries:
            info = bundle.gettarinfo(str(path), arcname=f"{name}/{path.relative_to(root).as_posix()}")
            info.uid = info.gid = 0
            info.uname = info.gname = ""
            info.mtime = EPOCH
            info.mode = 0o755 if info.isfile() and (path.stat().st_mode & 0o111) else (0o644 if info.isfile() else info.mode)
            if info.issym():
                bundle.addfile(info)
            else:
                with path.open("rb") as stream:
                    bundle.addfile(info, stream)
else:
    archive = output / f"{name}.zip"
    with zipfile.ZipFile(archive, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as bundle:
        for path in entries:
            info = zipfile.ZipInfo(f"{name}/{path.relative_to(root).as_posix()}", date_time=(1980, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o644 << 16
            bundle.writestr(info, path.read_bytes())
digest = hashlib.sha256(archive.read_bytes()).hexdigest()
(archive.parent / f"{archive.name}.sha256").write_text(f"{digest}  {archive.name}\n", encoding="ascii")
print(f"{archive.name}: {archive.stat().st_size} bytes, sha256 {digest}")
PY
cp -- "$build_config" "$output_directory/BUILD-CONFIG-$target.txt"
