# UI-01·04·06·07 화면 안 스크린샷 쌍을 원격 명령으로 만들고 픽셀 통계(평균색·변경 영역)를 JSON 으로 남기는 스크립트
import json, os, subprocess, sys, hashlib
# 영역 루트 = 이 파일 위치(test/archive/rounds/desktop) 기준 네 단계 위(UTODO-0012)
UI_ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', '..', '..'))
sys.path.insert(0, os.path.join(UI_ROOT, 'tools'))
sys.path.insert(0, os.path.join(UI_ROOT, 'test', 'common'))  # D-TESTDIR: test/common/_paths.py
import _paths  # noqa: E402
_paths.add()
import pdctl
from retest_remote import shot_dir_ok
from PIL import Image, ImageChops, ImageStat

OUT = os.path.join(UI_ROOT, 'logs', 'otest', 'desktop', 'ui') + '/'
# 사용: python3 ui_shots.py <이번 실행의 임시 데이터 폴더 Windows 경로>(또는 PD_DATA_DIR). 앱 쪽 캡처 path 는 <폴더>\shots\<이름>.png 로 반드시 준다(D-SHOT1)
DATA = shot_dir_ok(sys.argv[1] if len(sys.argv) > 1 else os.environ.get('PD_DATA_DIR'))
if DATA is None:
    sys.exit('실행 거부: 임시 데이터 폴더(Windows 경로)를 인자 또는 PD_DATA_DIR 로 주라(사용자 폴더 불가) — path 없는 screenshot 금지(D-SHOT1)')
info = pdctl.load_info()


def call(cmd, **args):
    body = json.dumps({"cmd": cmd, "args": {k: v for k, v in args.items() if v is not None}}, ensure_ascii=False)
    r = json.loads(pdctl.curl(info, "api/cmd", body, timeout=90))
    return r.get("result") if r.get("ok") else {"_error": r.get("error")}


def shot(name, **a):
    p = OUT + name + '.png'
    a['path'] = DATA + '\\shots\\' + name + '.png'  # 앱이 이 경로에 쓴 뒤 바이트를 돌려준다(RemoteControlServer screenshot.download)
    pa = ['python3', os.path.join(UI_ROOT, 'tools', 'pdctl.py'), 'screenshot.download', 'out=' + p] + ['%s=%s' % kv for kv in a.items()]
    subprocess.run(pa, capture_output=True, timeout=60)
    return p


def stat(p):
    im = Image.open(p).convert('RGB')
    m = ImageStat.Stat(im).mean
    return {"file": os.path.basename(p), "size": im.size, "mean_rgb": [round(x, 1) for x in m],
            "md5": hashlib.md5(im.tobytes()).hexdigest()[:10]}


res = {}
# 준비: 문서 3종 + SDI 1
call("docs.close", all=True)
for k in ("grid", "gantt", "form"):
    call("docs.open", kind=k)
call("docs.arrange", mode="cascade")
call("docs.open", kind="grid", title="SDI-그리드", sdi=True)
call("wait.for", docCount=4, timeout=10000)

# UI-01/07: 내장 테마 6종 + Light/Dark 모드
themes = (call("themes.get") or {}).get("builtinThemes") or []
orig = (call("themes.get") or {}).get("themeId")
res["themes"] = []
for t in themes:
    r = call("theme.select", id=t)
    s = call("settings.get") or {}
    row = {"id": t, "activeThemeId": s.get("activeThemeId"), "theme": s.get("theme"), "actualTheme": s.get("actualTheme")}
    row["main"] = stat(shot("th_%s_main" % t, window=0))
    row["sdi"] = stat(shot("th_%s_sdi" % t, window=1))
    res["themes"].append(row)
call("theme.select", id=orig)

# UI-04 폰트: 3종 전후
fonts = call("settings.fonts") or []
res["fonts_available"] = fonts[:10] if isinstance(fonts, list) else fonts
base = shot("font_before", window=0)
res["fonts"] = []
for f in (fonts[:3] if isinstance(fonts, list) else []):
    call("settings.set", fontName=f)
    s = call("settings.get") or {}
    row = {"font": f, "fontName": s.get("fontName"), "shot": stat(shot("font_%d" % len(res["fonts"]), window=0))}
    res["fonts"].append(row)
call("settings.set", fontName=(call("settings.get") or {}).get("fontName"))

# UI-05 메인 창 위 Ctrl+휠 무시: 전후 픽셀(메인 크롬 영역) 비교
call("settings.set", wheelMode="FontSize")
call("input.ctrlWheel", delta=0)
b = shot("wheel_main_before", window=0)
r = call("input.ctrlWheel", delta=1, target="main")
a = shot("wheel_main_after", window=0)
d = ImageChops.difference(Image.open(b).convert('RGB'), Image.open(a).convert('RGB')).getbbox()
res["wheel_main"] = {"result": r, "diff_bbox": d}
# 문서 Ctrl+휠: 활성 문서 글자 변화
doc = (call("docs.list") or [{}])[0].get("id")
b = shot("wheel_doc_before", window=0)
call("input.ctrlWheel", delta=1, doc=doc); call("input.ctrlWheel", delta=1, doc=doc); call("input.ctrlWheel", delta=1, doc=doc)
a = shot("wheel_doc_after", window=0)
d2 = ImageChops.difference(Image.open(b).convert('RGB'), Image.open(a).convert('RGB')).getbbox()
res["wheel_doc"] = {"font_after": (call("settings.get") or {}).get("baseFontSize"), "diff_bbox": d2}
call("input.ctrlWheel", delta=0, doc=doc)

# UI-06 창 배율: 같은 문서 50/100/150/200%
call("settings.set", wheelMode="WindowZoom")
call("docs.activate", doc=doc)
res["zoom"] = []
for z in (0.5, 1.0, 1.5, 2.0):
    r = call("docs.zoom", doc=doc, zoom=z)
    res["zoom"].append({"zoom": z, "reported": (r or {}).get("zoom") if isinstance(r, dict) else r,
                        "shot": stat(shot("zoom_%d" % int(z * 100), doc=doc))})
call("docs.zoom", doc=doc, zoom=1.0)
call("settings.set", wheelMode="FontSize")
json.dump(res, open(OUT + 'ui_shots.json', 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
print('done')
