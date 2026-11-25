<!-- ui 확정된 결정을 ADR 형식으로 누적하는 결정 기록 정본 -->
# 결정기록 (ADR) — ui

- 상태: 작성 중
- 최종 갱신일: 2026-11-25
- 카탈로그 키: `decisions`

## 작성 규칙
- 결정 1건 = ADR 1개다. 번호는 `ADR-NNN` 으로 늘려 가며 재사용하지 않는다.
- 제목 줄 형식은 `## ADR-NNN 제목 (관련 D-NNN, REQ-NN)` 이다. 관련 항목이 없으면 괄호를 생략한다.
- 각 ADR 은 아래 항목을 모두 적는다.
  - 일자 — 결정한 날(YYYY-MM-DD).
  - 결정 — 무엇을 하기로 했는가(한두 문장).
  - 근거 — 왜 그렇게 정했는가. 실측·조사 결과는 출처나 측정 명령을 붙인다.
  - 대안 — 검토했으나 택하지 않은 안과 버린 이유.
  - 영향 — 바뀌는 문서·코드·절차.
  - 상태 — 채택 · 대체됨→ADR-NNN · 철회(사유).
- 확정된 ADR 본문은 고치지 않는다. 결정이 바뀌면 새 ADR 을 쓰고 옛 ADR 의 상태만 "대체됨→ADR-NNN" 으로 바꾼다.
- 결정대기(`01_결정대기_질문목록.md`)에서 넘어온 결정이면 그 행의 상태를 "결정됨→ADR-NNN" 으로 함께 바꾼다.
- 이 파일이 300줄을 넘으면 오래된 ADR 을 `02_결정기록_ADR_부록/b1_*.md`, `b2_*.md` … 로 옮기고, 본문에는 아래 색인표만 남긴다.

## 부록 색인

| 부록 | 수록 ADR | 비고 |
|---|---|---|
| (없음) | — | 300줄 초과 시 b1 부터 추가 |

## 결정 목록

## ADR-000 산출물 정본 체계 채택
- 일자: 2026-11-24
- 결정: 요구·설계·계획·결정·시험 산출물을 `plan/` 아래 번호 폴더에 두고, `plan/index.json` 을 단일 진입 색인으로 삼는다.
- 근거: 단계마다 남긴 산출물을 다음 단계와 다음 사이클이 같은 경로로 찾아 참조·갱신해야 하기 때문이다.
- 대안: 문서를 저장소 곳곳에 흩어 두는 안은 찾는 경로가 사람마다 달라 기각했다. 세션 보관소를 정본으로 쓰는 안은 커밋되지 않아 기각했다.
- 영향: `plan/README.md`, `plan/index.json`, 이후 모든 plan 문서.
- 상태: 채택

## UADR-0001 XKXK 모노레포 구조와 별별UI 정본 영역 채택
- 일자: 2026-11-24
- 결정: `/mnt/c/DATA/Project/xkxk` 를 git 모노레포 루트로 두고, `frameworks/ui` 를 별별UI 세션 홈이자 하네스 정본 영역(`plan/`·`src/`·`test/`·`tools/`·`assets/`·`vendor/`·`ref/`·`HISTORY.md`·`LESSONS.md`)으로 삼는다. GPT 제안 구조 중 하네스가 이미 맡는 부분(`docs/work/`, 영역 결정·아키텍처 상세)은 만들지 않고, 루트 `docs/` 는 제품군 지도·교차 결정 자리·포인터만 둔다. 루트 `CLAUDE.md`·`AGENTS.md` 는 Tier1 공용 symlink 로 두고, 영역 `AGENTS.md` 는 실파일, 영역 `CLAUDE.md` 는 `@AGENTS.md` 한 줄 실파일로 둔다.
- 근거: 사용자 세 번째 요구("우리의 현재 하네스가 만든 구조(plan dev test 등)도 같이 고려해서 구성하라")에 따랐다. 하네스의 정본 루트 해석기(`canon_root.py`)는 `.claude/` 를 가진 첫 폴더를 영역 루트로 보고, 문서 게이트·ofinish 보관은 git toplevel 기준이라 루트 `.claude/docs_gate.json` 으로 영역 경로를 가리키면 둘이 맞물린다. 상세 대조표는 `05_아키텍처/01_XKXK_저장소구조.md` §2·§3 에 있다. GPT 원문은 `/mnt/c/DATA/Project/xkxk/docs/architecture/source/gpt_XKXK_구조대화_원문_2026-11-24.txt` 2828~2975행·3790~3860행이다.
- 대안: GPT 제안 그대로(루트 `AGENTS.md` 실내용, `docs/decisions`·`docs/work` 에 결정·작업 기록)는 Tier1 공용 지침 분리 원칙과 충돌하고 하네스 세션 기록·plan 결정관리와 중복돼 기각했다. `frameworks/ui` 를 독립 git 저장소로 두는 안은 사용자가 루트 모노레포를 골라 기각했다. Pd 저장소 안에 계속 두는 안은 사용자 요구(새 홈에서 진행)와 맞지 않아 기각했다.
- 영향: `/mnt/c/DATA/Project/xkxk/README.md`, 루트·영역 `.claude/rules/docs-map.md`·`docs_gate.json`, `plan/README.md`, `plan/index.json`, `05_아키텍처/01_XKXK_저장소구조.md`, `90_결정관리/이전기록_2026-11-24.md`. 다중 영역이 생기면 루트 docs_gate 가 한 영역만 가리키는 한계가 남는다(하네스 수정은 AI 프로젝트 세션 소관).
- 상태: 채택

