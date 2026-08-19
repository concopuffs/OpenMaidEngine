"""Confirm operand tag hypothesis: string pointers are preceded by tag dword 2.
Also profile the first body dword and the recurring 'type' dwords 0x55/0x6E/0x6F/0x71/0x72/0x8F.
"""
import struct
from pathlib import Path
import paths
from collections import Counter

ROOT = paths.DATA1

def parse(p):
    data = p.read_bytes()
    f = struct.unpack_from("<13I", data, 8)
    body = data[0x3C:]
    n = len(body) // 4
    dws = struct.unpack_from(f"<{n}I", body, 0)
    return f, dws, body

def is_string_at(body, i):
    n = len(body) // 4
    if i >= n:
        return False
    raw = bytearray()
    for j in range(i, min(i + 60, n)):
        chunk = bytes(b ^ 0xFF for b in body[j*4:(j+1)*4])
        raw += chunk
        if 0 in chunk:
            break
    else:
        return False
    s = bytes(raw).split(b"\0")[0]
    if len(s) < 2:
        return False
    i2, chars = 0, 0
    while i2 < len(s):
        b = s[i2]
        if 0x20 <= b <= 0x7E:
            i2 += 1; chars += 1
        elif 0x81 <= b <= 0x9F or 0xE0 <= b <= 0xEA:
            if i2 + 1 < len(s) and 0x40 <= s[i2+1] <= 0xFC and s[i2+1] != 0x7F:
                i2 += 2; chars += 1
            else:
                return False
        else:
            return False
    return chars >= 2

def main():
    files = sorted(ROOT.glob("*.BIN"))
    preceding = Counter()   # dword right before a string-region start
    first_dword = Counter()
    total_strings = 0
    for p in files:
        f, dws, body = parse(p)
        n = len(dws)
        first_dword[dws[0]] += 1
        # find string regions in the string area (>= T3 end is where strings live typically)
        i = 1
        while i < n:
            if is_string_at(body, i) and not is_string_at(body, i - 1):
                total_strings += 1
                preceding[dws[i - 1]] += 1
                # skip the string
                while i < n and is_string_at(body, i):
                    i += 1
            else:
                i += 1
    print(f"total string regions: {total_strings}")
    print("dword immediately preceding a string (top 10):")
    for v, c in preceding.most_common(10):
        print(f"  {v:#x}: {c}  ({100*c/total_strings:.1f}%)")
    print("\nfirst body dword (top 8):")
    for v, c in first_dword.most_common(8):
        print(f"  {v:#x}: {c}")

if __name__ == "__main__":
    main()
