"""What do T1/T2/T3 entries point at? Plus: extract XOR-0xFF strings from scripts.

Prints dword windows around table-entry targets, and scans for complement-encoded
Shift-JIS strings.
"""
import struct
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2] / "extracted" / "DATA1"

def parse(path):
    data = path.read_bytes()
    f = struct.unpack_from("<13I", data, 8)
    body = data[0x3C:]
    return data, f, body

def dw(body, i):
    return struct.unpack_from("<I", body, i * 4)[0]

def try_string(body, i, max_dwords=40):
    """Try to decode a XOR-0xFF cp932 string starting at dword i."""
    raw = bytearray()
    n = 0
    for j in range(i, min(i + max_dwords, len(body) // 4)):
        chunk = bytes(b ^ 0xFF for b in body[j*4:(j+1)*4])
        raw += chunk
        n += 1
        if 0 in chunk:
            break
    else:
        return None, 0
    s = raw.split(b"\0")[0]
    if len(s) == 0:
        return "", n
    try:
        dec = s.decode("cp932")
    except UnicodeDecodeError:
        return None, 0
    if all(0x20 <= b or b in (0x09,) for b in s):
        return dec, n
    return None, 0

def windows(name):
    data, f, body = parse(ROOT / name)
    nd = len(body) // 4
    print(f"\n=== {name} (body {nd} dw, code {f[8]} dw) ===")
    for label, cnt, off in (("T1", f[7], f[8]), ("T2", f[9], f[10]), ("T3", f[11], f[12])):
        entries = [dw(body, off + k) for k in range(min(cnt, 6))]
        print(f" {label}: {cnt} entries -> {[hex(e) for e in entries]}")
        for e in entries[:4]:
            ctx = [dw(body, i) for i in range(max(0, e - 2), min(nd, e + 5))]
            s, _ = try_string(body, e)
            tag = f"  str@target: {s!r}" if s else ""
            print(f"   target {e:#x}: [-2..+4] = {[hex(v) for v in ctx]}{tag}")

def scan_strings(name, limit=15, min_len=4):
    data, f, body = parse(ROOT / name)
    nd = len(body) // 4
    print(f"\n=== strings in {name} ===")
    found = 0
    i = 0
    while i < nd and found < limit:
        s, n = try_string(body, i)
        if s and len(s) >= min_len:
            print(f"  [{i:#x}] {s!r}")
            found += 1
            i += n
        else:
            i += 1

if __name__ == "__main__":
    for n in ("MENU.BIN", "ADDEXP.BIN", "CALLBACK_LOST.BIN", "ALCHEMY.BIN"):
        windows(n)
    scan_strings("MENU.BIN")
    scan_strings("SC0030.BIN", limit=20, min_len=6)
