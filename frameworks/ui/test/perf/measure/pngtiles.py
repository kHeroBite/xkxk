# PNG 두 장 차이를 50px 타일 지도로 출력(표준 라이브러리만) — 어느 창 영역이 다른지 보는 보조 도구
import sys

sys.path.insert(0, __file__.rsplit("/", 1)[0])
from pngdiff_lib import load  # noqa: E402

w, h, b1, r1 = load(sys.argv[1])
_, _, b2, r2 = load(sys.argv[2])
T = 50
for ty in range(0, h, T):
    line = ""
    for tx in range(0, w, T):
        n = 0
        for y in range(ty, min(ty + T, h)):
            a, b = r1[y], r2[y]
            for x in range(tx, min(tx + T, w), 2):
                p, q = a[x * b1:x * b1 + 3], b[x * b2:x * b2 + 3]
                if max(abs(p[k] - q[k]) for k in range(3)) > 16:
                    n += 1
        line += "." if n == 0 else ("o" if n < 100 else "#")
    print(f"{ty:4d} {line}")
