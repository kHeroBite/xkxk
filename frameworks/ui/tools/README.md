# tools — 별별UI 개발·검증 도구 색인

> tools_root 색인이다. 이 폴더의 도구는 2026-11-24 Pd 저장소 `poc/ui/tools/`(커밋 `157403c`)에서 그대로 옮겨 왔다. 원본 Pd 에는 이 색인이 없었다.
> 실행 기준 폴더는 영역 루트 `frameworks/ui` 이다. 예: `cd frameworks/ui && python3 -B tools/check_layers.py --self-test`.
> 경로 표기 `poc/ui` 는 옛 저장소의 위치다. 새 홈에서는 `frameworks/ui` 로 읽는다. 동작 경로를 아직 고치지 않은 도구는 "경로 고정 TODO UTODO-0001" 로 표기했고, 대상 목록은 `plan/90_결정관리/이전기록_2026-11-24_경로고정목록.md` 에 있다.

## 빌드·실행

| 경로 | 용도 | 재사용 이유 | 실행 명령 | 상태 |
|---|---|---|---|---|
| `tools/build_desktop.sh` | Pd.Poc.Desktop 을 Windows dotnet.exe 로 비증분 빌드(WSL 에서 실행) | 데스크톱 빌드의 단일 경로 | `bash tools/build_desktop.sh --bg [Debug\|Release\|R2R\|Work\|Product]` → `logs/build_desktop_<시각>.log` 와 `.rc` | 새 홈에서 동작(스크립트 위치 기준 상대 해석). 주석에 `poc/ui` 문구 1곳 잔존 |
| `tools/build_browser.sh` | Pd.Poc.Browser(WASM) 정적 번들 publish | 웹 번들 빌드 | `bash tools/build_browser.sh --bg [Release\|Debug]` | 경로 문구 없음 |
| `tools/build_android.sh` | Pd.Poc.Android 비증분 빌드 | Android 빌드 | `bash tools/build_android.sh --bg [Debug\|Release] [PdDiagnostics=true\|false]` | 경로 문구 없음 |
| `tools/build_lock.sh` | 같은 Configuration 빌드 직렬화 잠금(source 해서 씀) | 빌드 스크립트 3종이 공유 | `source tools/build_lock.sh; pd_build_lock <ROOT> <Configuration> <LOG>` | 경로 문구 없음 |
| `tools/build_avalonia_browser_pd.sh` | Avalonia.Browser 유휴 rAF 패치판을 로컬 피드로 pack(기본 dry-run) | 패치판 재생성 | `bash tools/build_avalonia_browser_pd.sh` | 경로 문구 없음 |
| `tools/build_font_subset.py` | 웹·Android 임베드 한글 글꼴 부분집합 재생성 | 글꼴 글자 추가 때 | `python3 -B tools/build_font_subset.py` | 경로 문구 없음 |
| `tools/run_desktop_bg.sh` | Desktop 을 비전경·stdio 분리로 기동하고 실존 2회 확인 | 시험 기동 표준 | `bash tools/run_desktop_bg.sh [--conf Debug] -- <앱 인자>` | 경로 문구 없음 |
| `tools/slot_handoff.sh` | 데스크톱 실행 슬롯 소유자 이관 | 실행 슬롯 교착 방지 | `bash tools/slot_handoff.sh <새 소유자\|없음> [메모]` | 경로 문구 없음 |
| `tools/serve_browser.sh`·`tools/serve_browser.py` | WASM 번들을 127.0.0.1 에 서빙 | 웹 시험 서버 | `bash tools/serve_browser.sh start [포트] [번들경로]` | 경로 문구 없음 |
| `tools/pdctl.py` | 실행 중 PoC 앱 원격 제어 서버에 명령 전달 | 시험 자동화 클라이언트 | `python3 -B tools/pdctl.py <명령>` | 주석에 `poc/ui/logs` 문구 1곳 잔존 |

## 계층·정적 검사

