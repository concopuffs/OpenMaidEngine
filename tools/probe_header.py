"""Probe SYS4 script headers across the DATA1 corpus.

Header hypothesis (offsets in bytes):
  0x00  char magic[8]      "SYS4422 "
  0x08  u32 F0..F12        13 fields
  0x3C  body (dword stream)

Tests invariants and prints field stats.
"""
import struct
import sys
from pathlib import Path
from collections import Counter
import paths

ROOT = paths.DATA1

def parse(path):
    data = path.read_bytes()
    magic = data[:8]
    fields = struct.unpack_from("<13I", data, 8)
    return data, magic, fields

def main():
    files = sorted(ROOT.glob("*.BIN"))
    print(f"files: {len(files)}")

    magics = Counter()
    distinct = [Counter() for _ in range(13)]
    fail_f12 = []      # F12 != filedwords - 15
    fail_f6 = []       # F6 != 0x1C
    fail_f11 = []      # F11 != 0
    rel_a = Counter()  # F8 + F9 vs F12
    rel_b = Counter()  # F10 + F11 vs F12
    body_first = Counter()

    for p in files:
        data, magic, f = parse(p)
        magics[magic] += 1
        nd = len(data) // 4
        for i, v in enumerate(f):
            distinct[i][v] += 1
        if f[12] != nd - 15:
            fail_f12.append((p.name, f[12], nd - 15))
        if f[6] != 0x1C:
            fail_f6.append((p.name, f[6]))
        if f[11] != 0:
            fail_f11.append((p.name, f[10], f[11], f[12]))
        rel_a["F8+F9==F12" if f[8] + f[9] == f[12] else
              ("F8+F9<F12" if f[8] + f[9] < f[12] else "F8+F9>F12")] += 1
        rel_b["F10+F11==F12" if f[10] + f[11] == f[12] else
              ("F10+F11<F12" if f[10] + f[11] < f[12] else "F10+F11>F12")] += 1
        if len(data) >= 0x40:
            body_first[struct.unpack_from("<I", data, 0x3C)[0]] += 1

    print("magics:", dict(magics))
    print("\nfield value diversity (distinct count; top values):")
    for i, c in enumerate(distinct):
        top = ", ".join(f"{v:#x}x{n}" for v, n in c.most_common(4))
        print(f"  F{i:<2} @0x{8+i*4:02X}: {len(c):4} distinct | {top}")
    print(f"\nF6==0x1C fails: {len(fail_f6)} {fail_f6[:5]}")
    print(f"F12==dwords-15 fails: {len(fail_f12)} {fail_f12[:5]}")
    print(f"F11!=0 count: {len(fail_f11)} {fail_f11[:5]}")
    print("rel F8+F9 vs F12:", dict(rel_a))
    print("rel F10+F11 vs F12:", dict(rel_b))
    print("\nfirst body dword (top 15):",
          ", ".join(f"{v:#x}x{n}" for v, n in body_first.most_common(15)))

if __name__ == "__main__":
    main()
