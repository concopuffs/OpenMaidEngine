#!/usr/bin/env python3
"""Write FFmpeg-SOURCE.txt: the LGPL source offer and build configuration shipped beside the FFmpeg libraries.

    write_ffmpeg_source_notice.py <dependency-manifest.json> <sdk-root> <output-file>

Reads the manifest's version, license, hosted source URL and SHA-256, and the SDK's BUILD-CONFIG.txt (configure
line and toolchain recorded by build-ffmpeg-sdk.sh), so every native bundle states exactly where the corresponding
source is and how the shipped libraries were built.
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

REQUIRED_FIELDS = ("ffmpeg_version", "license", "mirror_version", "source_url", "source_sha256", "upstream_url")


def render(manifest: dict[str, object], build_config: str) -> str:
    missing = [field for field in REQUIRED_FIELDS if not manifest.get(field)]
    if missing:
        raise ValueError(f"FFmpeg manifest lacks source-notice fields: {', '.join(missing)}")
    if manifest["license"] != "LGPL-2.1-or-later":
        raise ValueError(f"unexpected FFmpeg license in manifest: {manifest['license']}")
    return (
        f"This software uses FFmpeg (https://ffmpeg.org/) version {manifest['ffmpeg_version']}, licensed under the\n"
        "GNU Lesser General Public License version 2.1 or later; the license text is in FFmpeg-LICENSE.txt.\n"
        "FFmpeg is used as unmodified shared libraries that the program links dynamically. Open Maid Engine\n"
        "does not own FFmpeg.\n"
        "\n"
        "Corresponding source (the exact, unmodified release tarball these libraries were built from):\n"
        f"  {manifest['source_url']}\n"
        f"  SHA-256 {manifest['source_sha256']}\n"
        f"  identical to the official FFmpeg release {manifest['upstream_url']}\n"
        "\n"
        f"Build configuration ({manifest['mirror_version']}):\n"
        "\n"
        f"{build_config.rstrip()}\n"
    )


def main(arguments: list[str]) -> int:
    if len(arguments) != 3:
        print(__doc__, file=sys.stderr)
        return 2
    manifest_path, sdk_root, output = (Path(argument) for argument in arguments)
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    build_config_path = sdk_root / "BUILD-CONFIG.txt"
    if not build_config_path.is_file():
        print(f"FFmpeg SDK has no BUILD-CONFIG.txt: {build_config_path}", file=sys.stderr)
        return 1
    try:
        notice = render(manifest, build_config_path.read_text(encoding="utf-8"))
    except ValueError as error:
        print(error, file=sys.stderr)
        return 1
    output.write_text(notice, encoding="utf-8", newline="\n")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
