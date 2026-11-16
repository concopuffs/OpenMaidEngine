"""Writes a synthetic 32x32 32-bit .ico for M0 question 2 (does a cross-built Windows program carry its icon).

The picture is a plain square with a diagonal; only its presence in the program's resources matters.
Usage: python3 make-icon.py OUT.ico
"""
import struct
import sys

SIZE = 32


def pixels() -> bytes:
    rows = []
    for y in range(SIZE - 1, -1, -1):  # BMP rows are stored bottom-up
        row = bytearray()
        for x in range(SIZE):
            on_diagonal = abs(x - y) <= 2
            b, g, r = (0xF0, 0xF0, 0xF0) if on_diagonal else (0x80, 0x40, 0xC0)
            row += bytes((b, g, r, 0xFF))
        rows.append(bytes(row))
    return b"".join(rows)


def main() -> None:
    xor = pixels()
    and_mask = bytes(4 * SIZE)  # one bit per pixel, rows padded to 4 bytes; all opaque
    header = struct.pack("<IiiHHIIiiII", 40, SIZE, SIZE * 2, 1, 32, 0, len(xor) + len(and_mask), 0, 0, 0, 0)
    image = header + xor + and_mask
    icondir = struct.pack("<HHH", 0, 1, 1)
    entry = struct.pack("<BBBBHHII", SIZE, SIZE, 0, 0, 1, 32, len(image), 6 + 16)
    with open(sys.argv[1], "wb") as f:
        f.write(icondir + entry + image)


if __name__ == "__main__":
    main()
