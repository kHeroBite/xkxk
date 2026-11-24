# WASM 정적 번들을 127.0.0.1 에 서빙하는 웹 서버 — 미리 압축된 .br 을 br 인코딩 헤더로 보내고 지문 붙은 _framework 파일은 장기 불변 캐시, 나머지는 재검증
# 사용: python3 -I serve_browser.py <포트> <번들 wwwroot 경로>   (serve_browser.sh 가 띄운다)
import email.utils
import http.server
import os
import re
import sys
from functools import partial

# .NET 번들 지문: 이름.<소문자·숫자 8자 이상, 숫자 1개 이상>.확장자 (예: dotnet.native.8bv0zyjm1g.wasm). 지문 없는 dotnet.js 등은 재검증 대상이다
FINGERPRINT = re.compile(r"\.(?=[a-z0-9]*[0-9])[a-z0-9]{8,}\.[A-Za-z0-9]+$")
IMMUTABLE = "public, max-age=31536000, immutable"
REVALIDATE = "no-cache"
# 조건부 요청(If-None-Match·If-Modified-Since)에 304 를 돌려줄 범위. 기본 all: 지문 파일도 304.
# .NET 런타임 기본 설정(fetch cache:'no-cache')은 불변 파일도 매번 재검증하는데, 여기서 200 을 주면 본문을 다시 받고
# V8 wasm 코드 캐시도 지워진다(https://v8.dev/blog/wasm-code-caching — 200 은 캐시 무효, 304 는 유지).
# 비교 시험용: 환경변수 PD_SERVE_304=revalidate-only 면 예전 동작(재검증 대상 파일만 304, 지문 파일은 늘 200).
CONDITIONAL_ALL = os.environ.get("PD_SERVE_304", "all") != "revalidate-only"


def accepts_br(header):
    # Accept-Encoding 에서 br 이 q>0 으로 들어 있는지 본다(br;q=0 은 거부로 본다)
    for part in header.split(","):
        name, _, params = part.strip().partition(";")
        if name.strip().lower() != "br":
            continue
        q = re.search(r"q\s*=\s*([0-9.]+)", params)
        try:
            return q is None or float(q.group(1)) > 0
        except ValueError:
            return False
    return False


class BundleHandler(http.server.SimpleHTTPRequestHandler):
    protocol_version = "HTTP/1.1"
    extensions_map = dict(http.server.SimpleHTTPRequestHandler.extensions_map, **{
        ".wasm": "application/wasm", ".js": "text/javascript", ".mjs": "text/javascript", ".json": "application/json",
        ".dat": "application/octet-stream", ".webmanifest": "application/manifest+json"})

    def log_message(self, *args):
        pass

    def cache_control(self):
        path = self.path.split("?", 1)[0].split("#", 1)[0]
        if "/_framework/" in path and FINGERPRINT.search(path):
            return IMMUTABLE
        return REVALIDATE

    def send_head(self):
        path = self.translate_path(self.path)
        if os.path.isdir(path):
            path = os.path.join(path, "index.html")
        if not os.path.isfile(path):
            self.send_error(404)
            return None
        has_br = os.path.isfile(path + ".br")
        src = path + ".br" if has_br and accepts_br(self.headers.get("Accept-Encoding", "")) else path
        cache = self.cache_control()
        mtime = int(os.path.getmtime(path))
        # ETag: 보낼 본문(원본 또는 .br) 기준 — 인코딩별로 달라야 한다(Vary: Accept-Encoding 과 짝)
        st = os.stat(src)
        etag = '"%x-%x%s"' % (int(st.st_mtime), st.st_size, "-br" if src != path else "")
        if (cache == REVALIDATE or CONDITIONAL_ALL) and self.not_modified(etag, mtime):
            # 본문 재전송을 피한다(지문 파일이면 V8 wasm 코드 캐시도 유지)
            self.send_response(304)
            self.send_header("Cache-Control", cache)
            self.send_header("ETag", etag)
            if has_br:
                self.send_header("Vary", "Accept-Encoding")
            self.send_header("Content-Length", "0")
            self.end_headers()
            return None
        f = open(src, "rb")
        self.send_response(200)
        self.send_header("Content-Type", self.guess_type(path))
        if src != path:
            self.send_header("Content-Encoding", "br")
        if has_br:
            self.send_header("Vary", "Accept-Encoding")
        self.send_header("Content-Length", str(os.fstat(f.fileno()).st_size))
        self.send_header("Last-Modified", self.date_time_string(mtime))
        self.send_header("ETag", etag)
        self.send_header("Cache-Control", cache)
        self.end_headers()
        return f

    def not_modified(self, etag, mtime):
        # RFC 9110 13.1.2·13.2.2: If-None-Match 가 있으면 그것만 보고(약한 비교), 없을 때만 If-Modified-Since 를 본다
        inm = self.headers.get("If-None-Match")
        if inm is not None:
            tags = [t.strip() for t in inm.split(",")]
            return "*" in tags or any(t.removeprefix("W/") == etag for t in tags)
        ims = self.headers.get("If-Modified-Since")
        if not ims:
            return False
        try:
            return mtime <= int(email.utils.parsedate_to_datetime(ims).timestamp())
        except (TypeError, ValueError, OverflowError):
            return False


def main():
    port, root = int(sys.argv[1]), sys.argv[2]
    http.server.ThreadingHTTPServer(("127.0.0.1", port), partial(BundleHandler, directory=root)).serve_forever()


if __name__ == "__main__":
    main()