## UADR-0002 프로젝트·어셈블리 이름은 저장소 폴더 경로 기준으로 짓는다
- 일자: 2026-11-24
- 결정: 프로젝트·라이브러리·어셈블리 이름을 저장소 폴더 경로를 점으로 이은 소문자 이름(`xkxk.frameworks.ui`, 층별 `.views`·`.shell`·`.data`·`.desktop`·`.browser`·`.android`·`.fonts`)으로 한다. 하위 네임스페이스는 하위 폴더 이름(PascalCase) 그대로 둔다. 이름 대응은 `Pd.Ui`→`xkxk.frameworks.ui`, `Pd.Ui.Views`→`.views`, `Pd.Poc.Shell`→`.shell`, `Pd.Client.Data`→`.data`, `Pd.Poc.Desktop`→`.desktop`, `Pd.Poc.Browser`→`.browser`, `Pd.Poc.Android`·`Pd.Poc.Droid`→`.android`, `Pd.Fonts`→`.fonts` 이다.
- 근거: 사용자 원문 "프로젝트 라이브러리는 폴더기준으로 잡자. 예를들면 xkxk.frameworks.ui 같은거다". 이름과 폴더 경로가 기계적으로 대응하고, 실행 파일 이름이 사용자 Pd 앱과 달라져 프로세스 이름 겹침이 사라진다.
- 대안: PascalCase(`Xkxk.Frameworks.Ui.*`)는 사용자 예시와 달라 기각했다. 폴더는 그대로 두고 어셈블리만 바꾸는 안은 폴더와 이름이 어긋나 기각했다. 하위 네임스페이스까지 소문자로 하는 안은 하위 폴더 수백 곳 이름 변경이 필요해 기각했다.
- 영향: `src/` 전체, 도구·시험 스크립트, `AGENTS.md`, `README.md`, `plan/04_기술스택/02_소스구조_분할규칙.md`. Android 패키지 `com.pd.poc`(UTODO-0013)와 `PdPoc` 데이터 폴더·단일 인스턴스 이름(UTODO-0014)은 이번 결정 범위 밖이었으나 UADR-0003 에서 같은 규칙으로 전환했다.
- 상태: 채택

## UADR-0003 데이터 폴더·단일 인스턴스·Android 패키지도 폴더 경로 기준으로 옛 Pd 앱과 분리한다
- 일자: 2026-11-25
- 결정: 사용자 데이터 폴더를 `%LOCALAPPDATA%\xkxk\frameworks\ui`, 단일 인스턴스 이름을 `xkxk.frameworks.ui.<채널>.Instance`, Android 패키지를 `xkxk.frameworks.ui`(APK `xkxk.frameworks.ui-Signed.apk`)로 한다. 옛 `%LOCALAPPDATA%\PdPoc` 를 읽는 이전·폴백 코드는 두지 않는다. 브라우저 localStorage 키는 바꾸지 않는다.
- 근거: 사용자 원문 "기존 폴더와의 의존성을 모두 끊어야 한다"·"xkxk/frameworks/ui 처럼 폴더를 동일하게 맞춰라"·"옛앱은 이제 필요없으니 모두 지워도 된다. 신규앱만 남겨라. %LOCALAPPDATA%\PdPoc 도 지워라".
- 대안: 옛 폴더를 읽어 설정을 옮기는 이전 코드는 사용자가 옛 앱을 버리기로 해 기각했다. Android 패키지를 그대로 두는 안(UTODO-0013 당시 유지)은 옛 앱과 공존·충돌 위험이 남아 기각했다.
- 영향: `src/xkxk.frameworks.ui.data/AppDataFolder.cs`(신설)·데스크톱·Android 호스트, `tools/`·`test/` 스크립트, `AGENTS.md`, `README.md`. UTODO-0013·0014 완료. 이전하지 않으므로 사용자 옛 설정은 새 앱에 이어지지 않는다.
- 상태: 채택

