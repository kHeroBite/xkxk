# 호스트 정체성(실행 파일·프로젝트 경로·패키지·데이터 폴더 이름) 단일 출처 host_identity.json 의 접근자와 CLI
"""
사용: python3 -B tools/host_identity.py get <키>   (값 1줄 출력, 배열은 공백 없이 '/' 로 이은 문자열이 아니라 줄마다 1개)
      python3 -B tools/host_identity.py list       (전체 키=값)
파이썬: import host_identity; host_identity.get("desktop.exe_name"); host_identity.path("desktop.project_dir")
경로 값은 영역 루트(UI) 기준 상대이다. 없는 키는 KeyError(CLI 는 rc=2).
"""
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
UI = os.path.dirname(HERE)
JSON_PATH = os.path.join(HERE, "host_identity.json")

_cache = None


def load():
    """host_identity.json 을 읽어 dict 로 돌려준다(1회 캐시)."""
    global _cache
    if _cache is None:
        with open(JSON_PATH, encoding="utf-8") as f:
            _cache = json.load(f)
    return _cache


def get(key):
    """키의 값을 돌려준다. 없으면 KeyError."""
    return load()[key]


def path(key, *more):
    """경로 값을 영역 루트 기준 절대경로로 돌려준다(more 는 뒤에 이음)."""
    return os.path.join(UI, *get(key).split("/"), *more)


def bin_dir(host, conf, tfm):
    """호스트(desktop|browser|android)의 bin/<conf>/<tfm> 절대경로."""
    return path(host + ".project_dir", "bin", conf, tfm)


def apk_path(conf):
    """서명된 APK 절대경로(android bin/<conf>/net11.0-android/<apk_name>)."""
    return os.path.join(bin_dir("android", conf, "net11.0-android"), get("android.apk_name"))


def data_dir_under(local_app_data):
    """LOCALAPPDATA 아래 사용자 데이터 폴더 경로(segments 이음)."""
    return os.path.join(local_app_data, *get("data.segments"))


def _main(argv):
    if len(argv) >= 2 and argv[0] == "get":
        try:
            v = get(argv[1])
        except KeyError:
            sys.stderr.write("없는 키: %s\n" % argv[1])
            return 2
        if isinstance(v, list):
            for x in v:
                print(x)
        else:
            print(v)
        return 0
    if argv and argv[0] == "list":
        for k, v in load().items():
            print("%s=%s" % (k, "/".join(v) if isinstance(v, list) else v))
        return 0
    sys.stderr.write("사용: host_identity.py get <키> | list\n")
    return 64


if __name__ == "__main__":
    sys.exit(_main(sys.argv[1:]))
