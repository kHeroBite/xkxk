#!/usr/bin/env python3
# 템플릿 부품 이름 계약 검사 — TemplateParts 상수의 이름이 Views 템플릿 정의나 Dock 패키지에 실재하는지 판정한다(check_layers.py 가 호출)
import glob
import os
import re

CONST_STRING = re.compile(r'\bconst\s+string\s+(\w+)\s*=\s*"([^"]*)"')
PREFIX = "PART_"


def in_dock(name, blob):
    """Dock DLL 사용자 문자열(UTF-16)에 그 이름이 있는지. 뒤에 글자가 이어지면 다른 이름이다."""
    return blob is not None and re.search(re.escape(name.encode("utf-16-le")) + rb"(?![0-9A-Za-z_]\x00)", blob) is not None


def check_template_parts(files, rules, dock_blob, strip_comments):
    """files: {상대 경로: 내용}. dock_blob 이 None 이면 대조할 수 없으므로 위반이다."""
    cfg = rules["template_parts"]
    contract = cfg["contract"]
    if contract not in files:
        return [f"{contract} 템플릿 부품 계약 파일이 없다"]
    code = strip_comments(contract, files[contract])
    consts = [(m.group(1), m.group(2)) for m in CONST_STRING.finditer(code) if m.group(2) != PREFIX]
    found = [f"{contract} 계약 상수가 하나도 없다"] if not consts else []
    found += [f"{contract} {k} 값이 {PREFIX} 로 시작하지 않는다: {v}" for k, v in consts if not v.startswith(PREFIX)]
    if dock_blob is None:
        found.append(f"{contract} Dock 패키지를 찾지 못해 부품 이름을 대조할 수 없다")
    prefix = f"{rules['src_root'].rstrip('/')}/{cfg['definer']}/"
    views = "\n".join(strip_comments(r, t) for r, t in files.items() if r.startswith(prefix) and r.endswith(".axaml"))
    defined = set(re.findall(r'(?<![\w.])(?:x:)?Name="(PART_\w+)"', views))
    for key, name in consts:
        if name.startswith(PREFIX) and name not in defined and dock_blob is not None and not in_dock(name, dock_blob):
            found.append(f"{contract} {key}={name} 가 {cfg['definer']} 템플릿과 Dock 패키지 어디에도 없다")
    return found


def load_dock_blob(package_versions, rules):
    """NuGet 캐시에서 Shell 이 참조한 판본의 Dock DLL 을 읽어 잇는다. 하나라도 없으면 None(검사 불가)."""
    cfg = rules["template_parts"]
    homes = [os.environ.get("NUGET_PACKAGES", ""), os.path.expanduser("~/.nuget/packages")] + glob.glob("/mnt/c/Users/*/.nuget/packages")
    blobs = []
    for pkg in cfg["dock_packages"]:
        rel = f"{pkg.lower()}/{package_versions.get(pkg)}/lib/{cfg['dock_tfm']}/{pkg}.dll"
        path = next((os.path.join(h, rel) for h in homes if h and os.path.isfile(os.path.join(h, rel))), None)
        if path is None:
            return None
        with open(path, "rb") as f:
            blobs.append(f.read())
    return b"".join(blobs)
