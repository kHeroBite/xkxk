<!-- ui 확정된 결정을 ADR 형식으로 누적하는 결정 기록 정본 -->
# 결정기록 (ADR) — ui

- 상태: 작성 중
- 최종 갱신일: 2026-11-24
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
