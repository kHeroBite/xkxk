# tools — 별별UI 개발·검증 도구 색인

> tools_root 색인이다. 이 폴더의 도구는 2026-11-24 Pd 저장소 `poc/ui/tools/`(커밋 `157403c`)에서 그대로 옮겨 왔다. 원본 Pd 에는 이 색인이 없었다.
> 실행 기준 폴더는 영역 루트 `frameworks/ui` 이다. 예: `cd frameworks/ui && python3 -B tools/check_layers.py --self-test`.
> 경로 표기 `poc/ui` 는 옛 저장소의 위치다. 새 홈에서는 `frameworks/ui` 로 읽는다. 2026-11-24 UTODO-0001 처리(커밋 11e47c3)로 도구의 동작 경로는 새 홈 기준으로 고쳤다. 처리 결과와 남은 흔적은 `plan/90_결정관리/이전기록_2026-11-24_경로고정목록.md` §5 에 있다.

## 빌드·실행

| 경로 | 용도 | 재사용 이유 | 실행 명령 | 상태 |
|---|---|---|---|---|
| `tools/build_desktop.sh` | 데스크톱 시험대 실행 프로젝트(`host_identity.json` 의 `desktop.project_dir`, 호스팅 라이브러리 `src/xkxk.frameworks.ui.hosting.desktop` 를 함께 빌드)를 Windows dotnet.exe 로 비증분 빌드(WSL 에서 실행) | 데스크톱 빌드의 단일 경로 | `bash tools/build_desktop.sh --bg [Debug\|Release\|R2R\|Work\|Product]` → `logs/build_desktop_<시각>.log` 와 `.rc` | 새 홈에서 동작(스크립트 위치 기준 상대 해석). 주석에 `poc/ui` 문구 1곳 잔존 |
| `tools/build_browser.sh` | 브라우저 시험대 실행 프로젝트(`browser.project_dir`, WASM, 호스팅 라이브러리 `hosting.browser` 포함) 정적 번들 publish | 웹 번들 빌드 | `bash tools/build_browser.sh --bg [Release\|Debug]` | 경로 문구 없음 |
| `tools/build_android.sh` | Android 시험대 실행 프로젝트(`android.project_dir`, 호스팅 라이브러리 `hosting.android` 포함) 비증분 빌드 | Android 빌드 | `bash tools/build_android.sh --bg [Debug\|Release] [PdDiagnostics=true\|false]` | 경로 문구 없음 |
| `tools/build_lock.sh` | 같은 Configuration 빌드 직렬화 잠금(source 해서 씀) | 빌드 스크립트 3종이 공유 | `source tools/build_lock.sh; pd_build_lock <ROOT> <Configuration> <LOG>` | 경로 문구 없음 |
| `tools/build_avalonia_browser_pd.sh` | Avalonia.Browser 유휴 rAF 패치판을 로컬 피드로 pack(기본 dry-run) | 패치판 재생성 | `bash tools/build_avalonia_browser_pd.sh` | 경로 문구 없음 |
| `tools/build_font_subset.py` | 웹·Android 임베드 한글 글꼴 부분집합 재생성 | 글꼴 글자 추가 때 | `python3 -B tools/build_font_subset.py` | 경로 문구 없음 |
| `tools/check_build_rc.sh` | 최신 빌드 로그의 `<로그>.log.rc` 와 `오류 0개`·`0 Error(s)` 표기로 축별 빌드 성공 판정(UTODO-0016) | 검수 조건이 빌드 rc 를 읽을 때 `.log.rc` 이름을 한곳에서 맞춘다 | `bash tools/check_build_rc.sh [desktop\|browser\|android\|all] [--logs <폴더>]` → 0=모두 PASS·1=FAIL·2=인자 오류 | 2026-11-24 신설 |
| `tools/safe_click.ps1` | 물리 클릭 전 좌표계·창 소유권(ExpectedProcessId)·DPI 3축을 검증한 뒤에만 클릭·끌기하는 가드 | 실입력 시험(`test/desktop/measure/retest_real_input.py` 의 `SAFE_CLICK`)이 공유하는 클릭 경로 | `retest_real_input.Rig.click` 이 호출(직접 실행 시 `powershell.exe -File tools/safe_click.ps1 -ScreenX <x> -ScreenY <y> -ExpectedProcessId <pid>`) | 2026-11-24 Pd `.claude/scripts/safe_click.ps1`(기준 커밋 867b432)에서 본문 무변경 복사. 그전에는 xkxk 에 없어 실입력 시험이 깨졌다. 2026-11-25(D-7) 클릭 주입을 injections.jsonl 에 기록(PC 비움 감시기 제외 대상) — safe_wheel.ps1 과 같은 형식(t_ms·kind·x·y·source)으로 `logs/pcaway/injections.jsonl` 에 `<kind>_pre`→`<kind>`/`<kind>_failed`(kind=click·dblclick·drag·wheel·cursor_move)를 남기고, `-InjectionLog` 로 경로를 바꿀 수 있으며 기록에 실패하면 입력하지 않는다 |
| `tools/run_desktop_bg.sh` | Desktop 을 비전경·stdio 분리로 기동하고 실존 2회 확인 | 시험 기동 표준 | `bash tools/run_desktop_bg.sh [--conf Debug] -- <앱 인자>` | 경로 문구 없음 |
| `tools/slot_handoff.sh` | 데스크톱 실행 슬롯 소유자 이관 | 실행 슬롯 교착 방지 | `bash tools/slot_handoff.sh <새 소유자\|없음> [메모]` | 경로 문구 없음 |
| `tools/serve_browser.sh`·`tools/serve_browser.py` | WASM 번들을 127.0.0.1 에 서빙 | 웹 시험 서버 | `bash tools/serve_browser.sh start [포트=5791] [번들경로]` | 경로 문구 없음 |
| `tools/pdctl.py` | 실행 중 PoC 앱 원격 제어 서버에 명령 전달 | 시험 자동화 클라이언트 | `python3 -B tools/pdctl.py <명령>` | 2026-11-24 경로 고정 해소(동작 줄에 옛 경로 없음) |
| `tools/host_identity.json`·`tools/host_identity.py` | 실행 호스트 정체성·경로 값의 단일 출처(실행 파일·프로세스 이름, 프로젝트 폴더, APK·패키지, 데이터 폴더 세그먼트, 인스턴스 접두 — 영역 루트 기준 상대 경로)와 그 접근자 | 호스트 이름·위치(호스팅 라이브러리·시험대 실행 프로젝트 포함)가 바뀌어도 스크립트·시험은 이 값만 읽게 해 하드코딩을 막는다(`test/framework/judge/host_paths_check.py` 가 감시) | 파이썬 `import host_identity`(`load()`·`get(키)`·`path(키, …)`), 셸 `python3 -B tools/host_identity.py get <키>`(없는 키 rc=2) | 2026-11-28 신설(B11-1). `test/common/_paths.py`·`tools/pdctl.py` 가 재노출 |