## UADR-0004 메인 창 Ctrl+휠은 시스템 글자 크기를 바꾼다 (REQ-119)
- 일자: 2026-11-25
- 결정: 메인 창 크롬(문서 래퍼 밖 — 메뉴 줄·제목 줄 탭·탐색기 등 패널·상태바·MDI 문서 창 머리) 위의 Ctrl+휠(macOS Cmd+휠)은 시스템 글자 크기 `UiSettings.ShellFontSize`(설정 키 `ui.font.shell_size`, 범위 10~24, 기본 14)를 1씩 바꾸고 저장한다. 문서 창 안 Ctrl+휠(문서 글자·창 배율)과 Ctrl+0 동작은 바꾸지 않는다. 판정은 휠이 닿은 곳(히트 영역) 기준이다.
- 근거: 사용자 원문 "MDI/SDI에서는 ctrl+휠로 확대/축소가 잘된다. 메인창에서도 ctrl+휠로 메인창 전체의 글자 폰트를 확대/축소 하여야 한다"(2026-11-25, RAW-002 4). 새 설정 키를 만들지 않고 기존 시스템 글자 크기를 쓰면 설정 화면 값과 항상 일치하고 재기동 뒤에도 유지된다.
- 대체 대상: 2026-10-28 21:40 사용자 지시 "+ - 버튼은 없애고 ctrl+휠로만 … 다만 메인창은 조절불가"(출처 `Pd:plan/00_요구사항/01_사용자요구사항_원문.md` REQ-49 행, 이 영역 인용 `../04_기술스택/04_판정보고서_부록/a7_실측환경_데스크톱결과_2_3.2.md` UI-02·UI-05·UI-06, `../04_기술스택/04_판정보고서_부록/a13_화면공통원칙_구현대응_기록.md` §4.5) 중 "메인창은 조절불가" 부분이다. 메인 크롬 글자 크기 고정 규칙은 2026-11-11 시스템 글자 설정으로 이미 일부 대체됐고(같은 a13 §4.5), 이번 결정으로 Ctrl+휠 입력까지 대체한다. "+ - 버튼 없음"은 유지한다. 이 파일에는 옛 지시의 ADR 이 없어 상태를 바꿀 대상이 없다.
- 대안: 메인 크롬 전용 배율 설정을 새로 두는 안은 시스템 글자 크기와 값이 둘로 갈라져 기각했다. 메인 창 전체(문서 포함)를 한 배율로 키우는 안은 기존 문서 창 배율 규칙과 충돌해 기각했다.
- 영향: `src/xkxk.frameworks.ui.shell/Input/SettingsInput.cs`(메인 크롬 단계), `../09_화면설계/01_화면설계_공통원칙.md` §5.2·§5.3, 원격 진단 `input.wheel`·`input.ctrlWheel` 의 메인 대상 응답.
- 상태: 채택

