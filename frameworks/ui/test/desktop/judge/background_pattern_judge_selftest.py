# AC-014 판정기(background_pattern_judge.py)의 자기시험 — 앱 없이 합성 이미지로 판정 함수의 PASS·FAIL·INVALID 를 고정 확인한다
"""
직접 실행하지 않는다. `background_pattern_judge.py --self-test` 가 이 파일의 run() 을 부른다(300줄 한도로 판정기에서 분리).
판정 로직은 판정기에만 있고 이 파일은 그 함수들을 부르기만 한다.
"""
import background_pattern_judge as m
import judge_session as session


def run():
    from PIL import Image, ImageDraw

    def canvas(fill_work=None, fill_doc=None):
        im = Image.new("RGB", (400, 300), (30, 30, 30))
        d = ImageDraw.Draw(im)
        if fill_work:
            for x in range(0, 400, 20):
                d.line([(x, 0), (x, 299)], fill=fill_work, width=6)
        d.rectangle((100, 80, 250, 200), fill=(200, 200, 200))
        if fill_doc:
            d.rectangle((120, 100, 230, 180), fill=fill_doc)
        return im

    def dots(step=24, rows=99, level=49, doc_change=False):
        """바탕 (30,30,30) 위 2x2 점(채널 차 level-30), 문서 창 자리 밖에만."""
        im = canvas()
        px = im.load()
        for j, y in enumerate(range(11, 300, step)):
            if j >= rows:
                break
            for x in range(11, 400, step):
                if not (100 <= x < 252 and 80 <= y < 202):
                    for dx in (0, 1):
                        for dy in (0, 1):
                            if x + dx < 400 and y + dy < 300:
                                px[x + dx, y + dy] = (level, level, level)
        if doc_change:
            ImageDraw.Draw(im).rectangle((120, 100, 230, 180), fill=(0, 0, 255))
        return im

    work, doc = (0, 0, 400, 300), (100, 80, 250, 200)
    base = canvas()
    st = {"decorEffective": "image", "fallbackCount": 0, "lastFallback": None}
    ok_after = {"decorEffective": "none", "fallbackCount": 1, "lastFallback": "background.fail.decode"}
    fits = {"a": canvas((255, 0, 0)), "b": canvas((0, 255, 0)), "c": canvas((0, 0, 255))}
    return session.run_cases([
        ("무늬 그려짐 PASS", m.judge_drawn(base, canvas((255, 0, 0)), work, doc)["verdict"] == "PASS"),
        ("무늬 없음 FAIL", m.judge_drawn(base, canvas(), work, doc)["verdict"] == "FAIL"),
        ("문서 창 안 변화 FAIL", m.judge_drawn(base, canvas((255, 0, 0), (0, 0, 255)), work, doc)["verdict"] == "FAIL"),
        ("크기 다름 INVALID", m.judge_drawn(base, base.resize((200, 150)), work, doc)["verdict"] == "INVALID"),
        ("맞춤 서로 다름 PASS", m.judge_distinct(fits, work, doc)["verdict"] == "PASS"),
        ("맞춤 같은 쌍 FAIL", m.judge_distinct(dict(fits, d=fits["a"].copy()), work, doc)["verdict"] == "FAIL"),
        ("손상 PASS", m.judge_corrupt(st, ok_after, 1, 0)["verdict"] == "PASS"),
        ("손상 뒤 none 아님 FAIL", m.judge_corrupt(st, dict(ok_after, decorEffective="image"), 1, 0)["verdict"] == "FAIL"),
        ("폴백 횟수 그대로 FAIL", m.judge_corrupt(st, dict(ok_after, fallbackCount=0), 1, 0)["verdict"] == "FAIL"),
        ("알림 없음 FAIL", m.judge_corrupt(st, ok_after, 0, 0)["verdict"] == "FAIL"),
        ("재손상 알림 중복 FAIL", m.judge_corrupt(st, ok_after, 1, 1)["verdict"] == "FAIL"),
        ("pngdiff 문턱 재사용(채널 16 이하 무시)", m.region_diff(base, Image.new("RGB", (400, 300), (40, 40, 40)), (0, 0, 60, 60))[0] == 0),
        ("dots 성긴 점 PASS", m.judge_sparse(base, dots(), work, doc, 24, 1)["verdict"] == "PASS"),
        ("dots 무늬 없음 FAIL", m.judge_sparse(base, canvas(), work, doc, 24, 1)["verdict"] == "FAIL"),
        ("dots + 문서 창 안 변화 FAIL", m.judge_sparse(base, dots(doc_change=True), work, doc, 24, 1)["verdict"] == "FAIL"),
        ("dots 간격 틀림(37px) FAIL", m.judge_sparse(base, dots(step=37), work, doc, 24, 1)["verdict"] == "FAIL"),
        ("dots 너무 적음 FAIL", m.judge_sparse(base, dots(rows=1), work, doc, 24, 1)["verdict"] == "FAIL"),
        ("dots 채널 차 16 이하 FAIL(문턱 불변)", m.judge_sparse(base, dots(level=46), work, doc, 24, 1)["verdict"] == "FAIL"),
        ("dots 이미 타일 통과면 그대로", m.judge_sparse(base, canvas((255, 0, 0)), work, doc, 24, 1)["sparse"].startswith("미적용")),
        ("실측 912x631 전체 덮음 → 줄이기", m.shrink_deltas({"w": 912, "h": 631}, {"w": 912, "h": 631}) == (-456, -316)),
        ("이미 작으면 줄이지 않음", m.shrink_deltas({"w": 912, "h": 631}, {"w": 300, "h": 200}) == (0, 0)),
        ("전체 덮음 가드 INVALID 대상", m.covers((204, 70, 1116, 701), (204, 70, 1116, 701))),
        ("줄인 뒤 456x315 가드 통과", not m.covers((204, 70, 1116, 701), (204, 70, 660, 385))),
        ("otest-1 확인값 512x331 가드 통과", not m.covers((0, 0, 912, 631), (0, 0, 512, 331))),
        ("가드 기준 70% 그대로", m.COVER_LIMIT == 0.7 and m.covers((0, 0, 100, 100), (0, 0, 70, 100))),
        ("무늬 8종·맞춤 5방식", len(m.PATTERNS) == 8 and m.FITS == ("fill", "fit", "stretch", "tile", "center")),
    ])
