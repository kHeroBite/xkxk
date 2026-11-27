# PD UI PoC — Avalonia + Dock (MDI/SDI · 데스크톱/브라우저)

> 폐기 가능한 실험 코드다. 정식 소스(`src/`)와 분리돼 있다. 실행 환경 정본은 프로젝트 루트 `INFRA.md` 3~6장이다.

> **별별UI 를 쓰는 쪽(다른 프로젝트의 개발자·AI)** 은 소비자 문서 [docs/README.md](docs/README.md) 부터 읽는다. AI 에이전트는 [docs/03_AI_활용안내.md](docs/03_AI_활용안내.md) 의 복사용 블록을 자기 지침에 붙여 넣고, 공개 API 는 [docs/api/README.md](docs/api/README.md) 로 확인한다.

## 구성
| 프로젝트 | 대상 | 역할 |
|---|---|---|
| `src/xkxk.frameworks.ui.shell` | net11.0 | 컨트롤 층(명령·상태·뷰모델, Dock 조작, 계측·자동화). axaml 없음. 화면은 `IViewRegistry` 로만 연다 |
| `src/xkxk.frameworks.ui.views` | net11.0 | 화면 조립 층(axaml·코드비하인드·테마·샘플 문서 3종·벤치·갤러리, 앱 진입점 `PdApp`). 호스트가 참조한다 |
| `src/xkxk.frameworks.ui.data` | net11.0 | 데이터 층(로깅·저장소·백업 DTO·카탈로그). Avalonia 무참조 |
| `src/xkxk.frameworks.ui.desktop` | net11.0 | Windows·macOS 호스트(파일 레이아웃 저장소, SDI OS 창) |
| `src/xkxk.frameworks.ui.browser` | net11.0-browser | WASM 호스트(localStorage 레이아웃 저장소, URL 쿼리 → 자동화 인자) |
| `src/xkxk.frameworks.ui.android` | net11.0-android | Android 호스트(Debug APK) |
| `src/xkxk.frameworks.ui` | net11.0 | 순수 UI 층(재사용 컨트롤·스타일·애니메이션·`MotionPreference`·문자열 훅 `UiText`). Avalonia 만 참조한다 |

패키지 버전은 Avalonia 12.1.4, Dock.Avalonia 12.1.0.12 이다(nuget.org 실측 2026-10-28).

### 계층 규칙(요건 8)
화면 조립층 `xkxk.frameworks.ui.views`·데이터층 `xkxk.frameworks.ui.data` 는 이동 배치(B7b)에서 생겼고 현재 소스에 있다. 표는 그 구조의 규칙이다.

| 층 | 참조할 수 있는 것 | 참조하면 안 되는 것 |
|---|---|---|
| 호스트(Desktop·Browser·Android) | 아래 전부 | — |
| `xkxk.frameworks.ui.views`(화면 조립: axaml·코드비하인드) | `xkxk.frameworks.ui.shell`, `xkxk.frameworks.ui`, `xkxk.frameworks.ui.data` | 호스트 |
| `xkxk.frameworks.ui.shell`(컨트롤: 명령·상태·뷰모델) | `xkxk.frameworks.ui`, `xkxk.frameworks.ui.data` | `xkxk.frameworks.ui.views`, 호스트 |
| `xkxk.frameworks.ui`(순수 UI) | Avalonia 패키지만 | `xkxk.frameworks.ui.shell`, `xkxk.frameworks.ui.data`, `xkxk.frameworks.ui.views`, 호스트, Dock |
| `xkxk.frameworks.ui.data`(DTO·저장소·직렬화) | 비 UI 패키지 | Avalonia, `xkxk.frameworks.ui.shell`, `xkxk.frameworks.ui`, `xkxk.frameworks.ui.views`, 호스트 |

- `xkxk.frameworks.ui` 는 상위 층을 모른다. 문자열은 `UiText.Provider`(셸이 `Loc.Tr` 로 연결), 설정은 속성·이벤트로 받는다.
- 검사: `python3 poc/ui/tools/check_layers.py`(rc=0/1, 마지막 줄 `elapsed_ms=<n>`, 상한 3초). 규칙은 `tools/check_layers_rules.json`, 검사기 자체 확인은 `--self-test`. 층 파일(`xkxk.frameworks.ui`·`xkxk.frameworks.ui.views`·`xkxk.frameworks.ui.data`)은 300줄 이하에 첫 줄 한국어 헤더 주석을 둔다. 템플릿 부품 이름(`PART_*`)은 Shell 코드에 쓰지 못하고 `xkxk.frameworks.ui` 의 `TemplateParts` 상수로만 쓴다. `check_layers_parts.py` 가 계약 상수의 실재를 `xkxk.frameworks.ui.views` axaml 의 `Name` 정의나 Shell 이 참조한 판본의 Dock DLL 에서 대조하고, DLL 을 못 찾으면 위반으로 친다.

## 빌드 (WSL 에서 Windows dotnet.exe 호출)

- 대상은 .NET 11(`net11.0`·`net11.0-browser`·`net11.0-android`)이다(2026-11-12 전환, SDK 11.0.100·런타임 Microsoft.NETCore.App 11.0.0). 워크로드는 `dotnet workload install android wasm-tools` 로 설치한다(android 37.2.31, wasm-tools 11.0.100 — obuild-2 보고). `tools/` 의 `build_android.sh`·`build_browser.sh`·`build_desktop.sh`·`run_desktop_bg.sh`·`serve_browser.sh` 출력 경로는 `net11.0` 이고, `tools/check_layers_rules.json` 의 `dock_tfm` 은 NuGet 패키지 lib 폴더명이라 `net10.0` 그대로다. `poc/ui/global.json` 은 SDK 11.0.100 이상 중 가장 높은 것을 쓰도록 `rollForward: latestMajor` 로 둔다. 실행 쪽은 `poc/ui/Directory.Build.props` 가 `xkxk.frameworks.ui.desktop` 에만 `RollForward=LatestMajor` 를 주어, 프레임워크 종속 실행본이 설치된 가장 높은 주 버전 런타임으로 뜬다(runtimeconfig.json 의 rollForward).
```bash
poc/ui/tools/build_desktop.sh --bg      # LOG=…, RC=… 출력 후 즉시 반환
poc/ui/tools/build_browser.sh --bg      # wasm-tools 워크로드 필요
cat <RC 파일>                            # 0 이면 성공
```

