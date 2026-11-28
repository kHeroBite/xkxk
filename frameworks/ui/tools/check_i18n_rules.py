# 다국어 검사(Q17) 추가 규칙 — ④ 목록 컨트롤 표시 경로(ItemTemplate 없음, 허용 목록 밖은 실패)·⑤ 기동 전 문구 사전 두 사본 일치
import glob
import json
import os
import re

import host_identity  # 호스트 경로 단일 출처(check_i18n.py 가 tools 폴더를 sys.path 에 넣는다)

LIST_TAG = re.compile(r"<(ComboBox|ListBox|ItemsControl|ListView)\b([^>]*?)(/?)>", re.S)


# ④ 허용 목록(lead 02:0x, odev-2 점검): 파일:ItemsSource 단위로만 허용 — 그 밖의 새 경고는 실패로 잡는다
ITEMTPL_ALLOW = {
    ("src/xkxk.frameworks.ui.views/PdApp.axaml", "{Binding Shell.LogLines}"): "로그 줄은 기록 당시 언어를 유지하는 것이 의도",
    # 화면 분할로 MainView.axaml → ShellSettingsPanel·ShellStatusBar 로 옮겨져 옛 키가 끊겼다(w14 Q17 오탐 3건, odev-fix) — 같은 3건을 새 위치로 옮긴다
    # 요건 7: 인라인 패널이 통합 설정 창 페이지로 옮겨져 앞 2건의 위치를 따라 옮겼다
    ("src/xkxk.frameworks.ui.views/Settings/LanguagePage.axaml", "{Binding LanguageNames}"): "각 언어의 자기 이름(한국어·English) — 번역 대상 아님",
    ("src/xkxk.frameworks.ui.views/Settings/AppearancePage.axaml", "{Binding FontNames}"): "시스템 글꼴 이름 — 번역 대상 아님",
    ("src/xkxk.frameworks.ui.views/ShellStatusBar.axaml", "{Binding Extras.Projects}"): "프로젝트 이름은 i18n-ok 샘플 데이터 — 번역 대상 아님",
}


def check_host_copies(root, dicts, problems):
    """
    ⑤ 기동 전 문구 사전 두 사본 일치(odev-3 02:0x, lead 지시): 셸 Localization/host.<lang>.json ↔ 웹 wwwroot/host.i18n.json({lang:{키:값}}).
    main.js 가 .NET 이전에 웹 사본을 읽으므로 한쪽만 고치면 기동 화면과 앱 화면 문구가 어긋난다 → 언어 집합·키 집합·값이 모두 같아야 한다.
    """
    shell = dicts.get("host")
    # 웹 사본 위치는 실행 프로젝트(host_identity browser.wwwroot_src)가 정본이다 — wwwroot 가 src 에서 testbed 로 옮겨졌다(UADR-0018)
    webs = glob.glob(os.path.join(root, *host_identity.get("browser.wwwroot_src").split("/"), "host.i18n.json"))
    if not shell and not webs:
        return
    if not shell or not webs:
        problems.append("⑤ host 사전 사본 한쪽만 있음(셸 %s · 웹 %s)" % (bool(shell), [os.path.relpath(w, root) for w in webs]))
        return
    for w in webs:
        web = {k.lower(): v for k, v in json.load(open(w, encoding="utf-8-sig")).items()}
        rel = os.path.relpath(w, root)
        if set(web) != set(shell):
            problems.append("⑤ host 언어 집합 다름: 셸 %s · %s %s" % (sorted(shell), rel, sorted(web)))
        for lang in sorted(set(web) & set(shell)):
            a, b = shell[lang], web[lang]
            if set(a) != set(b):
                problems.append("⑤ host.%s 키 다름: 셸에만 %s · 웹에만 %s" % (lang, sorted(set(a) - set(b)), sorted(set(b) - set(a))))
            diff = [k for k in set(a) & set(b) if a[k] != b[k]]
            if diff:
                problems.append("⑤ host.%s 값 다름(%s): %s" % (lang, rel, sorted(diff)))


def scan_item_templates(path, text, warns):
    """
    ④ 표시 경로 경고(odev-7c 01:5x 제안 ①): ItemsSource 가 있는데 ItemTemplate·DisplayMemberBinding 이 없는 목록 컨트롤은
    항목을 ToString() 으로 그린다 — 만들 때 1번만 읽혀 언어를 바꿔도 다시 그리지 않는다(Android i0136 '기본 어둡게 (내장)' 잔존).
    항목 형식(문자열 목록인지·ToString 이 Loc 을 쓰는지)은 정적으로 알 수 없어 실패가 아니라 경고로 낸다.
    """
    for m in LIST_TAG.finditer(text):
        tag, attrs, selfclose = m.group(1), m.group(2), m.group(3)
        if "ItemsSource" not in attrs or "ItemTemplate" in attrs or "DisplayMemberBinding" in attrs:
            continue
        body = ""
        if not selfclose:
            end = text.find("</%s>" % tag, m.end())
            body = text[m.end():end if end >= 0 else m.end()]
        if "<%s.ItemTemplate" % tag in body or "<DataTemplate" in body:
            continue
        src = re.search(r'ItemsSource="([^"]*)"', attrs)
        warns.append((path, text[:m.start()].count("\n") + 1, tag, src.group(1) if src else "?"))  # main 이 허용 목록과 대조