| 경로 | 용도 | 재사용 이유 | 실행 명령 | 상태 |
|---|---|---|---|---|
| `tools/check_layers.py` | 클라이언트 계층 정적 검사(참조 방향·줄 수·헤더) | 레이어 분리 원칙 게이트 | `python3 -B tools/check_layers.py [--self-test]` | 2026-11-24 기본 `--root` 를 영역 루트로 고침. 사용 주석의 `poc/ui` 문구만 잔존 |
| `tools/check_layers_parts.py` | 템플릿 부품 이름 계약 검사(check_layers.py 가 호출) | 계층 검사 보조 | check_layers.py 가 부름 | 경로 문구 없음 |
| `tools/check_layers_rules.json` | 계층 규칙(층별 금지 참조·패키지·패턴) | check_layers.py 입력 | — | 2026-11-24 경로 6곳을 영역 상대(`src`·`tools`)로 고침 |
| `tools/classify_layers.py` | 소스 계층 분류기, `LAYERS.md` 재생성 | 계층 분류표 정본 생성 | `python3 -B tools/classify_layers.py [--check]` | 2026-11-24 `SRC`·기본 `--root`·출력 경로를 영역 기준으로 고침. 생성 문구·사용 주석의 `poc/ui` 는 그대로(LAYERS.md 대조 유지) |
| `tools/classify_layers_overrides.json` | 분류기 고정 예외(사유 필수) | classify_layers.py 입력 | — | `_note` 문구에 `poc/ui/src/` 잔존(경로 기준 설명) |
| `tools/partial_gate.py` | partial 선언 여러 파일 분산 위반 게이트 | partial 쪼개기 금지 원칙 | `python3 -B tools/partial_gate.py [src 경로]` | **경로 고정 TODO UTODO-0001** — `DEFAULT_SRC` 가 Pd 절대경로. 새 홈에서는 src 경로를 인자로 준다 |
| `tools/check_i18n.py`·`tools/check_i18n_rules.py` | 다국어 정적 검사(키 집합·하드코딩 한글·사전 키) | 다국어 회귀 방지 | `python3 -B tools/check_i18n.py [--report-only]` | 사용 주석에 `poc/ui` 문구 잔존 |
| `tools/check_tour_catalog.py` 외 `_format`·`_rules`·`_selftest` | 도움말 투어 카탈로그 정적 검사 | 투어 커버리지 회귀 방지 | `python3 -I tools/check_tour_catalog.py [--self-test]` | 사용 주석에 `poc/ui` 문구 잔존 |
| `tools/check_assembly_closure.py` | 제품 산출물 어셈블리 TypeRef/MemberRef 폐포 검사 | 제품 빌드 누락 탐지 | `python3 -B tools/check_assembly_closure.py <폴더\|apk>` | 경로 문구 없음 |
| `tools/check_product_web.py`·`tools/q9_product_allowlist.json` | 제품 산출물의 진단 기능 부재(Q9) 판정 | 제품 판정 게이트 | `python3 -B tools/check_product_web.py <wwwroot\|폴더\|apk>` | 경로 문구 없음 |
| `tools/check_icons.py` | 산출물에 앱 아이콘이 들어갔는지 파일 검사 | 아이콘 회귀 방지 | `python3 -B tools/check_icons.py --exe … --apk … --web …` | 사용 주석에 `poc/ui` 문구 잔존 |

## 아이콘·그림

| 경로 | 용도 | 재사용 이유 | 실행 명령 | 상태 |
|---|---|---|---|---|
| `tools/make_icons.py` | 원본 그림 한 장으로 전 플랫폼 앱 아이콘 재생성 | 아이콘 교체 때 | `python3 -B tools/make_icons.py [--source 그림.png]` | 사용 주석에 `poc/ui` 문구 잔존 |
| `tools/make_logo.py` | 로고 원본으로 전 플랫폼 아이콘 트리 생성 | 로고 교체 때 | `python3 -B tools/make_logo.py --out <폴더> --preview <폴더>` | **경로 고정 TODO UTODO-0001** — 출력 트리를 `<out>/poc/ui/...` 로 만든다 |
| `tools/capture_win_ref.ps1` | Windows 표준 창을 화면 밖 PrintWindow 로 캡처 | 창 크롬 기준 비교 | `powershell.exe -File tools/capture_win_ref.ps1 -OutDir <폴더>` | 경로 문구 없음 |
| `plan/_build/menu_icons.py`(tools 밖) | 메뉴 ID 별 SVG·axaml·C# 카탈로그 생성 | 메뉴 아이콘 정본 생성 | `python3 -B plan/_build/menu_icons.py --check` | 2026-11-24 출력 경로 2줄을 영역 기준으로 고침. 데이터 `menu_icon_data.py`·`icon_glyphs.py`·`menu_data.py` 동반. `menu_tree.py` 는 옮기지 않았다(02_메뉴구조 재생성은 Pd 에서) |
