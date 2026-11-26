<!-- ui 확정된 결정을 ADR 형식으로 누적하는 결정 기록 정본 -->
# 결정기록 (ADR) — ui

- 상태: 작성 중
- 최종 갱신일: 2026-11-26
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

## UADR-0007 별별UI 프레임워크화를 전체안으로 진행하고 F0 결정 13항을 확정한다 (REQ-120)
- 일자: 2026-11-26
- 결정: 프레임워크화(REQ-120)를 구현 착수 상태로 바꾸고 전체안(층별 NuGet 패키지 + 버전 고정, 저장소 안은 ProjectReference)을 채택한다. F0 확정 13항은 다음과 같다. ① 진행. ② 전체안 채택. ③ 패키지 경계는 층별이고 호스트 공통 코드는 `hosting.*` 로 나눈다. ④ 피드는 서버 GitLab 패키지 레지스트리이고 사용 가능 여부는 F5 에서 실측한다. ⑤ 부·패치 버전은 CI 통과 시 자동 병합하고 주 버전은 수동 병합한다. ⑥ F7(선택 계층)은 1.0 뒤에 포함한다. ⑦ DI 는 자체 등록부로 하고 F5 의 AOT 실측 뒤 MS.DI 를 재검토한다. ⑧ 차트·엑셀·벤치는 기본 패키지에서 분리해 시험대 모듈로 옮긴다(F3). ⑨ 성능은 기준판(커밋 25c31b6) 대비 중앙값 +5% 이내이고 기존 절대 기준을 유지한다. ⑩ 첫 소비 솔루션은 시험대 앱과 저장소 밖 솔루션 ohoops 이다. ⑪ 공개 API 이름은 `xkxk.frameworks.ui.*` 를 유지한다(UADR-0002). ⑫ 패치판 Avalonia.Browser 는 사내 피드에 함께 배포한다. ⑬ 지원 버전 정책은 최신 부 버전과 직전 부 버전에만 패치를 낸다. 로드맵 순서는 원래대로 유지한다(F5 패키지 분리는 F4 뒤). 같은 날 추가로 확정한 세 항목은 다음과 같다. Q1 은 정본 원격을 GitLab 으로 옮기고 GitHub 는 미러로 둔다. Q2 는 CI 를 에이전트 실행 머신의 로컬 Windows 에서 GitLab Runner(shell executor, 동시 1)로 돌리고 성능·실입력 시험은 CI 에서 제외하며, 머신 이전 때는 러너만 다시 등록한다. Q3 은 F3 에서 `hosting.*` 를 소스로 분리하는 것이다(패키지화는 F5 그대로). Q4(1.0 게시)와 Q5(ohoops 협업 방식)는 B21 때 묻는다.
- 근거: 사용자 원문 "ohoops에서 ui개발은 홀딩하고 있겠다. 개발이 가능한 시점이 되면 나에게 알려달라 … (F0 부터 F8까지)하자"(2026-11-26, RAW-003). 저장소 밖 솔루션 ohoops 도 이 프레임워크로 개발하고 제품마다 출시 일정이 따로이므로, 제품이 버전을 고정해 받는 패키지 배포가 필요하다. 계획 상세는 `../05_아키텍처/02_별별UI_프레임워크화_계획.md`, 단계별 batch 와 질문은 `../05_아키텍처/02_별별UI_프레임워크화_계획_부록/a4_단계별_배치계획.md` 에 있다. 에이전트 실행 머신은 Windows(WSL) 환경이고 이전 대상 머신도 같은 환경이라는 사용자 설명(RAW-003)에 따라 로컬 러너로 정했다.
- 대안: 패키지 없이 저장소 안 ProjectReference 만 쓰는 안은 저장소 밖 제품이 버전을 고정할 수 없어 기각했다. 서버 GitLab Runner(Linux)만 쓰는 안은 데스크톱·Android 빌드에 필요한 SDK 가 없어 기각했다(러너 존재 여부는 확인 필요). 정본을 GitHub 에 두고 패키지만 GitLab 에 두는 안은 CI·패키지·저장소가 두 곳으로 갈라져 기각했다.
- 영향: `../05_아키텍처/02_별별UI_프레임워크화_계획.md` §6·§8·§10·§11, `../05_아키텍처/02_별별UI_프레임워크화_계획_부록/a3_시험대_검증계획.md`·`a4_단계별_배치계획.md`(신설), `03_향후과제_TODO.md`(UTODO-0008 재개, UTODO-0024·0025 신설), `../00_요구사항/02_요구사항정의서.md`(REQ-120 상태). UTODO-0008 의 "지금은 하지 않음"(사후 질문 Q10) 결정은 이 결정으로 대체됐다. 저장소 원격 이전(GitHub → GitLab 정본)과 러너 등록은 INFRA.md 에 값(주소·토큰)이 필요한 사용자 정보이며 F5(B18) 이전에 확인한다.
- 상태: 채택