## UADR-0005 설정 창은 앱 모달로 연다 (REQ-118)
- 일자: 2026-11-25
- 결정: 설정 창이 열려 있는 동안 메인 창·MDI 문서·SDI 창·다른 시스템 창의 입력을 모두 막는다. 데스크톱은 `Window.ShowDialog(owner)` 와 앱 공용 범위 `ModalScope`(OS 수준 차단은 데스크톱 훅 — Windows 는 `EnableWindow`, 앱 수준은 TopLevel 터널 입력 관문)를 함께 쓴다. 설정 창 자신과 그 소유 사슬 아래 창(테마 편집기 등)·팝업은 제외한다. 웹은 플라이아웃의 바깥 누름 닫기를 끄고 같은 앱 관문으로 막으며 Esc·닫기 단추로만 닫는다.
- 근거: 사용자 원문 "설정창을 띄우면 설정창을 닫기 전까지 메인창/MDI/SDI 모두 컨트롤/클릭이 불가하여야 한다"(2026-11-25, RAW-002 3). Avalonia `ShowDialog(owner)` 는 owner 하나만 비활성화하고 다른 top-level 창(SDI)은 막지 않는다(https://raw.githubusercontent.com/AvaloniaUI/Avalonia/master/src/Avalonia.Controls/Window.cs , https://raw.githubusercontent.com/AvaloniaUI/Avalonia/master/src/Windows/Avalonia.Win32/WindowImpl.cs — 사이클27 계획서 §3 조사, SDI 를 막는 순회 코드가 없다는 판단은 추정).
- 대안: `ShowDialog` 만 쓰는 안은 SDI 가 막히지 않아 요구를 채우지 못해 기각했다. 앱 관문만 쓰는 안은 OS 제목줄·캡션 단추·Alt+F4 를 막지 못해 기각했다.
- 영향: `src/xkxk.frameworks.ui.shell/Platform/SystemDialogHost.cs`·`ModalScope.cs`·`ModalScope.Gate.cs`, `src/xkxk.frameworks.ui.desktop/Win32ModalBlock.cs`, 설정 열기 호출, `../09_화면설계/03_데스크톱_화면설계_2.md`·`04_웹_화면설계.md`, 진단 명령 `modal.state`. Windows 외 데스크톱은 앱 관문만 적용되는 한계가 있다.
- 상태: 채택

## UADR-0006 명령 아이콘 키 체계를 두고 메뉴 아이콘과 창 아이콘을 같은 키로 맞춘다 (REQ-117)
- 일자: 2026-11-25
- 결정: 상단 메뉴(파일·창·보기·시스템)의 각 항목에 16px 단색 선 아이콘을 단다. 키 체계는 메뉴 ID 화면 `PdMenuIcon<ID>`(기존), 셸 창 `PdShellIcon<이름>`(기존), 명령 `PdCmdIcon<이름>`(신설) 셋이며, 명령 키의 단일 출처는 생성 상수 `xkxk.frameworks.ui.data.Navigation.CmdIconKeys` 다. 메뉴로 연 창(MDI·SDI 문서, 설정·정보 등 시스템 창, SDI OS 창)의 창 아이콘은 그 메뉴 항목과 같은 키를 쓴다. 메인 창은 앱 아이콘을 유지한다. 최상위 메뉴 머리글에는 아이콘을 넣지 않는다.
- 근거: 사용자 원문 "이 상단 메뉴들을 … 이렇게 메뉴마다 아이콘을 만들어 달라. 그래서 해당 메뉴를 눌러서 창이 뜰때에는 그 아이콘이 창아이콘으로 보여져야 한다"(2026-11-25, RAW-002 2). 메뉴와 창이 같은 상수를 쓰면 키가 어긋날 수 없다. 아이콘 그림은 외부 아이콘 path 를 복사하지 않고 생성기로 새로 그린다(`../09_화면설계/22_메뉴아이콘_제목탭_즐겨찾기_설계.md` §2.2).
- 대안: 메뉴마다 아이콘 블록을 직접 적는 안은 메뉴 파일이 300줄을 넘고 키 오타를 막지 못해 기각했다. 외부 아이콘 글꼴 도입은 22 §2.2 와 라이선스 부담으로 기각했다.
- 영향: `plan/_build/menu_icons.py`·`menu_icon_data.py`·`icon_glyphs_cmd.py`, `src/xkxk.frameworks.ui.views/Themes/MenuIcons.axaml`·`MenuIconStyles.axaml`·`Menus/*`, `src/xkxk.frameworks.ui.data/Navigation/CmdIconKeys.Generated.cs`·`MenuIconCatalog.cs`, `src/xkxk.frameworks.ui.shell/Platform/MenuIconBitmap.cs`·`SystemDialogHost.cs`, `src/xkxk.frameworks.ui.desktop/SdiDocumentIcon.cs`, `../09_화면설계/22_메뉴아이콘_제목탭_즐겨찾기_설계.md`. 프레임워크화(REQ-120)에서는 이 키 체계가 솔루션 아이콘 등록의 기반이 된다.
- 상태: 채택