- Android SDK·JDK 경로는 환경변수 `PD_ANDROID_SDK`·`PD_JAVA_SDK`(Windows 경로)로 넘긴다. `xkxk.frameworks.ui.android.csproj` 는 이 값이 있으면 쓰고, 없으면 이 PC 의 실측 경로(`C:\Program Files (x86)\Android\android-sdk`, `C:\Program Files\Android\openjdk\jdk-21.0.8`)가 존재할 때 쓴다. `tools/build_android.sh` 는 환경변수가 비어 있을 때만 `%USERPROFILE%\pd-android\{sdk,jdk}` 가 있으면 설정하고 `WSLENV` 로 `dotnet.exe` 에 전달한다. 둘 다 없으면 워크로드 기본 탐색에 맡긴다.
- Android: `poc/ui/tools/build_android.sh --bg [Debug|Release] [PdDiagnostics=true|false]`(기본 Debug·true). 산출물은 `src/xkxk.frameworks.ui.android/bin/<구성>/net11.0-android/xkxk.frameworks.ui-Signed.apk`.
- **웹(.NET 11) SkiaSharp 우회**: `xkxk.frameworks.ui.browser.csproj` 에만 `SkiaSharp`·`SkiaSharp.NativeAssets.WebAssembly` 4.158.0-preview.2.26560.5 와 `HarfBuzzSharp`·`HarfBuzzSharp.NativeAssets.WebAssembly` 15.0.0-preview.2.26560.5 를 직접 참조하고, `SkiaSharp.dll`·`HarfBuzzSharp.dll` 은 `_AOT_InternalForceInterpretAssemblies`(SDK 내부 항목)로 AOT 에서 뺀다. 원인은 Avalonia.Skia 12.1.4 가 끌어오는 SkiaSharp 3.119 의 wasm 정적 라이브러리가 Emscripten 3.1.56 용이라 .NET 11(Emscripten 6.0.2)에서 `saveSetjmp` 링크 오류가 나는 것이고(근거 https://github.com/AvaloniaUI/Avalonia/issues/21700), 4.x 어셈블리를 Mono AOT 하면 크래시(`sgen-alloc.c:409`)해 해석 실행으로 둔다(크래시 위치는 obuild-2 보고). 위험: Avalonia.Skia 12.1.4 와 SkiaSharp 4.x 프리뷰는 보증된 조합이 아니며 데스크톱·Android 는 3.119 그대로라 판이 다르다. Avalonia 12.2(PR https://github.com/AvaloniaUI/Avalonia/pull/22196)가 NuGet 에 나오면 이 우회를 뺀다.
- **직렬화**: 세 스크립트는 `tools/build_lock.sh` 의 `flock`(`logs/.build_<구성>.lock`)으로 같은 Configuration 빌드를 한 번에 하나만 돌린다. 헤드(Desktop·Browser·Android)·`PdDiagnostics`·`PdChannel` 이 달라도 공유 프로젝트의 `obj/<구성>` 을 같이 쓰므로 겹치면 판본이 섞인다. Desktop 의 Work·Product·R2R 은 Release 잠금을 쓴다. 대기 상한은 `PD_BUILD_LOCK_WAIT`초(기본 1800), 넘기면 rc=75.
- **웹 프로파일 판(진단 전용, 판정 미사용)**: 브라우저 publish 에 `-p:PdDiagnostics=true -p:PdWasmProfile=true` 를 주면 wasm 함수 이름 맵과 브라우저 프로파일러 C# 구간이 들어간다(`src/xkxk.frameworks.ui.browser/xkxk.frameworks.ui.browser.csproj` 18~28행). 이 판은 SDK 제약으로 `-p:RunAOTCompilation=false`(비-AOT)라 시간 판정에 쓰지 않고 함수별 상대 몫만 본다. 프로파일 판을 만든 뒤 일반 웹 빌드는 obj 가 프로파일 상태라 `--no-incremental` 로 다시 빌드한다.
- **폐포 검사**: 빌드가 성공하면 끝에서 `tools/check_assembly_closure.py <폴더|apk>` 가 산출물 안 `Pd.*` 어셈블리끼리의 TypeRef·MemberRef 가 모두 정의돼 있는지 정적으로 본다(PE·webcil·APK 어셈블리 스토어 모두 — .NET 10 은 XALZ/LZ4, .NET 11 은 XABA 구조의 XAZS/Zstandard 스토어를 읽는다. Python 3.12 에 zstd 모듈이 없어 순수 Python 복호기를 내장하고, `.r2r.dll` 합성 네이티브 이미지는 메타데이터가 없어 제외한다. 외부 파일 없이 `check_assembly_closure.py --self-test` 로 파서 자가시험을 할 수 있다). 누락이면 스크립트 rc=4. 직접 실행: 누락 없음 rc=0, 누락 rc=1, 대상 없음 rc=2. 층 이동 뒤 한쪽 어셈블리만 낡은 채 묶인 산출물(예: Shell 은 `ProfileStores` 를 참조하는데 Data 에는 없음)을 실행 전에 잡는 용도다.

## 데스크톱 실행 (비전경)
```bash
poc/ui/tools/run_desktop_bg.sh -- --open grid:5,gantt:5,form:10 --layout tile \
  --screenshot /mnt/c/DATA/Project/pd/poc/ui/logs/shot.png --perf-out /mnt/c/DATA/Project/pd/poc/ui/logs/perf.json
```
- `--exit-after`(기본 30)와 `--bg-pos auto`(모든 모니터 영역 밖)가 자동으로 붙는다.
- 결과는 `RESULT=RUNNING|EXITED_NORMALLY|FAILED` 한 줄이다.

## 자동화 인자 (AutomationBridge L0)
| 인자 | 뜻 |
|---|---|
| `--open grid:5,gantt:5,form:10` | 종류별 문서 개수만큼 열기. 옛 이름(Grid·Gantt·Form, 대소문자 무시)·옛 숫자 0~2 와 함께 창 ID(예 `xkxk.demo.grid`)·별칭도 받고, 모르는 이름이면 자리표시 문서를 열고 진단 `XKUI0505` 를 낸다 |
| `--layout cascade\|tile\|tile-h\|tile-v\|tabbed` | MDI 배치 / 탭 모드 |
| `--move-all <dx,dy>` | MDI 문서 전체를 60프레임에 나눠 이동(드래그 근사, phase `move-all`) |
| `--sdi <n>` | 문서 n개를 SDI(플로팅)로 분리 |
| `--dedupe` | 종류명을 고정 제목으로 열어 중복 방지 확인(MDI-02, `doc_count`) |
| `--cmd <a,b,…>` | close-others·close-all·minimize-all·restore-all·next·prev·redock-all·grid-exercise·gantt-exercise·maximize·restore-max·pin·unpin·tray-on·tray-off·esc-close·dblclick-close·theme-dark·theme-light·theme-next·font-up·font-down·font-reset·wheel-up·wheel-down·reset-font(UiSettings.HandleCtrlInput — 실제 Ctrl+휠·Ctrl+0 과 같은 경로, 활성 문서 창 대상. 문서가 없으면 메인 창이라 무시, 결과 `wheel_handled_<n>`)·chrome-sdi·chrome-pin·chrome-lock·chrome-sync·chrome-dup·chrome-cache·chrome-refresh·chrome-help(UI-09 제목줄 버튼과 같은 메서드, 활성 문서 대상 — 결과 `chrome_after_<명령>`(sdi·pin·lock·sync·dup·cache=적중/전체)·`chrome_help`. 계측 이름은 `chrome.*`. chrome-cache·chrome-refresh 는 새로고침이라 대상 문서가 새 객체로 바뀐다)·perf-reset(조작 기록 `operations` 초기화 — 측정 직전에 쓴다. 이름별 최근 2000건만 보관)·perf-ui(PERF-UI, 시작 시 자동 초기화: 문서 1개·20개 조건에서 글자·배율·테마·폰트·열기/닫기·활성 전환·배치·최소/최대/복원·SDI·레이아웃·그리드 정렬/스크롤·간트 줌/스크롤을 각 10회 → `operations` 의 `perfui.n{1|20}.*`)·zoom-in·zoom-out·zoom-reset·zoom-mode-font·zoom-mode-window 순서 실행. 테마·글자 명령은 `theme_after_<명령>`·`font_after_<명령>` 기록. 결과는 perf JSON `docs_after_<명령>`·`state_after_<명령>`·`pinned_after_pin|unpin`·`tray_after_tray-on|tray-off` |
| `--font-size <n>` | 기준 글자 크기(UiSettings.BaseFontSize, 10~24). 제목·세부 등 토큰은 기준 대비 상대 크기로 함께 바뀐다 |
| `--font <이름>` | 폰트 이름(UiSettings.FontName, 예: `Noto Sans KR`) |
| `--theme <id>` | 색 테마 선택(ThemeAutomation.Select — pd-light·pd-dark·pd-contrast·pd-ocean·pd-ember·pd-forest). 결과 `theme_selected`·`theme_cmd_<n>`(ok/fail·메시지) |
| `--cmd theme…` | 색 테마 명령은 ThemeAutomation 으로 전달: `theme:<id>`·`theme-next`·`theme-clone:<이름>`·`theme-set:<토큰>=<#hex>[,…]`·`theme-save`·`theme-delete:<id>`·`theme-export:<경로>[\|<id>]`·`theme-import:<경로>`·`theme-open-editor`. 대소문자 보존. `theme-dark`·`theme-light` 는 라이트/다크 모드 전환. 결과는 `theme_cmd_<n>`·`theme_cmd_fail` |
| `--zoom <퍼센트>` | 활성 문서 창 배율(50~200, `1.5` 도 허용). 결과 `zooms`(문서Id=배율) |
| `--frame mars\|pd` | 문서 창 외곽선 스타일(UI-10, WindowCommands.MarsFrame). 결과 `frame` |
| `--shadow on\|off` | 문서 창 그림자(UI-10, WindowCommands.ShadowEnabled) — PERF-UI 그림자 on/off 비교. 결과 `shadow` |
| `--frame-pump on\|off` | 측정 중 렌더 루프 유지(기본: `--perf-out` 있으면 on). 유휴 정지로 프레임 간격이 2 vsync 로 늘어나는 아티팩트 제거 — 근거 `logs/odev3/perf_validity.md` |
| `--save-layout` / `--load-layout` | 레이아웃 저장 / 복원 |
| `--screenshot <png>` | 창 캡처(데스크톱 전용) |
| `--perf-out <json>` | 계측 결과 JSON(브라우저는 콘솔 `PERF_JSON:`). 실행 끝의 `theme`(ThemeAutomation.DescribeJson 객체 — activeThemeId·mode·builtinThemes·userThemes 등)·`theme_variant`(light\|dark)·`theme_id`·`theme_setting`·`base_font_size`·`font_family`·`wheel_mode`·`zooms`·`frame_pump`·`layout`(SerializeLayout 객체 — Documents[].ZOrder·ActiveId. 저장 실행과 `--load-layout` 실행의 값을 비교해 Z 순서 복원 판정)·`operations`(조작별 count·sync/frame p50·p95·max — PerfProbe.TimeOperation) 포함 |
| `--exit-after <초>` | 지정 시간 뒤 종료 |
| `--bg-pos auto\|x,y` | 메인 창 위치(데스크톱 호스트 전용) |
| `--main-size WxH` | 진단 빌드 전용(데스크톱 호스트): 기동 시 메인 창 클라이언트 크기(논리px, 예 `2560x1392`). 화면 밖(`--bg-pos auto --offscreen-guard on`)에서 큰 창 캡처용, 제품판에는 없음 |
| `--render-dirty-regions on\|off` | 진단 빌드 전용(데스크톱 호스트): 이번 실행만 영역 dirty 추적(시작 옵션 `RenderDirtyRegions`)을 켜거나 끈다. 설정값보다 우선한다. 기본 끔 확정(01 §4.2 — 데스크톱 렌더 약 6배 손해) |
| `--timer-res N` | 진단 A/B 전용(데스크톱 호스트, 기본 동작 불변): 기동 시 winmm `timeBeginPeriod(N)`(N=1~15ms, 환경변수 `PD_TIMER_RES` 도 됨). B2 펌프 끔 p95 에 차이가 없어 기각했고 비교용으로만 남긴다 |
| `--profile short\|full` | `--cmd perf-ui` 규모. short(웹·모바일, 웹은 `?cmd=perf-ui&profile=short`) = 문서 1·5개 × 조작별 3회, full(기본·데스크톱) = 1·20개 × 10회. 결과 `perfui_profile` |
| `--reps N` | `--cmd perf-ui` 조작별 반복 수 덮어쓰기(기본=profile 값 full 10 / short 3). 결과 operations 에 `*_p95_interp_ms`(선형보간)·`frame_cold_ms`·`frame_warm_*` 추가 필드 |
| (로그) | perf-ui 는 조작마다 `[perf-ui] 시각 start/end 이름 sync=..ms frame=..ms` 를 남기고 2초 초과는 `SLOW` 를 붙인다. PerfProbe 는 rAF 간격 1초 초과를 `[perf] 시각 frame gap Nms phase=…` 로 항상 남긴다(UI 스레드 정지 추적). 데스크톱은 `--log` 파일, 브라우저는 console(`window.__pdPoc.logs`) |
| `--fresh` | 깨끗한 시험용(셸이 소비): 기동 시 레이아웃 자동 복원 안 함, 종료 시 자동 저장 안 함. 웹은 `?fresh`. 사용자 경로 시험(`--mode user`)에는 붙이지 않는다. 인자가 있는 자동화 실행은 원래도 자동 저장·복원이 꺼져 있다 |
| `--layout-file <json>` | 레이아웃 파일 경로(데스크톱, 기본 `%LOCALAPPDATA%\xkxk\frameworks\ui\layout.json`) |
| `--log <파일>` | 앱 로그 경로(데스크톱 호스트 전용) |
| `--layout-key <키>` | localStorage 키(브라우저, 기본 `pd-poc-layout`) |

## 브라우저 실행
```bash
poc/ui/tools/serve_browser.sh start 5791
# http://127.0.0.1:5791/?open=grid:5,gantt:5,form:10&layout=tile&save-layout&exit-after=10
poc/ui/tools/serve_browser.sh stop 5791
```
- 쿼리 키는 `--키` 로, 값은 다음 인자로 전달된다. 값 없는 키는 플래그다.
- 진행 상태는 `window.__pdPoc`(args·runtimeReadyMs·mainStartedMs·error·logs)로 읽는다.
- SDI 는 브라우저에서 관리형 창(DockSettings.UseManagedWindows)으로 대체된다.
- 브라우저 `PERF_JSON:` 은 `--perf-out` 인자(URL 쿼리 `perf-out=<임의 경로>`)가 있어야 콘솔에 출력된다. 없으면 계측만 하고 출력하지 않는다.
- 자동 입력 시험 주의: Avalonia Browser 는 keydown 과 조합(composition) 이벤트로만 글자를 받는다. CDP `Input.insertText` 단독(조합 없이 beforeinput/input 만 발생)은 숨김 입력 요소 값만 바뀌고 TextBox 에는 반영되지 않는다(2026-10-28 실측, 모든 칸 동일). 한글은 `Input.imeSetComposition` → `Input.insertText` 순서로, 영문은 `page.keyboard.type` 으로 입력한다.

## MDI-12 화면 밖 창 정상화 (데스크톱 호스트)
- 메인 창이 열릴 때, SDI 창이 열리거나 위치가 바뀔 때(레이아웃 복원 포함) 판정한다. 제목줄(위 32px)이 어느 모니터 작업영역에도 120×16px 이상 보이지 않으면 가장 가까운 모니터 작업영역 안으로 옮긴다. 작업영역보다 크면 줄인다.
- 앱 로그에 `[host] MDI-12 normalize <창> (x,y)->(x,y) size=WxH screen=...` 한 줄을 남긴다.
- `--bg-pos` 가 있으면(비전경 시험 — 의도적 화면 밖) 정상화하지 않는다. 이때는 반대로 SDI 창을 화면 밖으로 되돌린다.

검증 절차(`--bg-pos` 없이, 화면에 잠깐 창이 뜬다):
```bash
L=/mnt/c/DATA/Project/pd/poc/ui/logs/mdi12_layout.json
# 1) SDI 1개가 든 레이아웃 저장(비전경)
poc/ui/tools/run_desktop_bg.sh -- --open form:2 --sdi 1 --save-layout --layout-file $L --exit-after 8
# 2) 저장본의 플로팅 창 좌표(Window.X/Window.Y)를 화면 밖 값으로 바꾼다
python3 - "$L" <<'EOF'
import json, sys
p = sys.argv[1]; d = json.load(open(p, encoding="utf-8"))
def walk(o):
    if isinstance(o, dict):
        for wk in ("Window", "window"):  # 직렬화 이름 규칙(Pascal/camel) 모두 대응
            w = o.get(wk)
            if isinstance(w, dict):
                w["X" if "X" in w else "x"], w["Y" if "Y" in w else "y"] = 50000, -30000
        for v in o.values(): walk(v)
    elif isinstance(o, list):
        for v in o: walk(v)
walk(d); json.dump(d, open(p, "w", encoding="utf-8"), ensure_ascii=False)
EOF
# 3) 복원 — --bg-pos off 로 기동해 정상화 로그 확인
poc/ui/tools/run_desktop_bg.sh -- --bg-pos off --load-layout --layout-file $L --exit-after 8
grep "MDI-12 normalize" poc/ui/logs/desktop_*.log | tail -3
```
- 합격 기준은 3) 의 로그에 `sdi '...' (50000,-30000)->(x,y)` 가 있고, (x,y) 가 실제 모니터 작업영역 안인 것이다.
- `--bg-pos off` 는 run_desktop_bg.sh 의 자동 `--bg-pos auto` 추가를 막는 값이다(호스트는 off 를 일반 모드로 해석).