## UADR-0008 프레임워크와 분리된 시험대(testbed)를 두고 검증 체계를 세운다 (REQ-121)
- 일자: 2026-11-26
- 결정: 프레임워크(`src/`)와 폴더로 분리된 시험대를 영역 안 `frameworks/ui/testbed/`(이름 `xkxk.frameworks.ui.testbed.*`)에 둔다. 시험대는 패키지 대상이 아니고(IsPackable=false) 프레임워크 공개 API 만 쓰며 `InternalsVisibleTo` 를 받지 않는다. 직접 설정 수단은 세 가지다. 앱 안 설정 패널, 시나리오 JSON(해석기는 시험대 모듈 안에만 둔다), 원격 진단 명령(`scenario.apply`·`scenario.dump`·`registry.dump`)이다. 검증 스크립트는 `test/framework/` 에 두고 일괄 실행기 `verify_framework.sh` 로 묶는다. 검증 항목은 계약 시험, 레이어·내부 참조 검사, 기존 판정기 전수, 시나리오 판정, 공개 API 고정, 패키지 검증·소비 시험, AOT·트리밍 경고 0, 3호스트 스모크(Fold5 는 접지 않음), 성능 회귀 +5% 이다. 성능 기준선은 PoC 커밋 25c31b6 빌드이며 PC 비움 때 실제 ms 로 판정한다. 최소 소비자 앱(`testbed.consumer`)은 PackageReference 만으로 피드에서 restore·빌드한다.
- 근거: 사용자 원문 "프레임워크를 개발하고 테스트하기 위한 테스트용 프로젝트가 필요할것 같다. api 이나 메뉴등을 직접 설정해서 테스트가 가능하여야 한다 … 프레임워크와 테스트용프로젝트는 분리되어야 한다"(2026-11-26, RAW-003). 같은 영역 안에 두면 하네스 test_root·tools_root·빌드 스크립트를 그대로 쓸 수 있다. 상세는 `../05_아키텍처/02_별별UI_프레임워크화_계획_부록/a3_시험대_검증계획.md` 에 있다.
- 대안: 저장소 루트 `apps/` 는 제품 앱 자리(`../05_아키텍처/01_XKXK_저장소구조.md` §4)라 시험대와 맞지 않아 기각했다. 계획서 원안 `samples/` 이름은 예제로 읽혀 시험·검증 목적이 흐려져 기각했다. 시험대를 별도 저장소로 두는 안은 하네스 재사용에 불리해 기각했다. xUnit 같은 새 시험 프레임워크 도입은 기존 콘솔 실행형(rc 판정)으로 충분해 기각했다.
- 영향: `testbed/`·`test/framework/` 신설, `tools/check_layers_rules.json` 에 testbed 층 규칙 추가, 호스트 실행 파일·빌드 스크립트·판정기의 대상 정체성 갱신(B11), `test/README.md` 색인 등재, `../05_아키텍처/01_XKXK_저장소구조.md`·`../04_기술스택/02_소스구조_분할규칙.md`(층 정의, B10). 시험대 앱 정체성(데이터 폴더·단일 인스턴스·Android 패키지의 `.testbed` 접미)은 UADR-0002·0003 의 폴더 경로 규칙을 그대로 적용한다.
- 상태: 채택

