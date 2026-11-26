# 옛 셸 싱글톤 접근자(UiSettings·ThemeCatalog·TourCatalog·MonitorMenuViewModel·데스크톱 Instance 2종) 직접 참조·위임 정의·정적 상태 인벤토리를 확인하는 판정기 — B05 끝 조건
# 사용: context_services_check.py [--root <R>] [--scope all|refs|defs] [--report <json>] [--self-test] [--help]
#   refs : src 아래 *.cs·*.axaml(bin·obj 제외, 주석 //·/* */·<!-- --> 제외)에서 옛 접근자 직접 참조(한정 이름·global::·axaml x:Static 포함)와
#          using static 을 센다. 위임 접근자 선언 줄('public static <형식> Current|Instance|Provider')은 참조가 아니다
#   defs : 정의 형태 — UiSettings·ThemeCatalog·TourCatalog·MonitorMenuViewModel 의 '=> XkxkUi.Current.<서비스>' 위임, ThemeCatalog·
#          MonitorMenuViewModel 공개 생성자 0, MonitorMenuViewModel.Provider 위임·CurrentProvider, TourCatalog·DesignThemeResolver*
#          정적 가변 상태 0(WindowOverviewHost 는 범위 밖), 데스크톱 DesktopMonitorProvider·DesktopWindowHost 의 정적 Instance 0
#   --report <json> : 위 판정과 함께 src 의 정적 상태 인벤토리(category: migrated·host_injection·delegate_hook·diagnostics·cache·other)를 JSON 으로 쓴다
#   --scope refs|defs : 한쪽만 본다(defs 만이면 파일 수 하한 없음)
# 출력: REF <상대경로>:<줄> <일치> / DEF FAIL <사유> / 끝 줄 'files=N refs=M defs=ok|fail|skip inventory=K'
# 종료코드: 0=PASS(refs=0·defs=ok), 1=FAIL, 2=인자 오류·대상 없음·훑은 파일 N<100(판정불가)
import datetime
import json
import os
import re
import shutil
import sys
import tempfile

from app_context_check import strip_cs, strip_xml  # 같은 폴더의 B04 판정기 — 주석 제거 규칙을 한 곳에 둔다

SRC = "src"
SKIP_DIRS = {"bin", "obj"}
MIN_FILES = 100
SH = "src/xkxk.frameworks.ui.shell/"
DK = "src/xkxk.frameworks.ui.desktop/"
PFX = r"(?<![\w.])(?:global::)?(?:xkxk\.frameworks\.ui\.shell\.)?(?:Theming\.|Tour\.|ViewModels\.)?"
REF = re.compile(PFX + r"(?:UiSettings\.Current|ThemeCatalog\.Current|TourCatalog\.Current|MonitorMenuViewModel\.(?:Instance|Provider))\b"
                 r"|(?<![\w.])(?:global::)?(?:xkxk\.frameworks\.ui\.desktop\.)?(?:DesktopMonitorProvider|DesktopWindowHost)\.Instance\b"
                 r"|\busing\s+static\s+(?:global::)?xkxk\.frameworks\.ui\.(?:shell\.(?:Theming\.(?:UiSettings|ThemeCatalog)|Tour\.TourCatalog"
                 r"|ViewModels\.MonitorMenuViewModel)|desktop\.(?:DesktopMonitorProvider|DesktopWindowHost))\s*;")
DEF_LINE = re.compile(r"public\s+static\s+\S+\s+(?:Current|Instance|Provider)\b")
MIGRATED_FILES = ("Theming/UiSettings.cs", "Theming/ThemeCatalog.cs", "Tour/TourCatalog.cs", "ViewModels/MonitorMenuViewModel.cs",
                  "Theming/DesignThemeState.cs", "Framework/UiAppContext.cs")
