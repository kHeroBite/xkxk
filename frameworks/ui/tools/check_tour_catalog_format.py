# 도움말 카탈로그 검사 규칙 — 형식(⑤): JSON 문법, 필수·허용 필드, 열거값, 조건 이름, 제외 사유. check_tour_catalog_rules.py 가 부른다
import json
import re

CATALOG = "src/Pd.Poc.Shell/Tour/tour_catalog.json"
STEP_FILE = "src/Pd.Poc.Shell/Tour/TourStep.cs"
CONDITIONS = "src/Pd.Poc.Shell/Tour/TourConditions.cs"
ALLOWED = {
    "root": {"_note", "version", "steps", "shortcuts", "features", "menuRows", "coverage"},
    "step": {"id", "target", "text", "scope", "modes", "platforms", "requires", "covers", "_note"},
    "shortcut": {"id", "keys", "text", "webUnavailable", "macOnly", "binding", "handler", "_note"},
    "feature": {"id", "modes", "platforms", "requires", "covers", "_note"},
}
TARGET_FORM = re.compile(r"^(@\w+|[a-z]+\.[A-Za-z0-9.]+)$")


def enum_values(step_source, name):
    match = re.search(r"enum %s\s*\{(.*?)\}" % name, step_source, re.S)
    return set(re.findall(r"^\s*(\w+)\b", match.group(1), re.M)) if match else set()


def check_fields(kind, item, problems, required):
    label = "%s %s" % (kind, item.get("id", "?") if isinstance(item, dict) else item)
    if not isinstance(item, dict):
        problems.append("⑤ %s: 객체가 아님" % label)
        return False
    for field in sorted(set(item) - ALLOWED[kind]):
        problems.append("⑤ %s: 모르는 필드 %s" % (label, field))
    ok = True
    for field in required:
        if not isinstance(item.get(field), str) or not item[field]:
            problems.append("⑤ %s: 필수 필드 %s 없음" % (label, field))
            ok = False
    return ok


def check_flags(label, item, source, conditions, problems):
    for field, enum in (("modes", "TourModes"), ("platforms", "TourPlatforms")):
        if field in item:
            for token in str(item[field]).split("|"):
                if token.strip() not in enum_values(source, enum):
                    problems.append("⑤ %s: %s 값 '%s' 없음" % (label, field, token))
    needs = item.get("requires")
    if needs is not None and not needs.startswith("permission:") and needs not in conditions:
        problems.append("⑤ %s: 조건 '%s' 이 TourConditions 에 없음" % (label, needs))


def parse_catalog(src, problems):
    text = src.read(CATALOG)
    if text is None:
        problems.append("⑤ 카탈로그 파일 없음: %s" % CATALOG)
        return None
    try:
        data = json.loads(text)
    except ValueError as ex:
        problems.append("⑤ 카탈로그 JSON 문법 오류: %s" % ex)
        return None
    if not isinstance(data, dict):
        problems.append("⑤ 카탈로그 최상위가 객체가 아님")
        return None
    if "texts" in data:
        problems.append("⑤ 저장소 카탈로그에 texts 금지 — 문구는 sysview.*.json 사전에 둔다")
    for field in sorted(set(data) - ALLOWED["root"] - {"texts"}):
        problems.append("⑤ 모르는 최상위 필드: %s" % field)
    if not isinstance(data.get("version"), int):
        problems.append("⑤ version 이 정수가 아님")
    return data


def check_format(data, src, problems):
    """형식 검사 뒤 쓸 수 있는 항목만 돌려준다."""
    source = src.read(STEP_FILE) or ""
    conditions = set(re.findall(r'\["(\w+)"\]\s*=', src.read(CONDITIONS) or ""))
    scopes = enum_values(source, "TourScope")
    steps = []
    for step in data.get("steps") or []:
        if check_fields("step", step, problems, ("id", "target", "scope")):
            steps.append(step)
            label = "단계 %s" % step["id"]
            if step["scope"] not in scopes:
                problems.append("⑤ %s: scope 값 '%s' 없음" % (label, step["scope"]))
            if not TARGET_FORM.match(step["target"]):
                problems.append("⑤ %s: target 형식 오류 '%s'" % (label, step["target"]))
            check_flags(label, step, source, conditions, problems)
    entries = [s for s in data.get("shortcuts") or [] if check_fields("shortcut", s, problems, ("id", "keys"))]
    features = []
    for feature in data.get("features") or []:
        if check_fields("feature", feature, problems, ("id",)):
            features.append(feature)
            check_flags("기능 %s" % feature["id"], feature, source, conditions, problems)
    rows = data.get("menuRows") or {}
    for name in (rows.get("requires") or {}).values():
        if name not in conditions:
            problems.append("⑤ menuRows: 조건 '%s' 이 TourConditions 에 없음" % name)
    for item in (data.get("coverage") or {}).get("exclude") or []:
        if not (isinstance(item, dict) and item.get("id") and str(item.get("reason", "")).strip()):
            problems.append("⑤ 커버리지 제외 항목에 id·reason 필요: %s" % (item,))
    return steps, entries, features
