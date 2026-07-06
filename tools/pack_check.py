#!/usr/bin/env python3
import os, sys, struct, math, re
from pathlib import Path
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import paths
data = paths.AGE_EXE.read_bytes()
pe = struct.unpack_from("<I", data, 0x3C)[0]
nsec = struct.unpack_from("<H", data, pe+6)[0]
opt = pe+24
nrva = struct.unpack_from("<I", data, opt+92)[0]
# data directories: index 1 = import, 12 = IAT
imp_rva, imp_sz = struct.unpack_from("<II", data, opt+96+1*8)
iat_rva, iat_sz = struct.unpack_from("<II", data, opt+96+12*8)
print(f"NumberOfRvaAndSizes {nrva}")
print(f"ImportDir  rva {imp_rva:#x} size {imp_sz:#x}")
print(f"IAT        rva {iat_rva:#x} size {iat_sz:#x}")
sec_off = opt + struct.unpack_from("<H", data, pe+20)[0]
print("\nsection entropy:")
for i in range(nsec):
    o=sec_off+i*40
    name=data[o:o+8].rstrip(b"\0").decode('ascii','replace') or f"<blank{i}>"
    vsize,va,rsize,raw=struct.unpack_from("<IIII",data,o+8)
    chunk=data[raw:raw+rsize]
    if chunk:
        freq=[0]*256
        for b in chunk: freq[b]+=1
        ent=-sum((c/len(chunk))*math.log2(c/len(chunk)) for c in freq if c)
    else:
        ent=0
    print(f"  {name:<10} va {va:#08x} vsize {vsize:#08x} raw {raw:#08x} rsize {rsize:#08x} entropy {ent:.2f}")

# UTF-16LE anchor search
print("\nUTF-16 anchors:")
for t in [b"SYS4422", b"DATA1", b"Eushully", b".BIN", b"AGE"]:
    u = t.decode().encode("utf-16le")
    n = data.count(u)
    print(f"  {t.decode():10} utf16 hits {n}")
# ascii import dll names still present?
print("\nDLL-name-ish ascii:")
for m in re.finditer(rb"[\x20-\x7e]{4,}\.dll", data, re.I):
    print("  ", m.group().decode(errors='replace'))