HOST_DIRS = ("src/xkxk.frameworks.ui.desktop/", "src/xkxk.frameworks.ui.android/", "src/xkxk.frameworks.ui.browser/")
MODS = r"^\s*(?:\[[^\]]*\]\s*)*(?:(?:private|internal|public|protected|new|unsafe)\s+)*static\s+"
TYPE = r"([\w<>\[\],?.() ]+?)"
FIELD = re.compile(MODS + r"(?!readonly\b|class\b|partial\b|extern\b|void\b|event\b|async\b|implicit\b|explicit\b|operator\b|abstract\b)"
                   r"(?:volatile\s+)?" + TYPE + r"\s+(\w+)\s*(?:;|=(?!>))")
AUTOPROP = re.compile(MODS + r"(?!readonly\b)" + TYPE + r"\s+(\w+)\s*\{\s*(?:(?:public|private|internal|protected)\s+)?get;\s*"
                      r"(?:(?:private|internal|protected)\s+)?set;")
SINGLETON = re.compile(MODS + TYPE + r"\s+(\w+)\s*\{\s*get;\s*\}\s*=")
EVENT = re.compile(MODS + r"event\s+" + TYPE + r"\s+(\w+)\s*;")
TYPE_DECL = re.compile(r"\b(?:class|struct|record|interface)\s+(\w+)")
USAGE = "사용: context_services_check.py [--root <R>] [--scope all|refs|defs] [--report <json>] [--self-test] [--help]"


def read(path):
    with open(path, encoding="utf-8-sig", errors="replace") as f:
        return f.read()


def find_refs(text, axaml=False, stripped=False):
    """(줄, 일치문자열) 목록. 위임 접근자 선언 줄은 제외한다. stripped=True 면 text 는 이미 주석을 걷어 낸 본문이다."""
    body = text if stripped else (strip_xml(text) if axaml else strip_cs(text))
    hits = []
    for no, line in enumerate(body.split("\n"), 1):
        if DEF_LINE.search(line):
            continue
        for m in REF.finditer(line):
            hits.append((no, " ".join(m.group(0).split())))
    return hits


def split_decl(typ, name):
    """(형식, [이름…]) — 기본값 매개변수를 가진 메서드처럼 괄호가 짝이 안 맞으면 None, 'long a, b' 다중 선언은 이름을 나눈다."""
    flat, prev = typ, None
    while flat != prev:
        prev, flat = flat, re.sub(r"<[^<>]*>|\([^()]*\)", "", flat)
    if "(" in flat or ")" in flat or "<" in flat or ">" in flat:
        return None
    if "," not in flat:
        return typ.strip(), [name]
    parts, depth, cur = [], 0, ""
    for ch in typ:
        depth += ch in "<(" and 1 or (ch in ">)" and -1 or 0)
        if ch == "," and depth == 0:
            parts.append(cur)
            cur = ""
        else:
            cur += ch
    parts.append(cur)
    parts = parts[:1] + [p for p in parts[1:] if p.strip()]  # 'long a, b;' 는 형식이 'long a,' 로 잡혀 끝 조각이 빈다
    head = parts[0].strip().rsplit(None, 1)
    if len(head) != 2 or not all(re.fullmatch(r"\s*\w+\s*", p) for p in parts[1:]):
        return None
    return head[0], [head[1]] + [p.strip() for p in parts[1:]] + [name]


def static_state(text, stripped=False):
    """정적 상태 선언 [(줄, kind, 형식, 이름, 둘러싼 형식)]. kind=field|auto_property|singleton|event. 주석은 제외."""
    out, owner = [], ""
    for no, line in enumerate((text if stripped else strip_cs(text)).split("\n"), 1):
        t = TYPE_DECL.search(line)
        if t:
            owner = t.group(1)
        for kind, rx in (("event", EVENT), ("auto_property", AUTOPROP), ("singleton", SINGLETON), ("field", FIELD)):
            m = rx.search(line)
            if m:
                d = split_decl(m.group(1), m.group(2))
                if d:
                    out += [(no, kind, d[0], n, owner) for n in d[1]]
                break
    return out


