# retest_android_q16_17.py(7555a17b)를 결과 폴더만 logs/otest_m2/i1835rc 로 바꿔 i1835rc 판에서 실행하는 감싸기 스크립트(otest-m-3)
"""
사용: python3 -E -u tools/retest_android_q16_17_i1835rc.py --apk <i1835rc diag APK>
원 스크립트의 판정·원복을 그대로 쓴다. 결과 json 의 build 값은 원 스크립트 그대로 i1720rc 로 찍히므로 파일 위치(i1835rc 폴더)와 apk 인자로 판을 구분한다.
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import retest_android_q16_17 as q  # noqa: E402

q.O = os.path.join(q.ROOT, "logs", "otest_m2", "i1835rc")
os.makedirs(q.O, exist_ok=True)

if __name__ == "__main__":
    q.main()