## 계층·정적 검사

| 경로 | 용도 | 재사용 이유 | 실행 명령 | 상태 |
|---|---|---|---|---|
| `tools/check_layers.py` | 클라이언트 계층 정적 검사(참조 방향·줄 수·헤더) | 레이어 분리 원칙 게이트 | `python3 -B tools/check_layers.py [--self-test]` | 2026-11-24 기본 `--root` 를 영역 루트로 고침. 옛 경로 문구 해소 |
| `tools/check_layers_parts.py` | 템플릿 부품 이름 계약 검사(check_layers.py 가 호출) | 계층 검사 보조 | check_layers.py 가 부름 | 경로 문구 없음 |
| `tools/check_layers_consumer.py` | 프레임워크 소비 경계 검사(InternalsVisibleTo 금지·test/framework 의 Internal·NonPublic 접근 금지, testbed 는 비활성 골격) | 별별UI 프레임워크화 공개 경계 게이트 | check_layers.py 가 부름. 단독 `python3 -B tools/check_layers_consumer.py [--self-test]` | 2026-11-26 신설(B02). 규칙은 check_layers_rules.json 의 consumer_rules |
| `tools/check_layers_rules.json` | 계층 규칙(층별 금지 참조·패키지·패턴) | check_layers.py 입력 | — | 2026-11-24 경로 6곳을 영역 상대(`src`·`tools`)로 고침 |
| `tools/classify_layers.py` | 소스 계층 분류기, `LAYERS.md` 재생성 | 계층 분류표 정본 생성 | `python3 -B tools/classify_layers.py [--check]` | 2026-11-24 `SRC`·기본 `--root`·출력 경로를 영역 기준으로 고침. 생성 문구·사용 주석도 새 홈 기준으로 고쳐 LAYERS.md 를 재생성했다(`--check` rc=0) |
| `tools/classify_layers_overrides.json` | 분류기 고정 예외(사유 필수) | classify_layers.py 입력 | — | 2026-11-24 경로 설명 해소 |
| `tools/partial_gate.py` | partial 선언 여러 파일 분산 위반 게이트 | partial 쪼개기 금지 원칙 | `python3 -B tools/partial_gate.py [src 경로]` | 2026-11-24 `DEFAULT_SRC` 를 스크립트 위치 기준 `../src` 로 고침(경로 인자 없이 실행 가능) |
| `tools/check_i18n.py`·`tools/check_i18n_rules.py` | 다국어 정적 검사(키 집합·하드코딩 한글·사전 키) | 다국어 회귀 방지 | `python3 -B tools/check_i18n.py [--report-only]` | 사용 주석에 `poc/ui` 문구 잔존 |
| `tools/check_tour_catalog.py` 외 `_format`·`_rules`·`_selftest` | 도움말 투어 카탈로그 정적 검사 | 투어 커버리지 회귀 방지 | `python3 -I tools/check_tour_catalog.py [--self-test]`(B11-4 부터 셸 메뉴 AutomationId·모바일 목록 desktopIds 를 `ShellMenuTable*.cs` 뿐 아니라 시험대 `testbed/xkxk.frameworks.ui.testbed/TestbedMenus*.cs` 리터럴에서도 읽는다. 데모·벤치 8행이 표에서 시험대 등록으로 옮겨져 자동 설명 대상이 같다. 자기시험 사례는 8행 이전과 무관해 그대로 26건) | 사용 주석에 `poc/ui` 문구 잔존 |
| `tools/check_assembly_closure.py` | 제품 산출물 어셈블리 TypeRef/MemberRef 폐포 검사 | 제품 빌드 누락 탐지 | `python3 -B tools/check_assembly_closure.py <폴더\|apk>` | 경로 문구 없음 |
| `tools/check_product_web.py`·`tools/q9_product_allowlist.json` | 제품 산출물의 진단 기능 부재(Q9) 판정 | 제품 판정 게이트 | `python3 -B tools/check_product_web.py <wwwroot\|폴더\|apk>` | 경로 문구 없음 |
| `tools/check_icons.py` | 산출물에 앱 아이콘이 들어갔는지 파일 검사 | 아이콘 회귀 방지 | `python3 -B tools/check_icons.py --exe … --apk … --web …` | 2026-11-24 경로 문구 해소 |