def classify(rel, kind, typ, name, owner):
    """인벤토리 category 를 정한다(B05 범위 밖 항목의 배정 batch 는 a7 인벤토리 표가 정본)."""
    if name == "FontResolver" or (rel.startswith(HOST_DIRS)):
        return "host_injection"
    if owner == "WindowOverviewHost" or kind == "event" or re.match(r"(?:Func|Action|Predicate|EventHandler)\b", typ):
        return "delegate_hook"
    if re.search(r"Perf|Probe|Diag|Trace|Log|Remote|Automation|Stopwatch", rel + "/" + owner + "/" + name):
        return "diagnostics"
    if any(rel.endswith(f) for f in MIGRATED_FILES) or "/Theming/DesignThemeResolver" in rel:
        return "migrated"
    if re.search(r"[Cc]ache|_last|s_last|Memo|Pool", name) or re.match(r"(?:Dictionary|ConcurrentDictionary|List|HashSet)\b", typ):
        return "cache"
    return "other"


def mutable(text, cut=None):
    """정적 가변 상태(필드·get;set; 자동 속성·field-like 이벤트) 선언 문자열 목록. cut 이 있으면 그 문자열 앞까지만 본다."""
    if cut and cut in text:
        text = text.split(cut)[0]
    return ["%d:%s %s" % (no, typ, name) for no, kind, typ, name, _ in static_state(text) if kind in ("field", "auto_property", "event")]


def check_defs(src):
    """src = {상대경로: 원문}. 실패 사유 목록(빈 목록이면 ok). 파일이 없으면 그 자체가 실패 사유."""
    bad = []

    def body(rel):
        if rel not in src:
            bad.append("정의 파일 없음: %s" % rel)
            return None
        return strip_cs(src[rel])

    def delegate(rel, typ, name, target):
        s = body(rel)
        if s is None:
            return None
        if not re.search(r"public\s+static\s+%s\s+%s\s*=>\s*XkxkUi\.Current\.%s\s*;" % (typ, name, target), s):
            bad.append("%s.%s 가 '=> XkxkUi.Current.%s' 위임이 아님" % (typ, name, target))
        if re.search(r"static\s+%s\s+%s\s*\{\s*get;\s*\}\s*=" % (typ, name), s):
            bad.append("%s.%s 단일 인스턴스 자동 속성이 남음" % (typ, name))
        return s
    delegate(SH + "Theming/UiSettings.cs", "UiSettings", "Current", "Settings")
    tc = delegate(SH + "Theming/ThemeCatalog.cs", "ThemeCatalog", "Current", "Themes")
    if tc is not None and re.search(r"public\s+ThemeCatalog\s*\(", tc):
        bad.append("ThemeCatalog 공개 생성자가 있음")
    tr = delegate(SH + "Tour/TourCatalog.cs", "TourCatalogData", "Current", "Tour")
    if tr is not None and mutable(src[SH + "Tour/TourCatalog.cs"]):
        bad.append("TourCatalog 에 정적 가변 상태가 남음: %s" % mutable(src[SH + "Tour/TourCatalog.cs"])[:3])
    mm = delegate(SH + "ViewModels/MonitorMenuViewModel.cs", "MonitorMenuViewModel", "Instance", "Monitors")
    if mm is not None:
        if re.search(r"public\s+MonitorMenuViewModel\s*\(", mm):
            bad.append("MonitorMenuViewModel 공개 생성자가 있음")
        if not re.search(r"public\s+IMonitorProvider\?\s*CurrentProvider\b", mm):
            bad.append("MonitorMenuViewModel.CurrentProvider 인스턴스 속성 없음")
        if not (re.search(r"get\s*=>\s*XkxkUi\.Current\.Monitors\.CurrentProvider\s*;", mm) and re.search(r"set\s*=>\s*XkxkUi\.Current\.Monitors\.Attach\(\s*value\s*\)", mm)):
            bad.append("MonitorMenuViewModel.Provider 가 XkxkUi.Current.Monitors(CurrentProvider·Attach) 위임이 아님")
    dtr = sorted(r for r in src if re.match(re.escape(SH) + r"Theming/DesignThemeResolver[\w.]*\.cs$", r))
    if len(dtr) < 3:
        bad.append("DesignThemeResolver 부분 파일 %d개 < 3" % len(dtr))
    for rel in dtr:
        left = mutable(src[rel], "public static class WindowOverviewHost")
        if left:
            bad.append("%s 에 정적 가변 상태가 남음: %s" % (os.path.basename(rel), left[:3]))
    for rel, cls in ((DK + "DesktopMonitorProvider.cs", "DesktopMonitorProvider"), (DK + "DesktopWindowHost.cs", "DesktopWindowHost")):
        s = body(rel)
        if s is not None and re.search(r"\bstatic\s+%s\s+Instance\b" % cls, s):
            bad.append("%s.Instance 정적 단일 인스턴스 접근자가 남음" % cls)
    return bad