## UADR-0009 공개 API 는 확장 자리 선개방·기본값 호환 8원칙을 따르고 깨짐 검출을 B02 부터 시작한다 (REQ-122)
- 일자: 2026-11-26
- 결정: 공개 API 는 다음 8원칙을 따른다. ① 확장 입력은 옵션 객체(속성 + 기본값)로 받는다. ② 기본값 선택 파라미터를 공개 API 에 두지 않고 새 입력은 옵션 속성 또는 오버로드로만 더한다. ③ 확장점(서비스 등록부·옵션 속성 모음·모듈 가상 멤버)을 미리 연다. ④ 소비자가 구현하는 계약은 추상 기반 클래스로 받고 같은 이름의 인터페이스는 동결한다(프레임워크만 구현하는 인터페이스는 멤버 추가 허용). ⑤ 식별자는 문자열 ID 상수로 두고 번호를 재사용하지 않는다. ⑥ 저장 형식에는 버전을 둔다. ⑦ 깨짐은 자동으로 검출한다. ⑧ 폐기는 `[Obsolete]` 로 한 부 버전 이상 예고하고 주 버전에서만 제거한다. PublicAPI 분석기 도입은 F5 에서 B02 로 앞당긴다. 패키지 검증 baseline 비교는 이전 게시판 nupkg 가 있어야 하므로 B17 첫 pack 부터다. 정본 표준 문서는 `/mnt/c/DATA/Project/xkxk/docs/standards/02_공개API_호환원칙.md` 다.
- 근거: 사용자 원문 "표준api를 만들때 나중에 최대한 변경이 없어도 될 정도로 완벽하고 모든 경우의수에 대해 미리 확장을 한 개념으로 만들어달라. 그리고 확장하더라도 신규기능이나 파라미터는 기본값을 지정해서 이전 api와의 호환성도 100%확보하는 방향으로 구현하라."(2026-11-26, RAW-004). Microsoft 문서 원문 "changing a method's signature by adding a new parameter will cause assemblies compiled against the older version of the library to throw a MissingMethodException.", "Adding anything to an interface will cause existing types that implement it to fail.", "CONSIDER using abstract base classes instead of interfaces."(https://learn.microsoft.com/en-us/dotnet/standard/library-guidance/breaking-changes). PublicApiAnalyzers 규칙 RS0016·RS0017·RS0026·RS0027·RS0036·RS0037(https://raw.githubusercontent.com/dotnet/roslyn-analyzers/main/src/PublicApiAnalyzers/Microsoft.CodeAnalysis.PublicApiAnalyzers.md). RS0026 은 선택 파라미터를 가진 공개 오버로드가 여럿인 경우, RS0027 은 선택 파라미터 API 가 오버로드 중 파라미터가 가장 많아야 하는 경우만 막으므로 선택 파라미터 0건의 강제 수단은 계약 시험의 리플렉션 `IsOptional` 검사다.
- 대안: 기본값 파라미터를 허용하는 안은 호출부에 기본값이 컴파일되어 들어가고 시그니처가 바뀌면 이전 어셈블리가 `MissingMethodException` 을 내므로 기각했다. 자체 스크립트만으로 검출하는 안은 PublicAPI 분석기를 쓸 수 있어 보조 수단(`api_surface_check.py`)으로만 쓴다. 쓰이지 않을 기능까지 미리 구현하는 안은 미사용 API 도 바꾸기 어려운 고정 부담이 되어 기각했다.
- 영향: `/mnt/c/DATA/Project/xkxk/docs/standards/02_공개API_호환원칙.md`(신설), `../05_아키텍처/02_별별UI_프레임워크화_계획.md` §4.2·§6, 부록 `a3_시험대_검증계획.md`·`a4_단계별_배치계획.md`, `src/xkxk.frameworks.ui.shell/` 의 `Framework/`·`PublicAPI.*.txt`·`.editorconfig`·csproj(RS0017·RS0037 WarningsAsErrors).
- 한계: "모든 경우의 수" 는 확장 자리를 미리 만든다는 뜻이며 기능을 미리 다 만드는 것이 아니다. 검출 한계도 있다. B02~B20 은 계약이 모두 Unshipped 라서 삭제·변경은 PublicAPI txt 수정과 명시 허용 플래그로 드러나는 수준이고, base=HEAD 비교는 커밋 전 작업 트리 변경만 잡는다. Shipped baseline 기준 차단은 B21 에서 Shipped 로 확정한 뒤, 패키지 바이너리 비교는 B17 첫 pack 뒤에 성립한다. 분석기 저장소(roslyn-analyzers)가 2026-11-12 보관된 사실이 확인되어 후속 위치는 확인 필요다.
- 상태: 채택
