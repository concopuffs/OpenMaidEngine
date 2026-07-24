#!/usr/bin/env python3
"""Parse SYS4INI.BIN (Eushully AGE asset index) -> build/asset-index.json.

SYS4INI.BIN is the authoritative directory the game (and BinExtractALF, which is
based on asmodean's exs4alf) uses to locate every asset inside the DATA*.ALF
archives. It maps  name <-> archive <-> offset <-> size  and is the reusable
"answer key" for resId->file resolution (see docs/asset-resolution-re.md): it
both names every asset and gives archive-offset->name (to rescue noisy Frida
file-I/O offsets).

Container format (little-endian x86), signature "S4IC422 " at offset 0:

    0x000  char     signature[?]      "S4IC" family; "S4IC" -> data at 0x134
    ...    (title / padding)
    0x134  uint32   packed_size       length of the LZSS stream that follows
    0x138  byte[]   lzss_stream       packed_size bytes, runs to EOF

The LZSS stream (GARbro-style: 0x1000 ring buffer, zero-filled, init pos 0xFEE,
control bits LSB->MSB, 1=literal, 0=two-byte back-reference: offset=(hi&0xf0)<<4
| lo, length=3+(hi&0xf)) begins with a plain directory:

    uint32   arc_count
    { char name[256] } x arc_count        archive filenames (DATA1.ALF ..)
    uint32   file_count
    { char name[64]; uint32 arc_id;       one record per asset
      uint32 file_number; uint32 offset;
      uint32 size } x file_count          (80 bytes each)

The directory is followed by VM-bank metadata and a per-game engine-settings
trailer. This tool currently emits only the asset directory; see
docs/sys4-format-notes.md for the trailer finding.

Entries whose name is "@" are placeholders and skipped (matches GARbro/exs4alf).

Usage: py -3.11 -X utf8 tools/parse_sys4ini.py [--check]
  --check  cross-validate against the extracted/ ground truth and .ALF sizes
"""
from __future__ import annotations
import json
import struct
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import paths

# S4IC family: signature -> offset of the packed-size dword that precedes the stream.
DATA_OFFSETS = {b"S4AC": 0x114, b"S4IC": 0x134, b"S3IC": 0x134, b"S3IN": 0x12C}

FRAME_SIZE = 0x1000
FRAME_INIT_POS = 0xFEE
ARC_NAME_LEN = 256
FILE_NAME_LEN = 64
FILE_ENTRY_FMT = "<64s4I"           # name[64], arc_id, file_number, offset, size
FILE_ENTRY_LEN = struct.calcsize(FILE_ENTRY_FMT)   # 80


def lzss_decompress(src: bytes) -> bytes:
    """GARbro-compatible LZSS (0x1000 ring buffer, init pos 0xFEE, threshold 3)."""
    frame = bytearray(FRAME_SIZE)          # zero-filled
    fpos = FRAME_INIT_POS
    out = bytearray()
    i, n = 0, len(src)
    while i < n:
        ctl = src[i]; i += 1
        for bit in (1, 2, 4, 8, 0x10, 0x20, 0x40, 0x80):
            if ctl & bit:                  # literal
                if i >= n:
                    return bytes(out)
                b = src[i]; i += 1
                out.append(b)
                frame[fpos] = b; fpos = (fpos + 1) & 0xFFF
            else:                          # back-reference
                if i + 1 >= n:
                    return bytes(out)
                lo, hi = src[i], src[i + 1]; i += 2
                off = ((hi & 0xF0) << 4) | lo
                for _ in range(3 + (hi & 0x0F)):
                    b = frame[off & 0xFFF]; off += 1
                    out.append(b)
                    frame[fpos] = b; fpos = (fpos + 1) & 0xFFF
    return bytes(out)


def _cstr(buf: bytes) -> str:
    """Decode a null-terminated cp932 field (filenames are ASCII in practice)."""
    return buf.split(b"\x00", 1)[0].decode("cp932", errors="replace")