def walk(root):
    """[(상대경로, 절대경로)] — src 아래 *.cs·*.axaml, bin·obj 제외. src 가 없으면 None."""
    base = os.path.join(root, SRC)
    if not os.path.isdir(base):
        print("대상 폴더 없음: %s" % SRC, file=sys.stderr)
        return None
    out = []
    for dp, dns, fns in os.walk(base):
        dns[:] = sorted(d for d in dns if d not in SKIP_DIRS)
        for fn in sorted(fns):
            if fn.endswith((".cs", ".axaml")):
                p = os.path.join(dp, fn)
                out.append((os.path.relpath(p, root).replace(os.sep, "/"), p))
    return out


def run(root, scope, report, quiet=False):
    say = (lambda *a, **k: None) if quiet else print
    files = walk(root)
    if files is None:
        return 2
    texts = {rel: read(p) for rel, p in files}
    defs = None if scope == "refs" else check_defs({r: t for r, t in texts.items() if r.endswith(".cs")})
    hits, inv = [], []
    body = {} if scope == "defs" and not report else {r: strip_xml(t) if r.endswith(".axaml") else strip_cs(t) for r, t in texts.items()}
    if scope != "defs":
        for rel, b in body.items():
            hits += [(rel, no, m) for no, m in find_refs(b, stripped=True)]
    if report:
        for rel, b in body.items():
            if rel.endswith(".cs"):
                for no, kind, typ, name, owner in static_state(b, stripped=True):
                    inv.append({"file": rel, "line": no, "kind": kind, "type": typ, "member": "%s.%s" % (owner, name) if owner else name,
                                "category": classify(rel, kind, typ, name, owner)})
    for rel, no, m in hits:
        say("REF %s:%d %s" % (rel, no, m))
    for d in defs or []:
        say("DEF FAIL %s" % d)
    n = 0 if scope == "defs" else len(files)
    dstate = "skip" if defs is None else ("fail" if defs else "ok")
    say("files=%d refs=%s defs=%s inventory=%d" % (n, "skip" if scope == "defs" else len(hits), dstate, len(inv)))
    rc = 1 if hits or defs else 0
    if scope != "defs" and n < MIN_FILES:
        print("훑은 파일 %d < %d — 대상 소실 의심(판정불가)" % (n, MIN_FILES), file=sys.stderr)
        rc = 2
    if report:
        cats = {}
        for x in inv:
            cats[x["category"]] = cats.get(x["category"], 0) + 1
        data = {"generated_at": datetime.datetime.now().astimezone().isoformat(timespec="seconds"), "root": root, "scope": scope,
                "files": n, "refs": [{"file": r, "line": no, "match": m} for r, no, m in hits], "defs": defs or [],
                "defs_state": dstate, "rc": rc, "categories": cats, "inventory": inv}
        os.makedirs(os.path.dirname(os.path.abspath(report)), exist_ok=True)
        with open(report, "w", encoding="utf-8") as f:
            json.dump(data, f, ensure_ascii=False, indent=1)
            f.write("\n")
    return rc


