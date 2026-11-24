#!/usr/bin/env python3
# perf-ui 앱 로그에서 'Layout cycle detected' 줄을 조작별로 세어 레이아웃 순환 회귀를 판정하는 스크립트
"""
사용:
  python3 -I layout_cycle_per_op.py <app_*.log> [--max-total N] [--json 결과.json]
  python3 -I layout_cycle_per_op.py --self-test

동작:
  Avalonia 는 한 레이아웃 패스에서 같은 요소가 10회 넘게 대기열에 들어오면 'Layout cycle detected' 를 남기고 포기한다.
  PerfUiRunner 는 조작마다 '[perf-ui] … end <이름> …'(기본) 또는 '[perf-split] <이름> …'(--perf-split) 줄을 조작이 끝난 뒤 쓴다.
  그래서 두 표식 사이의 순환 줄은 뒤 표식의 조작 몫으로 센다.
출력: 조작별 합계·회차별 수·요소 종류 상위, 전체 합계. --max-total(기본 0) 을 넘으면 rc 1.
rc: 0=기준 이하, 1=기준 초과, 2=입력 없음·표식 없음.
재사용 이유: 그리드 순환(odebug-grid, ZoomDecorator 측정·배치 왕복)은 n1 단계에서만 드러나고 판정 결과(시간)에는 원인이 안 보인다.
           ZoomDecorator·PdDataGrid·DataGrid 열 폭 규칙을 고칠 때 같은 로그로 순환 0 을 다시 확인한다.
"""
import collections
import json
import re
import sys

ITEM = re.compile(r"Item '([^']+)'")


def count(lines):
    per = collections.OrderedDict()
    kinds = collections.OrderedDict()
    pending = 0
    pending_kinds = collections.Counter()
    markers = 0
    for line in lines:
        if "Layout cycle detected" in line:
            pending += 1
            m = ITEM.search(line)
            if m:
                pending_kinds[m.group(1).rsplit(".", 1)[-1]] += 1
            continue
        name = None
        if line.startswith("[perf-split] "):
            parts = line.split()
            name = parts[1] if len(parts) > 1 else None
        elif line.startswith("[perf-ui]") and " end " in line:
            name = line.split(" end ", 1)[1].split()[0]
        if name is None:
            continue
        markers += 1
        per.setdefault(name, []).append(pending)
        kinds.setdefault(name, collections.Counter()).update(pending_kinds)
        pending = 0
        pending_kinds = collections.Counter()
    return per, kinds, pending, markers


def report(per, kinds, tail):
    rows = []
    for name, v in per.items():
        if sum(v):
            rows.append({"op": name, "total": sum(v), "reps": v, "top": kinds[name].most_common(3)})
    total = sum(sum(v) for v in per.values()) + tail
    return {"total": total, "tail_unassigned": tail, "ops": rows}


def self_test():
    log = [
        "[perf-ui] 00:00 stage n=1 rep 1/2",
        "[perf-ui] 00:00 end perfui.n1.font.up sync=1ms frame=2ms",
        "[Layout]Layout cycle detected. Item 'Avalonia.Controls.Primitives.DataGridRowsPresenter' was enqueued '10' times.",
        "[Layout]Layout cycle detected. Item 'Pd.Ui.Views.ZoomDecorator' was enqueued '10' times.",
        "[perf-ui] 00:01 end perfui.n1.grid.sort sync=1ms frame=2ms",
        "[perf-split] perfui.n1.grid.sort sync=1 layout1=2",
    ]
    per, kinds, tail, markers = count(log)
    r = report(per, kinds, tail)
    assert markers == 3, markers
    assert r["total"] == 2, r
    assert r["ops"][0]["op"] == "perfui.n1.grid.sort" and r["ops"][0]["reps"] == [2, 0], r
    assert dict(r["ops"][0]["top"]) == {"DataGridRowsPresenter": 1, "ZoomDecorator": 1}, r
    print("self-test ok")
    return 0


def main(argv):
    if "--self-test" in argv:
        return self_test()
    paths = [a for a in argv if not a.startswith("--")]
    max_total = 0
    out = None
    for i, a in enumerate(argv):
        if a == "--max-total" and i + 1 < len(argv):
            max_total = int(argv[i + 1])
        if a == "--json" and i + 1 < len(argv):
            out = argv[i + 1]
    paths = [p for p in paths if p not in (str(max_total), out)]
    if not paths:
        print(__doc__)
        return 2
    with open(paths[0], encoding="utf-8", errors="replace") as f:
        lines = f.read().splitlines()
    per, kinds, tail, markers = count(lines)
    if markers == 0:
        print("표식 없음(perf-ui 로그가 아님)")
        return 2
    r = report(per, kinds, tail)
    for row in r["ops"]:
        print(f"{row['op']}\t{row['total']}\t{row['reps']}\t{row['top']}")
    print(f"total={r['total']} tail={tail} max_total={max_total}")
    if out:
        with open(out, "w", encoding="utf-8") as f:
            json.dump(r, f, ensure_ascii=False, indent=1)
    return 1 if r["total"] > max_total else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