## 원격 제어(UI-08) — AI·스크립트가 실행 중인 앱을 조작

실행 중인 앱에 로컬 HTTP/JSON 제어 채널을 연다. **진단 빌드(`-p:PdDiagnostics=true` → `PD_DIAGNOSTICS`) 전용**이다. 제품(기본) 빌드에는 서버·명령·웹 브리지(`AutomationExports` JSExport) 코드가 들어가지 않는다. 남는 것은 `RemoteProductStubs.cs` 의 빈 껍데기뿐이고, 서버는 항상 꺼져 있으며 모든 요청을 거부한다. Android 도 Debug 여부와 관계없이 진단 빌드에서만 켜진다. 진단 빌드에서도 기본은 꺼져 있다. 켜는 방법은 두 가지다.

- 기동 인자 `--remote-control`(기본 포트 5960) 또는 `--remote-control=포트` 로 켠다.
  - 23:08 빌드부터 데스크톱 호스트(Program.cs)가 `--remote-control`·`--remote-control N`·`--remote-control=N` 을 앱 인자에서 뺀다. 그래서 원격 제어만 준 실행은 사용자가 실행할 때와 같다(기동 자동 복원·종료 자동 저장). 23:33 `retest_remote_z.py --mode user` 로 실측했다.
  - 그 이전 판본(23:08 빌드 전)에서는 이 인자가 앱 인자로 남아 종료 자동 저장과 기동 자동 복원이 꺼졌다(App.axaml.cs·MainView.axaml.cs 의 `StartupArgs.Count == 0` 조건).