GOOD = {
    SH + "Theming/UiSettings.cs": "public sealed class UiSettings {\n    public UiSettings() { }\n    public static UiSettings Current => XkxkUi.Current.Settings;\n"
                                  "    public static Func<string, FontFamily> FontResolver { get; set; } = n => new FontFamily(n);\n}\n",
    SH + "Theming/ThemeCatalog.cs": "public sealed class ThemeCatalog {\n    internal ThemeCatalog() { }\n    public static ThemeCatalog Current => XkxkUi.Current.Themes;\n}\n",
    SH + "Tour/TourCatalog.cs": "public sealed class EmbeddedTourCatalogSource { }\npublic static class TourCatalog {\n"
                                "    public static TourCatalogData Current => XkxkUi.Current.Tour;\n    public static void UseSource(ITourCatalogSource? s) => UiAppContext.Instance.UseTourSource(s);\n}\n",
    SH + "ViewModels/MonitorMenuViewModel.cs": "public sealed class MonitorMenuViewModel {\n    internal MonitorMenuViewModel() { }\n"
                                               "    public static MonitorMenuViewModel Instance => XkxkUi.Current.Monitors;\n    public IMonitorProvider? CurrentProvider => _provider;\n"
                                               "    public static IMonitorProvider? Provider\n    {\n        get => XkxkUi.Current.Monitors.CurrentProvider;\n"
                                               "        set => XkxkUi.Current.Monitors.Attach(value);\n    }\n    private IMonitorProvider? _provider;\n}\n",
    SH + "Theming/DesignThemeResolver.cs": "public static partial class DesignThemeResolver {\n    private static readonly IReadOnlyDictionary<string, string> NoOverrides = new();\n"
                                           "    private static DesignThemeState S => UiAppContext.Instance.DesignTheme;\n    public static Func<int>? WindowCount { get => S.WindowCount; set => S.WindowCount = value; }\n"
                                           "    public static event Action? Changed { add => S.Changed += value; remove => S.Changed -= value; }\n}\n"
                                           "public static class WindowOverviewHost {\n    public static Func<bool>? Open { get; set; }\n}\n",
    SH + "Theming/DesignThemeResolver.Motion.cs": "public static partial class DesignThemeResolver {\n    public static MotionPlatform Platform { get => S.Platform; set => S.Platform = value; }\n}\n",
    SH + "Theming/DesignThemeResolver.Elements.cs": "public static partial class DesignThemeResolver {\n    internal static string? ElementThemeOverride { get => S.Element; set => S.Element = value; }\n}\n",
    DK + "DesktopMonitorProvider.cs": "internal sealed class DesktopMonitorProvider : IMonitorProvider {\n    public DesktopMonitorProvider() { }\n}\n",
    DK + "DesktopWindowHost.cs": "internal sealed class DesktopWindowHost : IWindowHost {\n    private static Window? s_main;\n}\n",
}


