# PNG 두 장 화소 비교(표준 라이브러리만, 8bit RGB/RGBA 비인터레이스) — 차이 화소 수와 차이 영역 상자를 출력한다
import struct
import sys
import zlib


def load(path):
    data = open(path, "rb").read()
    pos, idat, w = 8, b"", 0
    while pos < len(data):
        n, typ = struct.unpack(">I4s", data[pos:pos + 8])
        body = data[pos + 8:pos + 8 + n]
        if typ == b"IHDR":
            w, h, depth, ctype = struct.unpack(">IIBB", body[:10])
            bpp = {2: 3, 6: 4}[ctype]
        elif typ == b"IDAT":
            idat += body
        pos += 12 + n
    raw = zlib.decompress(idat)
    stride = w * bpp
    rows, prev, i = [], bytearray(stride), 0
    for _ in range(h):
        f = raw[i]
        line = bytearray(raw[i + 1:i + 1 + stride])
        i += 1 + stride
        for x in range(stride):
            a = line[x - bpp] if x >= bpp else 0
            b = prev[x]
            c = prev[x - bpp] if x >= bpp else 0
            if f == 1:
                line[x] = (line[x] + a) & 255
            elif f == 2:
                line[x] = (line[x] + b) & 255
            elif f == 3:
                line[x] = (line[x] + (a + b) // 2) & 255
            elif f == 4:
                p = a + b - c
                pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
                line[x] = (line[x] + (a if pa <= pb and pa <= pc else b if pb <= pc else c)) & 255
        rows.append(bytes(line))
        prev = line
    return w, h, bpp, rows


w1, h1, b1, r1 = load(sys.argv[1])
w2, h2, b2, r2 = load(sys.argv[2])
print("size", (w1, h1), (w2, h2))
if (w1, h1) != (w2, h2):
    sys.exit(0)
cnt, box = 0, [w1, h1, -1, -1]
for y in range(h1):
    a, b = r1[y], r2[y]
    if a == b:
        continue
    for x in range(w1):
        p, q = a[x * b1:x * b1 + 3], b[x * b2:x * b2 + 3]
        if max(abs(p[k] - q[k]) for k in range(3)) > 16:
            cnt += 1
            box = [min(box[0], x), min(box[1], y), max(box[2], x), max(box[3], y)]
print("diff_pixels", cnt, "of", w1 * h1, "box", box if cnt else None)