## 문서 생성

| 경로 | 용도 | 재사용 이유 | 실행 명령 | 상태 |
|---|---|---|---|---|
| `tools/gen_api_docs.py`(보조 `gen_api_docs_parse.py` PublicAPI·`///` 주석 파서, `gen_api_docs_render.py` Markdown 렌더) | `src/*/PublicAPI.Shipped.txt`·`Unshipped.txt` 와 셸 프로젝트 `*.cs`(`bin`·`obj` 제외)의 `///` 주석, 셸 메뉴 표 `src/xkxk.frameworks.ui.data/Navigation/ShellMenuTable.cs`(08 셸 메뉴 ID 의 부모·종류·순서·명령 ID 열)로 소비자용 공개 API 명세 `docs/api/*.md` 생성 | 공개 API 를 바꿀 때마다 명세를 다시 만들고 낡음을 판정한다(손 편집 금지) | `python3 -B tools/gen_api_docs.py [--root <영역 루트>]`(생성) · `--check`(디스크와 비교, 0=같음·1=불일치·2=입력 오류) · `--prune`(생성 대상이 아닌 `docs/api/*.md` 삭제) · `--self-test` | 2026-11-26 신설(B07b) |
| `tools/gen_api_docs_extra.py` | 공개 API 명세 생성기(`gen_api_docs.py`)의 보조 모듈. PublicAPI txt 밖이지만 소비자에게 공개하기로 한 화면 층 형식(명시 허용 목록 `EXTRA_TYPES`, 예 views 의 `UiWindowViewRegistration` 의 `Register` 오버로드)을 소스 `.cs` 에서 읽어 공개 표면에 더한다 | 소비자 공개 예외 형식을 명세에 싣기 위함. 허용 목록 밖 형식은 끌어오지 않는다 | `gen_api_docs.py` 가 import 해 쓴다(단독 실행 없음) | 2026-11-27 신설(B10) |

## 아이콘·그림

| 경로 | 용도 | 재사용 이유 | 실행 명령 | 상태 |
|---|---|---|---|---|
| `tools/make_icons.py` | 원본 그림 한 장으로 전 플랫폼 앱 아이콘 재생성 | 아이콘 교체 때 | `python3 -B tools/make_icons.py [--source 그림.png]` | 2026-11-24 경로 문구 해소 |
| `tools/make_logo.py` | 로고 원본으로 전 플랫폼 아이콘 트리 생성 | 로고 교체 때 | `python3 -B tools/make_logo.py --out <폴더> --preview <폴더>` | 2026-11-24 UTODO-0012 처리로 출력 트리를 영역 루트 상대 경로 그대로(`<out>/src/...`·`<out>/assets/...`) 만든다. 옛 `<out>/poc/ui/...` 구조는 없앴다 |
| `tools/capture_win_ref.ps1` | Windows 표준 창을 화면 밖 PrintWindow 로 캡처 | 창 크롬 기준 비교 | `powershell.exe -File tools/capture_win_ref.ps1 -OutDir <폴더>` | 경로 문구 없음 |
| `plan/_build/menu_icons.py`(tools 밖) | 메뉴 ID 별 SVG·axaml·C# 카탈로그 생성 | 메뉴 아이콘 정본 생성 | `python3 -B plan/_build/menu_icons.py --check` | 2026-11-24 출력 경로 2줄을 영역 기준으로 고침. 데이터 `menu_icon_data.py`·`icon_glyphs.py`·`menu_data.py` 동반. `menu_tree.py` 는 옮기지 않았다(02_메뉴구조 재생성은 Pd 에서) |