def self_test():
    bad = 0

    def report(ok, name, detail=""):
        nonlocal bad
        bad += 0 if ok else 1
        print("SELFTEST %s %s%s" % ("PASS" if ok else "FAIL", name, detail))
    ref_cases = [
        ("ref_old_accessors_detect", "var a = UiSettings.Current; var b = ThemeCatalog.Current.Requested; TourCatalog.Current.Steps;\n"
                                     "MonitorMenuViewModel.Instance.Items; MonitorMenuViewModel.Provider = p;", False, 5),
        ("ref_qualified_detect", "global::xkxk.frameworks.ui.shell.Theming.UiSettings.Current.X; Theming.UiSettings.Current.Y;\n"
                                 "xkxk.frameworks.ui.shell.ViewModels.MonitorMenuViewModel.Instance.Z;", False, 3),
        ("ref_host_instance_detect", "var m = DesktopMonitorProvider.Instance; DesktopWindowHost.Instance.Execute(c);", False, 2),
        ("ref_using_static_detect", "using static xkxk.frameworks.ui.shell.Theming.UiSettings;\nusing static global::xkxk.frameworks.ui.desktop.DesktopWindowHost;", False, 2),
        ("ref_comment_ignored", "// UiSettings.Current\n/* ThemeCatalog.Current\n TourCatalog.Current */ var u = \"http://x\"; // DesktopWindowHost.Instance", False, 0),
        ("ref_def_line_and_lookalike_ignored", "public static UiSettings Current => XkxkUi.Current.Settings;\npublic static MonitorMenuViewModel Instance => XkxkUi.Current.Monitors;\n"
                                               "XkxkUi.Current.Settings.X; MyUiSettings.Current; LayoutModeGate.Instance; PerfProbe.Current; DesignThemeResolver.Current;"
                                               " UiSettings.CurrentTheme; using static xkxk.frameworks.ui.shell.Diagnostics.PerfStats;", False, 0),
        ("ref_axaml_xstatic_detect_comment_ignored", "<!-- {x:Static theming:UiSettings.Current}\n -->\n<X Text=\"{Binding A, Source={x:Static theming:UiSettings.Current}}\"/>\n"
                                                     "<M Items=\"{Binding Items, Source={x:Static vm:MonitorMenuViewModel.Instance}}\"/>\n"
                                                     "<Y Text=\"{Binding Settings.A, Source={x:Static fw:XkxkUi.Current}}\"/>", True, 2),
    ]
    for name, src, axaml, want in ref_cases:
        got = len(find_refs(src, axaml))
        report(got == want, name, " (기대 %d, 실측 %d)" % (want, got))
    old = dict(GOOD)
    old[SH + "Theming/UiSettings.cs"] = old[SH + "Theming/UiSettings.cs"].replace("Current => XkxkUi.Current.Settings;", "Current { get; } = new();")
    old[SH + "Theming/ThemeCatalog.cs"] = old[SH + "Theming/ThemeCatalog.cs"].replace("internal ThemeCatalog()", "public ThemeCatalog()")
    tour = dict(GOOD)
    tour[SH + "Tour/TourCatalog.cs"] = tour[SH + "Tour/TourCatalog.cs"].replace("public static TourCatalogData", "private static TourCatalogData? s_current;\n    public static TourCatalogData")
    mon = dict(GOOD)
    mon[SH + "ViewModels/MonitorMenuViewModel.cs"] = mon[SH + "ViewModels/MonitorMenuViewModel.cs"].replace("set => XkxkUi.Current.Monitors.Attach(value);", "set => Instance.Attach(value);")
    dtr = dict(GOOD)
    dtr[SH + "Theming/DesignThemeResolver.cs"] = dtr[SH + "Theming/DesignThemeResolver.cs"].replace(
        "    private static DesignThemeState S", "    private static string _selected = \"\";\n    public static event Action? Requested;\n    private static DesignThemeState S")
    dtr[SH + "Theming/DesignThemeResolver.Elements.cs"] = dtr[SH + "Theming/DesignThemeResolver.Elements.cs"].replace(
        "{ get => S.Element; set => S.Element = value; }", "{ get; set; }")
    host = dict(GOOD)
    host[DK + "DesktopWindowHost.cs"] = host[DK + "DesktopWindowHost.cs"].replace("private static Window? s_main;", "public static DesktopWindowHost Instance { get; } = new();")
    gone = dict(GOOD)
    del gone[SH + "Theming/DesignThemeResolver.Motion.cs"]
    def_cases = [("defs_delegate_ok", GOOD, 0), ("defs_singleton_and_public_ctor_fail", old, 3), ("defs_tour_static_state_fail", tour, 1),
                 ("defs_provider_not_delegate_fail", mon, 1), ("defs_design_theme_mutable_fail", dtr, 2), ("defs_host_instance_fail", host, 1),
                 ("defs_missing_part_file_fail", gone, 1)]
    for name, src, want in def_cases:
        got = check_defs(src)
        report(len(got) == want, name, " (기대 실패 %d, 실측 %d%s)" % (want, len(got), "" if len(got) == want else " " + str(got)))
    inv = {}
    for rel, t in list(GOOD.items()) + [(SH + "Diagnostics/PerfProbe.cs", "public sealed class PerfProbe {\n    public static PerfProbe Current { get; } = new();\n}\n"),
                                        (SH + "Docking/Snap.cs", "public static class Snap {\n    private static Dictionary<string, Rect> s_rects = new();\n    private static int s_count;\n"
                                         "    private static long s_a, s_b;\n    public static (int A, int B) Calc(int x, double y = 0) => default;\n"
                                         "    static bool Try(Window w, PixelPoint? o = null) => true;\n"
                                         "    public static Func<IReadOnlyList<(string W, uint? A)>>? Probe { get; set; }\n}\n")]:
        for no, kind, typ, name, owner in static_state(t):
            inv["%s.%s" % (owner, name)] = (kind, classify(rel, kind, typ, name, owner))
    want = {"UiSettings.FontResolver": ("auto_property", "host_injection"), "WindowOverviewHost.Open": ("auto_property", "delegate_hook"),
            "DesktopWindowHost.s_main": ("field", "host_injection"), "PerfProbe.Current": ("singleton", "diagnostics"),
            "Snap.s_rects": ("field", "cache"), "Snap.s_count": ("field", "other"), "Snap.s_a": ("field", "other"), "Snap.s_b": ("field", "other"),
            "Snap.Probe": ("auto_property", "delegate_hook")}
    miss = {k: (v, inv.get(k)) for k, v in want.items() if inv.get(k) != v}
    extra = sorted(k for k in inv if k not in want)
    report(not miss and not extra, "inventory_kind_category", "" if not miss and not extra else " 불일치 %s 초과 %s" % (miss, extra))
    tmp = tempfile.mkdtemp()
    try:
        report(run(tmp, "all", None, quiet=True) == 2, "missing_src_unjudgeable")
        for rel, t in GOOD.items():
            os.makedirs(os.path.dirname(os.path.join(tmp, rel)), exist_ok=True)
            with open(os.path.join(tmp, rel), "w", encoding="utf-8") as f:
                f.write(t)
        os.makedirs(os.path.join(tmp, SRC, "p", "obj"))
        with open(os.path.join(tmp, SRC, "p", "obj", "G.cs"), "w", encoding="utf-8") as f:
            f.write("class G { void M() { var x = UiSettings.Current; } }\n")
        rep = os.path.join(tmp, "out", "r.json")
        rc = run(tmp, "all", rep, quiet=True)
        d = json.loads(read(rep)) if os.path.isfile(rep) else {}
        ok = rc == 2 and d.get("files") == len(GOOD) and not d.get("refs") and d.get("defs_state") == "ok" and all(x.get("category") for x in d.get("inventory", [1]))
        report(ok, "few_files_unjudgeable_obj_pruned_report", " (기대 rc 2, 실측 %d)" % rc)
        report(run(tmp, "defs", None, quiet=True) == 0, "scope_defs_no_file_floor")
    finally:
        shutil.rmtree(tmp, ignore_errors=True)
    return 1 if bad else 0


def main(argv):
    root = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", ".."))
    args, scope, report = argv[1:], "all", None
    if args in (["--help"], ["-h"]):
        print(USAGE)
        return 0
    if args == ["--self-test"]:
        return self_test()
    i = 0
    while i < len(args):
        if args[i] == "--root" and i + 1 < len(args):
            root = os.path.abspath(args[i + 1])
            i += 2
        elif args[i] == "--scope" and i + 1 < len(args) and args[i + 1] in ("all", "refs", "defs"):
            scope = args[i + 1]
            i += 2
        elif args[i] == "--report" and i + 1 < len(args):
            report = os.path.abspath(args[i + 1])
            i += 2
        else:
            print(USAGE, file=sys.stderr)
            return 2
    return run(root, scope, report)


if __name__ == "__main__":
    sys.exit(main(sys.argv))
