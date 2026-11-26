# AGENTS.md — 별별UI(frameworks/ui) 영역 규칙

> 이 파일은 XKXK 모노레포 `frameworks/ui` 영역의 작업 규칙이다. 범용 규칙(Tier1)은 저장소 루트 `CLAUDE.md`·`AGENTS.md`(공용 하네스 링크)에 있고, 이 파일은 그 위에 영역 고유 규칙만 더한다.
> 이 영역의 `CLAUDE.md` 는 `@AGENTS.md` 한 줄로 이 파일을 불러온다.

## 1. 책임
- Pd 제품군 공용 UI 프레임워크(별별UI)의 소스·시험·도구·디자인 자산·UI 정본 문서를 맡는다.
- 2026-11-24 Pd 저장소의 UI PoC(`poc/ui/`, 기준 커밋 157403c)와 UI 관련 정본 문서를 원본 무변경으로 옮겨 시작했다. 경위는 `plan/90_결정관리/이전기록_2026-11-24.md` 에 있다.
- Pd 제품 자체(업무 기능·서버·DB)는 Pd 저장소에 남는다. 이 영역은 UI 층만 다룬다.

## 2. 수정 범위
- 세션은 이 폴더(`/mnt/c/DATA/Project/xkxk/frameworks/ui`)에서 시작한다. 작업 대상은 이 영역 아래 파일이다.
- 저장소 루트(`docs/`·`modules/`·`apps/`·`libraries/`·`tooling/`·루트 README·.gitignore)는 여러 영역이 함께 쓰므로, 고칠 때는 다른 영역 영향을 먼저 확인한다.
- Pd 저장소(`/mnt/c/DATA/Project/Pd`)는 읽기만 한다. Pd 문서를 인용할 때는 `Pd:<저장소 상대경로>` 로 적는다.
- `INFRA.md` 는 커밋하지 않는다(루트 `.gitignore`·`.git/info/exclude`).

## 3. 읽는 순서
1. `.claude/rules/docs-map.md` — 이 영역의 문서 지도(세션 시작 때 자동 로딩).
2. `plan/README.md` → `plan/index.json` → 작업에 해당하는 정본 문서(`plan/09_화면설계/`·`plan/04_기술스택/` 등).
3. `README.md`(PoC 구성·빌드)와 `LAYERS.md`(소스 계층 분류표).
4. 빌드·실행·시험이 필요하면 `INFRA.md` 의 해당 절만 읽는다.
5. 시험 자산은 `test/README.md`, 개발 도구는 `tools/README.md` 색인에서 고른다.

## 4. 빌드
- WSL 에서 Windows `dotnet.exe` 를 호출한다. 명령은 영역 루트 기준이다.
  - 데스크톱: `bash tools/build_desktop.sh --bg [Release|R2R]`
  - 브라우저: `bash tools/build_browser.sh --bg [Release]`
  - Android: `bash tools/build_android.sh --bg [Debug|Release]`
- `--bg` 는 `logs/build_*_<시각>.log` 와 같은 이름 `.rc` 를 남긴다. 성공은 `.rc`=0 이고 로그에 `오류 0개` 또는 `0 Error(s)` 가 있는 것이다.
- 빌드는 한 번에 한 명만 돌린다(obj 잠금). 실행·포트·프로세스 규칙은 `INFRA.md` 4절을 따른다. 실행 파일 이름은 `xkxk.frameworks.ui.desktop.exe` 이고, 데이터 폴더(`%LOCALAPPDATA%\xkxk\frameworks\ui`)·단일 인스턴스 이름(`xkxk.frameworks.ui.<채널>.Instance`)도 옛 Pd 앱과 분리됐다. 같은 이름의 다른 프로세스는 사용자가 띄웠을 수 있으므로 자기 PID 외 프로세스는 건드리지 않는다.

## 5. 레이어 원칙
- 저장소 공통 원칙은 `/mnt/c/DATA/Project/xkxk/docs/standards/01_소스구조_공통원칙.md` 이고, 아래는 별별UI 영역 고유 규칙이다.
- UI/UX(화면 조립)·컨트롤·데이터 층을 철저히 나눈다. 시험·PoC 코드도 같은 규칙을 따른다.

| 층 | 참조할 수 있는 것 | 참조하면 안 되는 것 |
|---|---|---|
| 호스트(Desktop·Browser·Android) | 아래 전부 | — |
| `xkxk.frameworks.ui.views`(화면 조립) | `xkxk.frameworks.ui.shell`, `xkxk.frameworks.ui`, `xkxk.frameworks.ui.data` | 호스트 |
| `xkxk.frameworks.ui.shell`(컨트롤) | `xkxk.frameworks.ui`, `xkxk.frameworks.ui.data` | `xkxk.frameworks.ui.views`, 호스트 |
| `xkxk.frameworks.ui`(순수 UI) | Avalonia 패키지만 | 그 밖 전부, Dock |
| `xkxk.frameworks.ui.data`(데이터) | 비 UI 패키지 | Avalonia, 나머지 층 |

- 층 정의의 정본은 `plan/04_기술스택/02_소스구조_분할규칙.md` 다.
- 검사는 `python3 -B tools/check_layers.py`(규칙 `tools/check_layers_rules.json`, 자체 확인 `--self-test`)와 `python3 -B tools/classify_layers.py --check`(LAYERS.md 대조)다.
- 층 파일은 300줄 이하에 첫 줄 한국어 헤더 주석을 둔다.

## 6. 알려진 한계
- 프로젝트·어셈블리 이름은 폴더 경로 기준 `xkxk.frameworks.ui.*` 로 전환했다(UTODO-0007, UADR-0002). Android 패키지(`xkxk.frameworks.ui`)·데이터 폴더(`%LOCALAPPDATA%\xkxk\frameworks\ui`)·단일 인스턴스 이름(`xkxk.frameworks.ui.<채널>.Instance`)도 같은 규칙으로 전환했다(UTODO-0013·0014). 옛 폴더·설정은 이전하지 않는다.
- 일부 시험 스크립트·도구에 Pd 경로가 박혀 있다. 목록과 과제 번호(UTODO-0001)는 `plan/90_결정관리/이전기록_2026-11-24_경로고정목록.md` 에 있다.
- `plan/09_화면설계/02_메뉴구조.md` 는 Pd 저장소의 `menu_tree.py` 가 만드는 생성물이라 이 영역에서는 재생성할 수 없다.
