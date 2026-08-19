"""Find how instructions reference string offsets, and inspect 0x71 operands
in context. Uses T3 jump-target values as confirmed instruction starts.
"""
import struct
from pathlib import Path
import paths
from collections import Counter

ROOT = paths.DATA1

def parse(name):
    data = (ROOT / name).read_bytes()
    f = struct.unpack_from("<13I", data, 8)
    body = data[0x3C:]
    n = len(body) // 4
    dws = struct.unpack_from(f"<{n}I", body, 0)
    return f, dws, body

def decode_str(body, i):
    raw = bytearray()
    n = len(body) // 4
    for j in range(i, min(i + 60, n)):
        chunk = bytes(b ^ 0xFF for b in body[j*4:(j+1)*4])
        raw += chunk
        if 0 in chunk:
            break
    try:
        return bytes(raw).split(b"\0")[0].decode("cp932")
    except UnicodeDecodeError:
        return None

def xref(name, targets):
    f, dws, body = parse(name)
    print(f"\n=== {name}: xrefs to {[hex(t) for t in targets]} ===")
    for t in targets:
        hits = [i for i, v in enumerate(dws[:f[8]]) if v == t]
        for i in hits:
            lo = max(0, i - 6)
            ctx = " ".join(f"{v:x}" for v in dws[lo:i + 3])
            print(f"  {t:#x} referenced at [{i:#x}]: ...{ctx}...")

def sample_71(name, k=6):
    f, dws, body = parse(name)
    print(f"\n=== {name}: T1 (0x71) operands in context ===")
    off, cnt = f[8], f[7]
    for e in list(dws[off:off + cnt])[:k]:
        lo = max(0, e - 8)
        ctx = " ".join(f"{v:x}" for v in dws[lo:e + 4])
        print(f"  target [{e:#x}]: ...{ctx}...")

def first_instrs(name, count=20):
    """Dump first N dwords raw, plus known instruction starts from T3 values."""
    f, dws, body = parse(name)
    starts = sorted(set(dws[f[12]:f[12] + f[11]]))
    print(f"\n=== {name}: first dwords ===")
    print("  " + " ".join(f"{v:x}" for v in dws[:count]))
    print(f"  T3-confirmed instruction starts (first 12): {[hex(s) for s in starts[:12]]}")

if __name__ == "__main__":
    xref("MENU.BIN", [0x303, 0x30B, 0x30E])
    xref("SC0030.BIN", [0xEF80, 0xEF89, 0xEF91])
    sample_71("SC0030.BIN")
    sample_71("MENU.BIN", k=2)
    first_instrs("MENU.BIN")
    first_instrs("SC0030.BIN")