- 깨끗한 시험은 `--fresh` 로 격리한다. 두 판본 모두 `--remote-control --fresh` 로 띄운다. `--fresh` 는 셸이 소비해 기동 자동 복원을 건너뛰고 종료 자동 저장을 끈다. 테마·글자 크기 같은 설정(settings.json)은 `--fresh` 와 관계없이 복원되므로, 초기화가 필요하면 `settings.set` 으로 맞춘다. 브라우저는 URL `?fresh` 가 같은 효과다.
- 환경변수 `PD_REMOTE_CONTROL=1`(또는 포트 번호)로도 켤 수 있다. WSL 에서는 `WSLENV=PD_REMOTE_CONTROL` 로 Windows 프로세스에 넘긴다. `run_desktop_bg.sh` 는 이 환경변수가 있으면 `--exit-after 30` 을 붙이지 않는다(odev-4b 반영, **실측 전**). WSLENV 에 방향 플래그(`/w` 등)를 붙이면 전달되지 않는다.

수신·인증 규칙은 다음과 같다.
- **루프백 전용**이다. `TcpListener` 를 `127.0.0.1` 과 `[::1]` 에만 바인드하므로 다른 네트워크 주소로는 연결 자체가 되지 않는다. 요청의 원격 주소가 루프백이 아니면 403 으로 거부한다(IPv4 매핑 IPv6 도 정규화해 확인). `/api/health` 도 이 검사 뒤에 응답한다. 23:30 실측 결과는 다음과 같다. 데스크톱 netstat LISTEN 은 `127.0.0.1:5960`·`[::1]:5960` 뿐이다. 다른 NIC 주소는 `Host: localhost` 헤더를 붙여도 연결이 거부된다. Android 는 시작 로그에 같은 bind 가 찍힌다.
  - 예전에 쓰던 HttpListener(http.sys)는 `http://localhost:포트/` 접두사가 모든 주소에서 연결을 받고 Host 헤더로만 걸렀다(otest-1 실측). 루프백 IP 접두사는 관리자 권한(urlacl)이 필요하다. 그래서 교체했다.
