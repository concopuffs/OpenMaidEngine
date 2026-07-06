"""Corpus-wide: verify T1/T2/T3 target-dword types, test table completeness,
and do a stricter string scan.
"""
import struct
from pathlib import Path
from collections import Counter

ROOT = Path(__file__).resolve().parents[2] / "extracted" / "DATA1"

def parse(path):
    data = path.read_bytes()
    f = struct.unpack_from("<13I", data, 8)
    body = data[0x3C:]
    n = len(body) // 4
    dws = struct.unpack_from(f"<{n}I", body, 0)
    return f, dws, body

def main():
    files = sorted(ROOT.glob("*.BIN"))
    type_hist = {1: Counter(), 2: Counter(), 3: Counter()}
    complete = {1: Counter(), 2: Counter(), 3: Counter()}
    for p in files:
        f, dws, body = parse(p)
        code_end = f[8]
        specs = {1: (f[7], f[8], 0x71), 2: (f[9], f[10], 0x03), 3: (f[11], f[12], 0x8F)}
        for t, (cnt, off, expect) in specs.items():
            targets = dws[off:off + cnt]
            for e in targets:
                type_hist[t][dws[e] if e < len(dws) else "OOB"] += 1
            # naive count of (expect, 0, X) triples in code, at any alignment
            naive = sum(1 for i in range(code_end - 2)
                        if dws[i] == expect and dws[i + 1] == 0)
            if cnt == naive:
                complete[t]["exact"] += 1
            elif cnt < naive:
                complete[t]["table<naive"] += 1
            else:
                complete[t]["table>naive"] += 1
    for t in (1, 2, 3):
        print(f"T{t} target-dword histogram: "
              + ", ".join(f"{v:#x}x{n}" for v, n in type_hist[t].most_common(6)))
        print(f"T{t} completeness vs naive scan: {dict(complete[t])}")

    # value ranges of the operands referenced by each table (sample corpus-wide)
    for t, expect in ((1, 0x71), (2, 0x03), (3, 0x8F)):
        lo, hi = None, None
        vals = Counter()
        for p in files:
            f, dws, body = parse(p)
            cnt, off = (f[7], f[8]) if t == 1 else (f[9], f[10]) if t == 2 else (f[11], f[12])
            for e in dws[off:off + cnt]:
                if e + 2 < len(dws):
                    v = dws[e + 2]
                    vals[v] += 1
                    lo = v if lo is None else min(lo, v)
                    hi = v if hi is None else max(hi, v)
        print(f"T{t} operand values: min={lo and hex(lo)}, max={hi and hex(hi)}, "
              f"top: {', '.join(f'{v:#x}x{n}' for v, n in vals.most_common(8))}")

def good_string(bs):
    """bs = decoded-candidate raw bytes (already XOR'd). Strict cp932 validity."""
    i, chars = 0, 0
    while i < len(bs):
        b = bs[i]
        if 0x20 <= b <= 0x7E:
            i += 1; chars += 1
        elif 0x81 <= b <= 0x9F or 0xE0 <= b <= 0xEA:
            if i + 1 < len(bs) and (0x40 <= bs[i+1] <= 0xFC) and bs[i+1] != 0x7F:
                i += 2; chars += 1
            else:
                return 0
        else:
            return 0
    return chars

def scan(name, limit=25, min_chars=3):
    f, dws, body = parse(ROOT / name)
    n = len(body) // 4
    print(f"\n=== strings in {name} (strict) ===")
    found, i = 0, 0
    while i < n and found < limit:
        raw = bytearray()
        j = i
        while j < n:
            chunk = bytes(b ^ 0xFF for b in body[j*4:(j+1)*4])
            raw += chunk
            j += 1
            if 0 in chunk:
                break
        s = bytes(raw).split(b"\0")[0]
        if len(s) >= 2 and good_string(s) >= min_chars:
            print(f"  [{i:#x}] {s.decode('cp932')!r}")
            found += 1
            i = j
        else:
            i += 1

if __name__ == "__main__":
    main()
    scan("SC0030.BIN")
    scan("MENU.BIN", limit=12)