def parse(path: Path) -> dict:
    raw = path.read_bytes()
    sig4 = raw[:4]
    if sig4 not in DATA_OFFSETS:
        raise SystemExit(f"{path.name}: unknown signature {raw[:8]!r}")
    magic = _cstr(raw[:8])
    doff = DATA_OFFSETS[sig4]
    packed_size = struct.unpack_from("<I", raw, doff)[0]
    stream = raw[doff + 4: doff + 4 + packed_size]
    if len(stream) != packed_size:
        raise SystemExit(f"{path.name}: packed stream truncated "
                         f"({len(stream)} of {packed_size} bytes)")
    blob = lzss_decompress(stream)

    p = 0
    (arc_count,) = struct.unpack_from("<I", blob, p); p += 4
    if not 0 < arc_count < 0x1000:
        raise SystemExit(f"{path.name}: implausible arc_count {arc_count}")
    archives = []
    for _ in range(arc_count):
        archives.append(_cstr(blob[p:p + ARC_NAME_LEN])); p += ARC_NAME_LEN

    (file_count,) = struct.unpack_from("<I", blob, p); p += 4
    if not 0 < file_count < 0x400000:
        raise SystemExit(f"{path.name}: implausible file_count {file_count}")
    need = p + file_count * FILE_ENTRY_LEN
    if need > len(blob):
        raise SystemExit(f"{path.name}: directory truncated (need {need}, "
                         f"have {len(blob)} decompressed bytes)")

    files = []
    for i in range(file_count):
        name_b, arc_id, file_number, offset, size = struct.unpack_from(FILE_ENTRY_FMT, blob, p)
        p += FILE_ENTRY_LEN
        name = _cstr(name_b)
        if name == "@" or not name:
            continue
        files.append({
            # raw_index = the record's 0-based position in the SYS4INI table INCLUDING '@'
            # placeholders. This is the engine's universal file id: `call-script <id>` and every
            # script/asset load index by it (engine FUN_0044f390: record = base + id*0x50). It is
            # NOT the same as this entry's position in `files` (which omits placeholders).
            "raw_index": i,
            "name": name,
            "archive": archives[arc_id] if 0 <= arc_id < arc_count else None,
            "arc_id": arc_id,
            "file_number": file_number,
            "offset": offset,
            "size": size,
        })

    return {
        "source": path.name,
        "magic": magic,
        "packed_size": packed_size,
        "decompressed_size": len(blob),
        "archive_count": arc_count,
        "archives": archives,
        "file_count": file_count,
        "entry_count": len(files),
        "files": files,
    }


def check(index: dict) -> int:
    """Cross-validate against extracted/ counts and real .ALF file sizes."""
    problems = 0
    per_arc: dict[str, int] = {}
    for f in index["files"]:
        per_arc[f["archive"]] = per_arc.get(f["archive"], 0) + 1

    print("per-archive entry counts (from SYS4INI):")
    for a in index["archives"]:
        print(f"  {a:<16} {per_arc.get(a, 0)}")

    # 1) offset+size must fit inside the real archive on disk.
    for a in index["archives"]:
        alf = paths.GAME_DIR / a
        if not alf.exists():
            print(f"  ! archive not on disk: {a}")
            continue
        asize = alf.stat().st_size
        over = [f for f in index["files"]
                if f["archive"] == a and f["offset"] + f["size"] > asize]
        if over:
            problems += len(over)
            print(f"  ! {a}: {len(over)} entries run past EOF ({asize} bytes); "
                  f"e.g. {over[0]['name']} @ {over[0]['offset']}+{over[0]['size']}")
        else:
            print(f"  ok {a}: all entries within {asize} bytes")

    # 2) spot-check extracted-folder file sizes against the index (name -> size).
    by_name = {f["name"].upper(): f for f in index["files"]}
    checked = mismatch = 0
    for d in ("DATA1", "DATA2", "DATA3", "DATA4", "DATA5"):
        folder = paths.EXTRACTED / d
        if not folder.is_dir():
            continue
        for fp in list(folder.glob("*"))[:200]:
            if not fp.is_file():
                continue
            e = by_name.get(fp.name.upper())
            if e is None:
                continue
            checked += 1
            if e["size"] != fp.stat().st_size:
                mismatch += 1
                if mismatch <= 5:
                    print(f"  ! size mismatch {fp.name}: index {e['size']} "
                          f"vs extracted {fp.stat().st_size}")
    print(f"size spot-check: {checked} matched by name, {mismatch} size mismatches")
    problems += mismatch
    print("CHECK OK" if problems == 0 else f"CHECK: {problems} problems")
    return problems


def main() -> int:
    do_check = "--check" in sys.argv[1:]
    src = paths.GAME_DIR / "SYS4INI.BIN"
    if not src.exists():
        raise SystemExit(f"not found: {src}")
    index = parse(src)
    print(f"{index['source']}: magic={index['magic']!r} "
          f"packed={index['packed_size']} -> {index['decompressed_size']} bytes")
    print(f"archives ({index['archive_count']}): {index['archives']}")
    print(f"files: {index['file_count']} declared, {index['entry_count']} real "
          f"(after skipping '@' placeholders)")
    for f in index["files"][:5]:
        print(f"  {f['name']:<16} {f['archive']:<12} "
              f"off={f['offset']:>12} size={f['size']:>10} #{f['file_number']}")

    out = paths.BUILD / "asset-index.json"
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(index, ensure_ascii=False, indent=1), encoding="utf-8")
    print(f"-> {out.relative_to(paths.REPO)}")

    # call-script id -> name map (raw_index keyed). `call-script <id>` (opcode 0x03) is a direct
    # raw index into the SYS4INI file table, so this IS the call-graph name registry that
    # name-resolution.md #1 needed (confirmed via native-RE, see docs/engine-re.md). sys4load reads
    # it to annotate `call-script 0x1ab =ADDITEM`.
    cs = {str(f["raw_index"]): f["name"] for f in index["files"]}
    cs_out = paths.BUILD / "callscript-names.json"
    cs_out.write_text(json.dumps(cs, ensure_ascii=False, indent=0), encoding="utf-8")
    print(f"-> {cs_out.relative_to(paths.REPO)} ({len(cs)} ids)")

    if do_check:
        print("--- validation ---")
        return 1 if check(index) else 0
    return 0


if __name__ == "__main__":
    sys.exit(main())