- 포트가 사용 중이면 +1씩 최대 10회 다시 시도한다. 실제 바인드 주소는 시작 로그(`bind=127.0.0.1:5960,[::1]:5960`)와 연결 정보 파일의 `bind` 배열로 확인한다.
- 토큰은 기동할 때마다 무작위 32자로 만든다. 요청마다 `X-Pd-Token: <토큰>` 헤더, `Authorization: Bearer <토큰>`, `?token=` 중 하나로 보낸다. `/api/health` 만 토큰 없이 응답한다.
- 연결 정보는 `%LOCALAPPDATA%\xkxk\frameworks\ui\remote-control.json`(url·port·token·pid·bind·started)에 기록하고 호스트 로그에도 남긴다. 종료할 때 파일을 지운다. Android 는 앱 전용 폴더(`/data/user/0/xkxk.frameworks.ui/files/xkxk/frameworks/ui/remote-control.json`, 22:42 실측)다.
- 모든 조작은 UI 스레드(Dispatcher)에서 실행한다. 실입력(SendInput·커서·전경 획득)은 쓰지 않는다. 클릭은 Automation Peer 패턴(Invoke/Toggle/SelectionItem/ExpandCollapse)으로 한다.
- HTTP 처리는 최소 구현이다(RemoteHttp.cs). 한 연결에 요청 1건만 받고 `Connection: close` 로 응답한다. 헤더는 64KB, 본문은 16MB 까지이고 요청 하나는 120초가 지나면 끊는다.
- 브라우저(WASM)는 소켓 수신이 없어 HTTP 서버를 쓸 수 없다. 대신 `window.pdAutomation.invoke({cmd, args})` 브리지(`RemoteCommandRouter.Default.InvokeJsonAsync`)로 같은 명령을 부른다.
- 실행 인자 자동화(AutomationBridge, 위 "자동화 인자" 절)는 기동 시 1회 시나리오용이고, 원격 제어는 실행 중 대화형 조작용이다. 둘은 같은 셸 명령(IShellCommands)을 쓴다.

### 보안 주의
- 개발·시험 전용이다. 운영(Release 배포) 빌드에서는 켜지 않는다. 배포 스크립트와 바로가기에 `--remote-control` 을 넣지 않고, 배포 환경에는 `PD_REMOTE_CONTROL` 을 설정하지 않는다. Android 는 Debug 빌드에서만 켜진다(`#if DEBUG`).
- 루프백 전용이라 같은 PC 밖에서는 접근할 수 없다. CORS 헤더를 보내지 않으므로 다른 출처의 웹 페이지 스크립트는 응답을 읽을 수 없다. 토큰이 없으면 401 이다.
- 토큰 파일은 같은 Windows 사용자가 읽을 수 있다. 같은 사용자 계정에서 도는 다른 프로세스는 막지 못한다.

### 실행 슬롯 규칙(데스크톱)
- 데스크톱 앱 실행은 반드시 odev-4b(실행 슬롯 관리)에게 슬롯을 요청해 받은 뒤에 한다.
- `--no-kill` 로 기존 프로세스를 둔 채 동시에 띄우지 않는다. `run_desktop_bg.sh` 는 기본적으로 다른 PD 앱이 실행 중이면 rc=3(BUSY)로 실패한다. `--kill` 은 슬롯을 받은 사람만 쓴다.
- 시험이 끝나면 `app.shutdown` 또는 `app.closeMain` 으로 앱을 종료하고 odev-4b 에게 반납을 알린다.
- 데스크톱 앱이 Windows 5960 포트를 쓰는 동안에는 Android `adb forward` 의 Windows 쪽 포트를 5960 이 아닌 값(예: 15960)으로 한다(22:42 실측, bind 오류 10013).

### 호출 방법(WSL)
WSL2 NAT 모드에서는 WSL 의 curl 이 Windows localhost 에 닿지 않는다(Mars INFRA.md 실측과 같다). 그래서 Windows `curl.exe` 를 쓰는 `tools/pdctl.py` 로 호출한다. 연결 정보 파일 경로는 `PD_REMOTE_INFO` 로 바꿀 수 있다.

```bash
python3 tools/pdctl.py status
python3 tools/pdctl.py docs.open kind=grid count=3
python3 tools/pdctl.py docs.arrange mode=tile-v
python3 tools/pdctl.py tree.get doc=1
python3 tools/pdctl.py ui.click id=shell.btnTheme
python3 tools/pdctl.py settings.set theme=Dark baseFontSize=16
python3 tools/pdctl.py input.ctrlWheel delta=1 doc=1
python3 tools/pdctl.py wait.for docCount=3 timeout=5000
python3 tools/pdctl.py screenshot doc=1            # → result.path (Windows 경로)
python3 tools/pdctl.py screenshot.download out=/tmp/a.png window=0
```

직접 호출하는 예는 다음과 같다(토큰은 연결 정보 파일에서 읽는다).
```bash
/mnt/c/Windows/System32/curl.exe -s -H "X-Pd-Token: <토큰>" -H "Content-Type: application/json" \
  -d "{\"cmd\":\"docs.list\"}" http://localhost:5960/api/cmd
/mnt/c/Windows/System32/curl.exe -s -H "X-Pd-Token: <토큰>" "http://localhost:5960/api/logs/latest?lines=20"
```

경로형 호출 `/api/<영역>/<동작>`(쿼리와 JSON 본문이 인자)도 받는다. 예는 `/api/docs/open?kind=gantt` 다. 응답은 `{"ok":true,"cmd":..,"result":..,"ms":n,"elapsed_ms":n,"ui_frame_ms":n}` 또는 `{"ok":false,"cmd":..,"error":..}` 다.

### Android(Debug APK)
Debug APK 는 기동하면 기기 안 `localhost:5960` 에서 원격 제어를 켠다. 23:28 Fold5 에서 루프백 전용 판(APK md5 2aa85531)을 실측했다. 시작 로그 bind 는 `127.0.0.1:5960,[::1]:5960` 이다. adb forward 연결은 기기 루프백으로 들어오며 health 200 이다. status 의 platform 은 `android` 다.

