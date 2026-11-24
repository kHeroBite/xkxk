# PNG 디코더(표준 라이브러리만, 8bit RGB/RGBA 비인터레이스) — pngtiles.py 가 쓰는 공용 함수
import struct
import zlib


def load(path):
    return decode(open(path, "rb").read())


def diff_stats(img1, img2, tile=50):
    """두 디코드 결과의 (차이 화소 수, '#' 타일(100화소 이상 차이) 좌표 집합). 크기가 다르면 (None, None)."""
    w, h, b1, r1 = img1
    w2, h2, b2, r2 = img2
    if (w, h) != (w2, h2):
        return None, None
    cnt, tiles = 0, {}
    for y in range(h):
        a, b = r1[y], r2[y]
        if a == b:
            continue
        for x in range(w):
            p, q = a[x * b1:x * b1 + 3], b[x * b2:x * b2 + 3]
            if max(abs(p[k] - q[k]) for k in range(3)) > 16:
                cnt += 1
                key = (x // tile, y // tile)
                tiles[key] = tiles.get(key, 0) + 1
    return cnt, {k for k, v in tiles.items() if v >= 100}


def decode(data):
    pos, idat, w, h, bpp = 8, b"", 0, 0, 3
    while pos < len(data):
        n, typ = struct.unpack(">I4s", data[pos:pos + 8])
        body = data[pos + 8:pos + 8 + n]
        if typ == b"IHDR":
            w, h, _depth, ctype = struct.unpack(">IIBB", body[:10])
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
