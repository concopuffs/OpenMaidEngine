"""Verify the three (count, offset) table descriptors and inspect table contents.

Layout hypothesis (all offsets in dwords, relative to body start at 0x3C):
  code:   body[0 .. F8)
  T1:     count=F7,  offset=F8,  entry size s1 (solve: F8 + s1*F7 == F10)
  T2:     count=F9,  offset=F10, entry size s2 (solve: F10 + s2*F9 == F12)
  T3:     count=F11, offset=F12, entry size s3 (solve: F12 + s3*F11 == EOF)
"""
import struct
from pathlib import Path
import paths
from collections import Counter

ROOT = paths.DATA1

def parse(path):
    data = path.read_bytes()
    f = struct.unpack_from("<13I", data, 8)
    return data, f

def solve_entry_size(gap_pairs):
    """gap_pairs: list of (count, gap_dwords). Return consistent entry size or None."""
    sizes = set()
    for count, gap in gap_pairs:
        if count:
            if gap % count:
                return f"non-integer ({count},{gap})"
            sizes.add(gap // count)
    return sizes

def main():
    files = sorted(ROOT.glob("*.BIN"))
    t1_pairs, t2_pairs, t3_pairs = [], [], []
    bad = []
    for p in files:
        data, f = parse(p)
        nd = len(data) // 4 - 15  # body dwords
        F7, F8, F9, F10, F11, F12 = f[7], f[8], f[9], f[10], f[11], f[12]
        if not (F8 <= F10 <= F12 <= nd):
            bad.append((p.name, F8, F10, F12, nd))
            continue
        t1_pairs.append((F7, F10 - F8))
        t2_pairs.append((F9, F12 - F10))
        t3_pairs.append((F11, nd - F12))
    print(f"ordering violations: {len(bad)} {bad[:5]}")
    print("T1 entry sizes:", solve_entry_size(t1_pairs))
    print("T2 entry sizes:", solve_entry_size(t2_pairs))
    print("T3 entry sizes:", solve_entry_size(t3_pairs))

    # zero-count but nonzero-gap sanity
    for name, pairs in (("T1", t1_pairs), ("T2", t2_pairs), ("T3", t3_pairs)):
        odd = sum(1 for c, g in pairs if c == 0 and g != 0)
        print(f"{name}: count==0 but gap!=0 in {odd} files")

    # F2 outliers
    print("\nF2 outliers:")
    for p in files:
        _, f = parse(p)
        if f[2] not in (1, 2):
            print(f"  {p.name}: F2={f[2]:#x}")

    # dump tables for a few files
    for name in ("MENU.BIN", "ADDEXP.BIN", "ADDILL.BIN", "ALCHEMY.BIN"):
        data, f = parse(ROOT / name)
        nd = len(data) // 4 - 15
        print(f"\n=== {name}: body={nd} F7-12: cnt/off T1={f[7]}/{f[8]} "
              f"T2={f[9]}/{f[10]} T3={f[11]}/{f[12]}")
        for label, cnt, off, end in (("T1", f[7], f[8], f[10]),
                                     ("T2", f[9], f[10], f[12]),
                                     ("T3", f[11], f[12], nd)):
            vals = struct.unpack_from(f"<{end-off}I", data, 0x3C + off*4)
            show = ", ".join(f"{v:#x}" for v in vals[:16])
            print(f"  {label} ({cnt} entries, {end-off} dwords): {show}"
                  + (" ..." if end-off > 16 else ""))

if __name__ == "__main__":
    main()