1. 설치·실행: `adb -s <기기> install -r xkxk.frameworks.ui-Signed.apk` 뒤 `adb shell monkey -p xkxk.frameworks.ui -c android.intent.category.LAUNCHER 1`. logcat `DOTNET` 태그에 `[remote] 원격 제어 서버 시작 … token=…` 이 찍힌다.
2. 포트 연결: `adb -s <기기> forward tcp:15960 tcp:5960`. Windows 쪽 포트는 데스크톱 앱과 겹치지 않는 값을 쓴다.
3. 토큰: `adb -s <기기> exec-out run-as xkxk.frameworks.ui cat files/xkxk/frameworks/ui/remote-control.json > /tmp/pd_android.json` 로 받은 뒤 JSON 의 `port` 를 15960 으로 바꾼다.
4. 호출: `PD_REMOTE_INFO=/tmp/pd_android.json python3 tools/pdctl.py status`. 명령 체계는 데스크톱과 같다.
5. 원복: `adb forward --remove tcp:15960`, `adb uninstall xkxk.frameworks.ui`. 기기 화면 설정(wm size·density 등)은 바꾸지 않는다.

### 명령 표
| 명령 | 인자 | 결과 |
|---|---|---|
| help | - | 명령 목록 |
| ping / status | - | pong / pid·플랫폼·창 수·문서 수·활성 문서·테마·글자 크기·포트 |
| windows.list | - | 최상위 창(0=메인, 1~=SDI 실 창) 제목·활성·위치·크기·상태·포함 문서. 관리형 SDI 창(브라우저·Android)은 `type=ManagedWindow, managed=true, index=null` 로 덧붙이고, 그 문서는 메인 항목 docs 에서 뺀다(23:28 Android 실측) |
| docs.list | - | 문서 id·title·kind·active·state·window·floating·managedWindow·zoom·zIndex·childIndex. floating 은 셸 판정(WindowCommands.IsFloating)이고, managedWindow 는 메인 화면 안에 그려지는 관리형 창이면 true 다. zIndex·childIndex 는 MDI 창의 실제 화면 쌓임 순서다(MDI 가 아니면 null). 23:28~33 실측으로 확인했다. floating·managedWindow 는 Android 에서 확인했고, zIndex 는 3창 스크린샷의 렌더 순서와 일치했다 |
| docs.open | kind=grid\|gantt\|form [title] [sdi] [allowDuplicate] [count] | 연 문서 |
| docs.close | [doc] \| all=true \| others=true | 남은 문서 |
| docs.activate | [doc] \| dir=next\|prev | 활성 문서 |
| docs.arrange | mode=cascade\|tile-h\|tile-v\|tabbed \| preset=MdiCascade\|Tabbed\|MdiTileVertical | 문서 |
| docs.sdi / docs.redock | [doc] [on=true\|false] / [doc] \| all=true | 문서 |
| docs.state | state=minimized\|maximized\|normal [doc] \| all=minimize\|restore \| restoreLast=true | 문서 |
| docs.rename / docs.pin | title= / [doc] | 문서 / pinned |
| docs.zoom | [doc] zoom=0.5~2.0 \| step=in\|out\|reset | 문서 |
| docs.resetLayout / docs.moveAll / chrome.moveLight | - / dx= dy= [full=1] / [on=true\|false] [quietMs=50~1000] [staticCache=true\|false] [bitmapCache=true\|false] [siblings=true\|false] [hitTestOff=true\|false] [coalescePointer=true\|false] [releaseFlush=true\|false] [staticLightEarly=true\|false] [armNear=true\|false] [armRadius=24~400] [siblingsPerFrame=0~16] [restorePerFrame=0~16] [inactiveDim=opacity\|brush](준 값만 이번 실행에 설정·저장 안 함, 생략=조회) [grabZoneReset=true] [coalesceReset=true] | 문서 / {moved,dx,dy,docCount}(full=1 이면 예전 전체 목록) / {enabled,quietMs,bitmapCache,staticCache,siblings(정지 창 가벼운 크롬, 설정 MdiMoveLightSiblings),hitTestOff,coalescePointer,releaseFlush,staticLightEarly,armNear·armRadius·siblingsPerFrame(옵션 A·B, `plan/09_화면설계/01a_화면설계_공통원칙_끌기경량크롬.md` 'A15 수단 변경'),spread{frames,canceled},restorePerFrame(P2 놓은 뒤 정지 창 복원 프레임당 창 수, 설정 MdiMoveLightRestorePerFrame, 같은 문서 'P2' 절 — 측정 전 이 적용값을 먼저 확인),restore{frames,flushed,queued},inactiveDim(비활성 흐림 방식, 설정 ChromeInactiveDim),renderDirtyRegions(시작 옵션 적용값, 조회만),staticCacheAppliedTotal(정지 창 캐시를 건 누계),staticCacheActive(지금 캐시 중인 창 수),hitTestOffAppliedTotal,hitTestOffActive,grabZone(K1·K2 카운터),coalesce{held,replayed,flushed,dropped},k67{releaseFlushed,earlyComputed,earlyUsed,siblingsBounded}} |
| chrome.frameDiag | [on=true\|false] [capacity=256~32768] [tailMs=0~3000] [dispatch=true\|false](준 값만 설정) [dump=true] [reset=true] — 진단 회차 전용(판정 회차에 켜지 않는다), MDI 창 누름~놓은 뒤 tailMs 의 rAF 마다 1행 | {armed,capacity,tailMs,dispatch,drags,rows_n,dropped,commitsSeen,layoutPassesSeen(0 이면 그 열 무효),flags{release,restore,spread,near,dragging,first},fields[drag,t_ms,interval_ms,moves,gc0,gc1,gc2,alloc_bytes,layout_ms,layout_passes,queued_measure_max,queued_arrange_max,commits,ui_span_ms,dispatch_ms,flags,down_ms,move_ms,move_max_ms,tj_begin_ms,flush_started_ms,rel_total_ms,rel_caplost_ms,rel_tabjoin_ms,rel_tabjoin_reset_ms,rel_snap_ms,rel_snap_reset_ms,rel_snap_log_ms,apply_ms],rows[][]}(dump=true 일 때 행 포함, `plan/09_화면설계/01a_화면설계_공통원칙_끌기경량크롬.md` 'P1' 절) |
| tree.get | [window=n\|doc] [named=true\|false] [depth] | 컨트롤 트리(type·id·name·text·enabled·visible·bounds·checked·selectedIndex·value) |
| ui.find / ui.click | id=AutomationId\|Name("id#2"=2번째) [doc\|window] — doc=그 문서 창(MDI 창 틀·SDI 실 창 전체, 제목줄 버튼 포함) | 컨트롤 정보 |
| ui.setText / ui.select / ui.toggle | id= text= / id= index=\|text= / id= [on] | 컨트롤 정보 |
| ui.invoke | command=ToggleTheme… [param] [doc] (생략 시 목록) | 실행한 명령명 |
| settings.get / settings.set | - / theme= themeId=(ThemeCatalog.Select) baseFontSize= documentDefaultFontSize= fontName= wheelMode=FontSize\|WindowZoom gridViewPool=true\|false prewarmViews=true\|false | 설정(토큰별 글자 크기 포함). gridViewPool·prewarmViews 는 ?fresh 를 유지한 채 표 열기 켬·끔을 비교하는 스위치이고 값이 저장되므로 시험 뒤 원복한다. 글자 크기·배율은 문서 창에만 적용되고 메인 크롬은 고정이다 |
| settings.fonts / themes.get | - | 폰트 목록 / 테마 카탈로그 |
| theme.select·next·clone·set·save·delete·export·import·openEditor | id= / name= / colors=Accent=#RRGGBB,… / path= | 메시지 |
| chrome.state·sdi·pin·dup·sync·lock | [doc] | 문서 창 제목줄 버튼(UI-09) 실행 후 floating·pinned·allowDuplicate·sync·cacheHits·cacheTotal·locked·mdiBounds·canResize(SDI) |
| chrome.refresh / chrome.cache | [doc] | 새로고침(원래 문서는 닫힘) → 새 문서 상태(doc=새 Id, closed=옛 Id) |
| chrome.help / chrome.shadow / chrome.frame | [doc] / [on=true\|false] / [mars=true\|false] | 도움말 문구 / 입체 그림자 / 외곽선(Mars 1px·PD 2px) 현재 값 |
| chrome.tune | [spread=0.25~3] [opacity=0~3] [highlight=0~2] [style=Windows\|MacOS\|MarsClassic] [accentTitle=true\|false] | 창 입체감 조절(준 값만 설정) → 현재 값 전체(Mars 비교 캡처용) |
| window.snapshot | - | 메인 창 위치·외곽/클라이언트 px·상태·모니터별 Bounds/WorkingArea + SDI 창별 경계·상태(윈도우 메뉴 판정용) |
| window.autosave·escClose·dblClose .get/.set | on=true\|false | 윈도우 메뉴 ①② 등 설정 토글 → 실제 값 |
| window.captureProtect.get/.set | on=true\|false | ⑬ 설정값(setting)과 실제 적용값(applied — 데스크톱 표시 친화도·Android FLAG_SECURE) |
| window.main.reset | - | ④ 메인 창·MDI 초기화 → before/after 스냅샷 |
| sdi.minimizeAll·restoreAll·closeAll | - | ⑧⑨⑩ SDI 창 일괄 → before/after 스냅샷 |
| lang.get / lang.set | - / code=ko\|en | 다국어(Q17): 현재 언어·문화권·사전 언어 목록·키 수 / 실시간 전환 → 실제 적용값 |
| i18n.missing(별칭 lang.missingKeys) | [lang=en] | ko 에는 있는데 lang 에 없는 키(번역 누락) |
| i18n.scan(별칭 lang.scan) | [window=n\|doc] | 보이는 화면 글자 중 한글이 든 것(언어가 ko 가 아닐 때의 미번역 후보) — 텍스트·AutomationId·위치 |
| input.ctrlWheel | delta=1\|-1\|0 [doc] \| target=main | UiSettings.HandleCtrlInput(실입력과 같은 경로) → handled·before·after. 메인 크롬은 handled=false(조절 불가) |
| (모든 조작 명령 응답) | - | ms(처리), elapsed_ms(요청 수신 → 처리 + 렌더 2프레임 반영), ui_frame_ms(처리 종료 → 렌더 반영, 창이 숨겨져 500ms 안에 프레임이 없으면 null). PerfProbe operations 에 "remote.<명령>" 이름으로 누적된다(sync=수신→처리 끝, frame=수신→두 번째 프레임) |
| perf.reset | - | 조작 기록 초기화(측정 시작 전, --cmd perf-reset 과 같음) |
| perf.ops | [op=이름 접두(예 remote.docs.)] | PerfProbe operations 의 count·sync/frame p50·p95·max(PERF-UI). 앱 내부 TimeOperation 기록도 함께 나온다 |
| screenshot | [window=n] \| [doc] [path] [inline=true] | PNG 경로(+base64). 관리형 창은 doc= 로 찍는다 |
| screenshot.download(HTTP 전용) | 위와 같음 | PNG 바이트 |
| render.overlay(진단 빌드 전용) | [dirtyRects=true\|false] [fps=true\|false](준 값만 이번 실행에 설정, 생략=조회) | {dirtyRects,fps,value}. Avalonia 렌더러 디버그 오버레이(다시 그린 영역 사각형·FPS, FPS 줄의 V=방문 visual 수·R=그린 visual 수)를 켜고 끈다. 오버레이를 켜면 렌더 경로가 바뀔 수 있어 켠 실행의 시간 값은 판정에 쓰지 않고 캡처 관찰에만 쓴다 |
| perf.get / log.tail | - / n=50 | PerfProbe JSON(진단 필드 `frame_hist_*`, 레이아웃 패스 `layout_pass_samples·_avg_ms·_p50_ms·_p95_ms·_max_ms·_count·_frames·_measure_max·_arrange_max` 포함 — 판정식 불변) / 로그 패널 최근 줄 |
| layout.save / layout.load | - | true / 복원 여부 |
| layout.get | - | 현재 레이아웃 JSON(저장 없이). `Documents[].ZOrder`(0=맨 아래)·`ActiveId`·`IsPinned`·`IsLocked` 를 그대로 담는다 |
| wait.for | timeout=ms + docCount= \| control=id \| gone=id \| id=+text=\|contains= \| active=제목 | met·elapsedMs (초과 시 오류) |
| app.closeMain | - | 메인 창 닫기. 사용자 X 버튼과 같은 Window.Close 경로이고 Closing 에서 종료 자동 저장이 일어난다(`--fresh` 로 띄웠으면 저장하지 않는다). 23:33 실측으로 확인했다 |
| app.shutdown | - | 앱 종료(자동 저장 없음) |

