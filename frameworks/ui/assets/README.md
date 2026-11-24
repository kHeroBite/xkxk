<!-- 디자인 자산 공유 폴더의 구조·이름·형식·라이선스·빌드 반영 규칙 안내 -->
# Pd 디자인 자산 공유 폴더

> 작성일 2026-11-01 · 상태 초안 v0.1(P86) · 결정(P86) — 사용자 지시(2026-11-01) · 관련 문서 `plan/04_기술스택/02_소스구조_분할규칙.md` §10

## 1. 무엇을 위한 폴더인가

- 아이콘·로고·이미지·글꼴 같은 디자인 자산을 사용자와 Claude 가 한곳에서 주고받는 공유 폴더다.
- 사용자 원문은 "아이콘 등은 너가 만들거나 내가 제공할수도 있으니 폴더하나를 지정해서 공유하는 방식으로 정하자" 이다.
- 위치는 WSL 경로 `/mnt/c/DATA/Project/Pd/assets/`, Windows 경로 `C:\DATA\Project\Pd\assets\` 이다.
- 사용자는 Windows 탐색기에서 `C:\DATA\Project\Pd\assets\incoming\` 에 파일을 넣기만 하면 된다.

## 2. 폴더 구조와 누가 넣는가

| 폴더 | 용도 | 누가 넣는가 | 규칙 |
|---|---|---|---|
| `incoming/` | 사용자 원본 투입 | 사용자 | **삭제 금지**·덮어쓰기 금지·이름 변경 금지 |
| `icons/` | 아이콘 정리본 | Claude(사용자 확인 후) | incoming·generated 에서 다듬어 옮긴 것 |
| `icons/menu/` | 메뉴 아이콘 `m<메뉴ID>.svg`(메뉴 ID 마다 1개) | 생성기 `plan/_build/menu_icons.py` | 손으로 고치지 않는다. §9 |
| `logos/` | 로고 정리본 | Claude(사용자 확인 후) | 위와 같다. 앱 아이콘 원본·생성물은 §10 |
| `images/` | 일러스트·배경·스크린 이미지 정리본 | Claude(사용자 확인 후) | 위와 같다 |
| `fonts/` | 글꼴 파일 | 사용자 또는 Claude | 재배포 허용 라이선스만 둔다 |
| `generated/icons/` | AI 생성 아이콘 | Claude | pd-69 의 모바일 하단 탭 SVG 6개(`tab-*.svg`)가 이미 있다 |
| `generated/logos/` | AI 생성 로고 시안 | Claude | 확정 전 시안 |
| `generated/images/` | AI 생성 이미지 | Claude | 확정 전 시안 |
| `_LICENSES.csv` | 라이선스 목록(파일명 고정) | 자산을 넣은 쪽 | 자산 추가 때 행 추가 필수 |

- `incoming/` 은 사용자 원본이므로 Claude 를 포함한 누구도 지우거나 덮어쓰지 않는다(삭제 금지). 다듬은 결과는 정리본 폴더에 새 파일로 만든다.
- `generated/` 는 Claude 가 만든 시안 보관소다. 사용자가 채택하면 정리본 폴더로 옮기고, 채택하지 않은 시안은 지워도 된다.
- 빈 폴더에는 git 이 폴더를 유지하도록 `.gitkeep` 을 둔다. 파일이 들어오면 `.gitkeep` 은 지워도 된다.

## 3. 이름 규칙

- 파일명은 kebab-case(소문자·숫자·하이픈)로 쓴다. 예 `tab-home.svg`, `logo-pd-mark.svg`.
- 래스터 파일은 크기 접미를 붙인다. 접미는 `-16`·`-24`·`-32`·`-48`·`-256` 이다. 예 `tab-home-24.png`.
- 상태 접미는 크기 앞에 둔다. 예 `tab-home-active-24.png`.
- 한글·공백·대문자는 정리본 파일명에 쓰지 않는다. `incoming/` 의 사용자 원본 이름은 그대로 둔다.

## 4. 형식

| 대상 | 형식 | 비고 |
|---|---|---|
| 아이콘 | SVG 우선 | 벡터 원본을 정본으로 둔다 |
| 아이콘 래스터 | PNG 16·24·32·48·256 | SVG 에서 생성한다 |
| Windows 앱 아이콘 | `.ico` | 여러 크기를 한 파일에 담는다. 64px 이하 항목은 32bpp DIB, 256px 만 PNG 로 담는다(§10) |
| macOS 앱 아이콘 | `.icns` | 여러 크기를 한 파일에 담는다 |
| 모바일(Android·iOS) 앱 아이콘 | 미확인 | 공식 문서 확인 후 이 표에 적는다 |
| 웹(파비콘·PWA 매니페스트) 아이콘 | 미확인 | 공식 문서 확인 후 이 표에 적는다 |

## 5. 빌드 반영

- 앱·웹 소스 트리에 자산 원본을 직접 두지 않는다. 빌드 때 `assets/` 에서 복사(또는 링크)해 쓴다.
- 구체 방식(복사 스크립트·빌드 단계)은 WBS-M0 작업에서 정한다.
- 소스 구조 규칙은 `plan/04_기술스택/02_소스구조_분할규칙.md` §10 을 따른다.

## 6. 라이선스 기록

- 자산을 넣으면 `_LICENSES.csv` 에 한 행을 추가한다. 행이 없는 자산은 빌드에 쓰지 않는다.
- 열은 `file,source,license,date,author,purpose` 이다. 앞 4열은 기존 형식이고 끝 2열(author·purpose)은 하위 호환으로 덧붙였다.

| 열 | 내용 | 예 |
|---|---|---|
| file | `assets/` 기준 상대 경로 | `generated/icons/tab-menu.svg` |
| source | 출처(사용자 제공·AI 생성·외부 URL) | `AI 생성(odev-l4 c3)` |
| license | 라이선스 이름 | `프로젝트 자체 제작(제한 없음)` |
| date | 추가일(YYYY-MM-DD) | `2026-11-01` |
| author | 만든 사람·도구(모르면 빈 칸) | `사용자` |
| purpose | 쓰는 곳 | `모바일 하단 탭` |

- 외부 자산은 재배포·상업 이용이 허용되는지 라이선스 원문을 확인한 뒤에만 넣는다. 확인하지 못하면 license 에 `미확인` 이라 쓰고 빌드에 쓰지 않는다.
- 값에 쉼표가 있으면 큰따옴표로 감싼다.

## 7. poc/ui 자산과의 관계

- `poc/ui` 안의 기존 자산을 이 폴더로 옮길지, 그대로 참조할지는 pd-69 와 조율할 대상이다. `poc/ui` 는 이번 작업의 경계라 건드리지 않았다.

## 8. RTL 방향성 아이콘

- 근거는 `plan/05_아키텍처/27_Pd_다국어_로캘_설계.md` §5·§16 요청 4 다(2026-11-11 반영). 세부는 후보다.
- RTL 언어(아랍·히브리 문자 등)에서는 방향을 뜻하는 아이콘만 좌우 반전한다. 대상 예는 뒤로·앞으로·이전·다음·들여쓰기·내어쓰기·되돌리기·다시하기·목록 펼침 화살표다.
- 반전하지 않는 아이콘 예는 로고·체크·검색(돋보기)·재생 컨트롤·시계·그래프 축·숫자와 글자가 든 아이콘이다.
- 반전은 화면이 실행 중에 그린다. 반전본 파일을 따로 만들지 않는 것을 기본으로 한다. 반전만으로 뜻이 틀어지는 아이콘(예 글자가 든 아이콘)은 RTL 전용 파일을 따로 둔다.
- RTL 전용 파일 이름은 상태 접미 자리에 `-rtl` 을 붙인다. 예 `indent-rtl-24.png`.
- 반전 대상 목록은 `icons/_RTL_MIRROR.csv`(열 `file,mirror,note`, `mirror` 는 `yes`·`no`·`rtl-file`)에 둔다. 파일 이름과 열은 후보이며, 첫 아이콘 세트를 정리할 때 만든다.

## 9. 메뉴 아이콘(생성물)

- 근거는 REQ-113·`plan/09_화면설계/22_메뉴아이콘_제목탭_즐겨찾기_설계.md` 다. 아이콘은 메뉴 ID(`plan/_build/menu_data.py` MENU_ID, ADR-085) 기준이며 개수는 생성기 출력으로만 센다.
- 정본은 데이터 두 파일이다. 조합(메뉴 ID → 기본 글리프 + 보조 표지)은 `plan/_build/menu_icon_data.py`, 그림(24 격자 단색 도형)은 `plan/_build/icon_glyphs.py` 다.
- 생성기 `plan/_build/menu_icons.py` 가 `icons/menu/m<메뉴ID>.svg`, `poc/ui/src/xkxk.frameworks.ui.views/Themes/MenuIcons.axaml`(키 `PdMenuIcon<메뉴ID>`·`PdShellIcon<이름>`·`PdIcon<이름>`), `poc/ui/src/xkxk.frameworks.ui.data/Navigation/MenuIconCatalog.Generated.cs`(화면 ID → 메뉴 ID·아이콘 키), `_LICENSES.csv` 의 `icons/menu/` 행을 만든다.
- 메뉴를 더하거나 아이콘을 바꿀 때는 데이터 파일을 고치고 `python3 -B plan/_build/menu_icons.py` 를 다시 돌린다. 생성물을 손으로 고치지 않는다.
- 검사는 `--check`(누락·중복 조합·없는 이름·폐기 번호·경로 문법·생성물 최신 여부), `--self-test`(검사기 결함 주입·차집합·결정성), 품질 확인은 `--sheet <png>`(16·24·32px × 밝음·어두움 접촉 시트, 세션 임시 폴더에 둔다)다.
- 라이선스: 외부 아이콘 세트(Material·Lucide·Font Awesome 등)의 path 를 복사하거나 기억으로 재현하지 않고 사각·원·다각형·선을 조합해 새로 그린다. `_LICENSES.csv` source 열에 생성 도구·모델·날짜를 적는다.

## 10. 앱 아이콘(작업 표시줄·창)

- 근거는 REQ-115 다. 로고 원본은 `logos/logo-pd-mark.svg`(기존 64px 로고 모양을 따라 새로 그림)이고, 생성 도구는 `tools/make_icons.py` 다.
- 생성물은 `logos/pd-app.ico`(16·20·24·32·40·48·64 = 32bpp DIB, 256 = PNG)와 `logos/pd-app-<16|24|32|48|64|128|256>.png` 다.
- 같은 실행이 빌드 반영본 `poc/ui/src/xkxk.frameworks.ui.desktop/Assets/pd.ico`(csproj `ApplicationIcon`)와 `poc/ui/src/xkxk.frameworks.ui.views/Assets/Logo/pd-logo-64.png`(제목줄 로고)를 교체한다. 로고를 바꿀 때는 SVG 를 고치고 `python3 -B tools/make_icons.py` 를 다시 돌린다.
- 도구는 SVG 의 `pd-frame`(rx)·`pd-left`(경로 M·H·V·L·C·Z)·두 그라데이션 id 만 읽는다. 이 id·속성 이름을 바꾸면 도구도 함께 고친다.
- 검사는 `--check`(원본에서 다시 만든 결과와 저장소 파일 일치, ico 크기별 형식), `--self-test`(ico 왕복·작은 PNG 항목 결함 주입)다.

## 관련 문서

- `/mnt/c/DATA/Project/Pd/plan/04_기술스택/02_소스구조_분할규칙.md` §10 디자인 자산 공유 폴더 — 결정(P86).

## pd-69 요청(P86)

- docs-map 에 이 README 행을 등재한다(요청서 §7.1).
- `poc/ui` 자산의 이전·참조 여부를 정한다.