Mars 호환 별칭은 `/api/windows`(windows.list), `/api/logs/latest?lines=`(log.tail), `/api/window/open·close·activate·sdi-toggle`, `/api/screenshot`, `/api/screenshot/download`, `/api/health`, `/api/status` 다.

### 시험 스크립트
- 재사용 시험 도구는 `test/` 아래에 측정(measure)·판정(judge)·기록(record) 계층으로 모여 있다. 색인·안전 규칙·옛 경로 대응·archive 실행법은 `test/README.md` 를 본다(2026-10-31 D-TESTDIR).
- `test/common/measure/retest_remote.py`: 테마 전환, 글자 크기·창 배율, 제목줄 버튼(위치 고정과 손잡이 숨김 포함), 창 20개 배치, 조작별 p50/p95 표를 실행한다. 앱은 `--remote-control --fresh` 로 먼저 띄워 둔다. 23:30 데스크톱 실측 결과는 명령 96/실패 0, 검사 33/실패 0 이다(`logs/odev6r/retest_remote_desktop.json`).
- `test/desktop/measure/retest_remote_z.py`: 창 5개의 Z순서·활성·핀·잠금을 저장 → 재기동 → 복원한 뒤 대조한다. 모드는 두 가지다.
  - `--mode explicit`: `--remote-control --fresh` 로 띄우고 save/load 를 명시한다.
  - `--mode user`: `--remote-control` 만 주고 띄운 뒤 메인 창 닫기로 자동 저장하고, 재기동 자동 복원 결과를 대조한다.
  - 23:31~33 실측 결과, 두 모드 모두 ① 저장 ZOrder·ActiveId 와 ③ 핀·잠금 복원은 통과했다. ② 화면 쌓임은 실패했다. 원인은 셸의 실시간 MDI 쌓임이 MRU 가 아닌 결함 후보다(`logs/odev6r/remote_smoke_2330.md`). 재기동에 `run_desktop_bg.sh --kill` 을 쓰므로 반드시 슬롯을 받은 뒤 실행한다.
- `test/desktop/measure/retest_window_menu.py`(+`retest_window_menu_items.py`): Mars 윈도우 메뉴 13항목(판정보고서 §3.5 PD 결정 기준)을 데스크톱·Android·웹 공용으로 시험한다. 항목마다 실행 전후를 대조해 PASS/FAIL/N/A/PENDING 을 내고, 설정형 항목은 재기동 뒤 유지도 본다. **실측 전**(셸 API·진단 빌드 대기)이다.
- `tools/check_i18n.py`: 다국어 정적 검사. ① 언어별 키 집합이 같은지 ② 화면에 보이는 하드코딩 한글(axaml·cs, 로그·주석·AutomationId·진단 코드 제외)이 남았는지 ③ 사용한 키가 사전에 있는지 본다. 이관 중에는 `--report-only` 로 ②를 보고만 한다. 의도적 예외는 줄 끝에 `// i18n-ok 사유` 를 단다.
- `wait.for gone=<id>` 는 컨트롤이 시각 트리에서 없어졌거나 보이지 않으면(IsEffectivelyVisible=false) 충족이다. 숨김만 되는 요소(예: 위치 고정 시 `chrome.gripResize`)도 판정된다.

### AutomationId 규약
`화면약어.컨트롤약어` 형식을 쓴다(예: `shell.btnTheme`, `theme.cboTheme`, `grid.dgMain`). 같은 문서를 여러 개 열면 `id#2` 처럼 순번을 붙이거나 `doc=` 로 범위를 좁힌다. 문서 3종의 Id 는 다음과 같다.

| 문서 | AutomationId |
|---|---|
| 그리드 | `grid.dgMain`(DataGrid) · `grid.txtFilter`(필터) · `grid.lblCount`(행 수) · `grid.lblTiming`(필터·정렬 ms) |
| 간트 | `gantt.canvas`(직접 그리기 캔버스) · `gantt.btnZoomIn` · `gantt.btnZoomOut` · `gantt.lblZoom` · `gantt.hScroll` · `gantt.vScroll` |
| 입력폼 | `form.txtTitle`(작업명) · `form.txtOwner`(담당자) · `form.txtClient`(거래처) · `form.txtPlace`(위치) · `form.txtCategory`(분류) · `form.txtBody`(내용) · `form.btnSave` · `form.btnClear` · `form.lblSaved`(저장 결과) |

## 도움말 카탈로그(투어·단축키·기능 레이어)
정본은 `src/xkxk.frameworks.ui.shell/Tour/tour_catalog.json` 과 사전 `src/xkxk.frameworks.ui.shell/Localization/sysview.{ko,en}.json` 이다. 코드 수정 없이 아래를 고칠 수 있다.

| 하려는 일 | 고칠 곳 |
|---|---|
| 단계 추가 | `steps` 에 줄 추가, 사전에 `sysview.tour.<id>.title`·`.body` 추가 |
| 순서 변경 | `steps` 의 줄 순서(같은 `scope` 안에서 위가 먼저) |
| 문구 변경 | 사전 값(ko·en 함께). 다른 단계 문구를 쓰려면 `"text": "<그 id>"` |
| 폼팩터 조건 | `modes`(`Mdi`·`Dock`·`Nav`·`Wide`·`All`)·`platforms`(`Desktop`·`Web`·`Android`·`All`), `\|` 로 묶는다 |
| 조건부 표시 | `requires`: `TourConditions` 의 이름 또는 `permission:<이름>`(권한 체계가 없어 지금은 허용) |
| 단축키 | `shortcuts` 에 줄 추가. `binding` 은 메인 화면 키 바인딩을 만들고, `handler`(`파일:표식`)는 전용 처리기가 있는 곳을 적는다. 새 명령만 `src/xkxk.frameworks.ui.views/Input/ShortcutBindings.cs` 에 한 줄 더한다 |
| 기능 설명 | `features` 에 줄 추가, 사전에 `sysview.tour.feature.<id>.title`·`.body` 추가 |
| 메뉴 설명 | 고칠 것 없음. 기능 레이어의 메뉴 줄은 `NavMenuCatalog` 에서 만든다 |

- 대상(`target`)은 axaml 의 AutomationId 또는 앵커(`@content`)다. 실행 때 대상이 없거나 숨었거나 조건이 안 맞는 단계는 건너뛰고 번호를 다시 매기며 `tour step skipped ... reason=` 로그를 남긴다.
- 새 메뉴·버튼·창 메뉴 항목은 단계·기능의 `covers`, `NavMenuCatalog`, `coverage.exclude`(사유 필수) 중 한 곳에 있어야 검사를 통과한다.
- 빌드 전에 `python3 -I tools/check_tour_catalog.py` 를 돌린다. 규칙 자체 시험은 `--self-test`.
- 내장 리소스라 내용을 바꾸면 빌드가 필요하다. 원격 갱신은 범위 밖이고 `ITourCatalogSource` 를 구현해 `TourCatalog.UseSource` 로 넘기는 지점만 있다. 진단판은 `tour.catalog.load path=` 로 재빌드 없이 파일을 읽어 볼 수 있다. 이때만 `texts`(언어→키→문구)를 함께 넣을 수 있고, 저장소 카탈로그의 `texts` 는 검사기가 막는다.

## 사람 확인 필요
- 실제 마우스 드래그 체감, 실제 한글 IME, macOS 실기기.
