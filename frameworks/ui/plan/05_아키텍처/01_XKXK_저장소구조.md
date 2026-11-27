# XKXK 저장소 구조 — 모노레포와 별별UI 영역

> 이 문서는 XKXK 모노레포(`/mnt/c/DATA/Project/xkxk`)의 디렉터리 구조와 그 근거의 정본이다.
> 결정 기록은 `/mnt/c/DATA/Project/xkxk/frameworks/ui/plan/90_결정관리/02_결정기록_ADR.md` 의 UADR-0001 이다.
> 근거 원문은 GPT 대화 사본 `/mnt/c/DATA/Project/xkxk/docs/architecture/source/gpt_XKXK_구조대화_원문_2026-11-24.txt`(2828~2975행 구조 제안, 3790~3860행 프로젝트 홈 표)이다.
> 작성일은 2026-11-24 이고, Pd UI PoC 이전 사이클(대화ID conv_179547985218)에서 만들었다.

## 1. 검토 대상과 판단 기준

- 검토 대상은 GPT 가 제안한 XKXK 디렉터리 구조다. 루트 `AGENTS.md`·`CLAUDE.md`, `docs/{architecture,standards,decisions,products,modules,work}`, `frameworks/`·`modules/`·`apps/`·`libs(libraries)/`·`tooling/` 으로 이루어진다.
- 판단 기준은 현재 하네스가 만드는 정본 체계다. 하네스는 Tier1 공용 지침(`CLAUDE.md`·`AGENTS.md` symlink), 영역 `plan/`(정본 산출물), test_root·tools_root·ref_root, `HISTORY.md`·`LESSONS.md`, `.claude/rules/docs-map.md`, 세션 plans·checkpoint·archive 를 쓴다.
- 원칙은 하네스가 이미 맡는 일을 제안 구조로 중복하지 않는 것이다. 제안 구조는 하네스가 다루지 않는 모노레포 지도와 교차 영역 자리에만 쓴다.

## 2. 겹침·충돌 표

| GPT 제안 | 하네스 체계 | 관계 | 결정 |
|---|---|---|---|
| 루트 `AGENTS.md` 실내용 + `CLAUDE.md` 에 `@AGENTS.md` | `CLAUDE.md`·`AGENTS.md` = Tier1 공용 symlink(ocopy), Tier1 에 프로젝트 고유 내용 금지 | **충돌** | 루트(xkxk)는 Tier1 symlink 2개 유지(미추적). 영역 `frameworks/ui/AGENTS.md` 는 실파일(영역 규칙), `frameworks/ui/CLAUDE.md` 는 실파일 한 줄 `@AGENTS.md`. 상위 CLAUDE.md 로딩은 추정이며 사후 확인한다 |
| `docs/decisions/` (ADR) | 영역 `plan/90_결정관리/` | 겹침 | 영역 결정은 `frameworks/ui/plan/90_결정관리/`, 교차 결정만 루트 `docs/decisions/`(지금은 README 자리만) |
| `docs/work/tasks/`·`handoffs/`·`workflow.md` | 세션 plans·checkpoint·orespawn 인계·archive INDEX·WBS | **겹침(하네스가 상위)** | `docs/work/` 는 만들지 않는다. 루트 `docs/README.md` 에 대응 1줄 |
| `docs/architecture/ui.md` | 영역 `plan/05_아키텍처/`·`plan/09_화면설계/` | 겹침 | UI 상세 정본은 영역 plan. 루트 `docs/architecture/overview.md` 는 제품군 지도 + 포인터 |
| `docs/standards/` | Tier1·영역 `plan/04_기술스택/02_소스구조_분할규칙.md` | 일부 겹침 | 루트 `docs/standards/README.md` 자리만 |
| `docs/products/pd/` vs `apps/pd/` | Pd 는 별도 저장소로 남음 | 범위 밖 | `docs/products/README.md` 포인터만, `apps/pd` 없음 |
| `libs/` vs `libraries/` | — | 제안 내 불일치 | `libraries/` 채택 |
| `pd` vs `pms` | Pd 가 정식 약칭 | 제안 내 불일치 | `pd` 채택(포인터만) |
| `frameworks/ui/src/` | test_root·tools_root·ref_root, `plan/` | 보완 | `frameworks/ui/` = 하네스 정본 영역 |
| `tooling/` | 영역 `tools/` | 겹침 | `tooling/README.md` 자리만, 영역 도구는 `frameworks/ui/tools/` |

## 3. 경계(루트 대 영역)

- 루트 xkxk(git toplevel)에는 Tier1 링크(CLAUDE·AGENTS·.mcp.json), `README.md`, `INFRA.md`(영역 INFRA 로의 symlink), `docs/`, 빈 영역 자리 README(modules·apps·libraries·tooling), `.claude/`(하네스 링크 + `rules/docs-map.md` + `docs_gate.json` + `archive/` 미추적), `.gitignore`·`.gitattributes` 를 둔다.
- 영역 frameworks/ui 에는 `AGENTS.md`·`CLAUDE.md`, `.claude/`(링크 + `rules/docs-map.md` + `docs_gate.json`), `plan/`, `src/ test/ tools/ assets/ vendor/ ref/`, `HISTORY.md`·`LESSONS.md`·`INFRA.md`(미추적), PoC 루트 파일(`xkxk.frameworks.ui.slnx`·`Directory.Build.props`·`global.json`·`LAYERS.md`·`README.md`)을 둔다.
- 하네스 해석 정합은 이렇다. 정본 루트 해석기 `canon_root.py` 는 위로 올라가며 `.git` 또는 `.claude/` 를 가진 첫 폴더를 프로젝트 루트로 보므로 영역 경로는 `frameworks/ui` 로 풀린다. 문서 갱신 게이트와 ofinish 보관은 git toplevel(xkxk) 기준이라, 루트 `.claude/docs_gate.json` 에 `docs.history="frameworks/ui/HISTORY.md"`, `canon.plan_root="frameworks/ui/plan"`, `canon.test_root="frameworks/ui/test"`, `canon.tools_root="frameworks/ui/tools"` 를 둔다.
- INFRA 경로는 영역 `frameworks/ui/INFRA.md` 실파일이고, 루트 `INFRA.md` 는 symlink 다. 둘 다 미추적이다. 루트 symlink 는 루트 docs-map 현행 표에 행으로 등재한다(md_audit 가 루트 md 로 수집하기 때문이다).
- 다중 영역(다음 영역이 생길 때)에서 루트 docs_gate 가 한 영역만 가리키는 한계는 남는다. 하네스 수정은 AI 프로젝트 세션 소관이다.

## 4. 최종 트리

```text
/mnt/c/DATA/Project/xkxk/                 # git 저장소 루트(모노레포)
├── CLAUDE.md -> Tier1 (미추적) · AGENTS.md -> Tier1 (미추적) · .mcp.json -> 공용 (미추적)
├── INFRA.md -> frameworks/ui/INFRA.md (미추적, 루트 docs-map 등재)
├── README.md · .gitignore · .gitattributes
├── .claude/  (ocopy 링크 미추적 · locks/ · archive/ 미추적)
│   ├── rules/docs-map.md · docs_gate.json
├── docs/ (README · architecture/overview.md · architecture/source/ · decisions · standards · products · modules)
├── frameworks/ui/                         # 별별UI — 세션 홈·하네스 정본 영역
│   ├── AGENTS.md · CLAUDE.md(@AGENTS.md) · .mcp.json(미추적) · INFRA.md(미추적)
│   ├── HISTORY.md · LESSONS.md · README.md · LAYERS.md · xkxk.frameworks.ui.slnx · Directory.Build.props · global.json
│   ├── .claude/ (링크 · rules/docs-map.md · docs_gate.json)
│   ├── plan/ (README.md · index.json · 00·01·04·05·09·90 · _build[menu_icons·menu_icon_data·icon_glyphs·menu_data])
│   ├── src/                               # 프레임워크(패키지 대상)만
│   │   ├── xkxk.frameworks.ui · .data · .shell · .views · .fonts
│   │   └── xkxk.frameworks.ui.hosting.desktop · .browser · .android   # 호스트 공통 코드(F3 B11 소스 분리, 패키지화는 F5)
│   ├── testbed/                           # 시험대(IsPackable=false) — 모듈 1(testbed/xkxk.frameworks.ui.testbed, B10)·호스트 3(B11)·consumer·scenarios/ (F3 B10~)
│   ├── test/(README.md)
│   │   └── framework/                     # 프레임워크 검증 스크립트·일괄 실행기 verify_framework (B02~)
│   ├── tools/(README.md 신규) · assets/ · vendor/ · ref/README.md
├── modules/README.md · apps/README.md · libraries/README.md · tooling/README.md
```

- 시험대 `testbed/` 와 검증 스크립트 `test/framework/` 는 2026-11-26 프레임워크화 F0 에서 정했다(UADR-0008). 구성·검증 표는 `/mnt/c/DATA/Project/xkxk/frameworks/ui/plan/05_아키텍처/02_별별UI_프레임워크화_계획_부록/a3_시험대_검증계획.md` 가 정본이다. 두 폴더는 각 batch 에서 생긴다.
- 시험대를 저장소 루트 `apps/` 에 두지 않는다. `apps/` 는 제품 앱 자리(§1·§2 의 GPT 제안 구조)이고, 시험대는 제품이 아니라 프레임워크를 공개 API 만으로 검증하는 영역 내부 자산이다. 영역 안에 두면 test_root·tools_root·빌드 스크립트·빌드 잠금을 그대로 쓴다.
- `src/xkxk.frameworks.ui.hosting.*` 는 a4 Q3 확정에 따라 F3(B11)에서 지금 호스트의 공통 코드(Win32 훅·SDI 창·저장소)를 소스로만 분리한 자리다. 남는 Program·csproj 는 `testbed/` 의 실행 호스트로 옮긴다. NuGet 패키지화는 F5 에서 한다.

## 5. 하네스 산출물과의 대응

| 하네스 산출물 | 이 저장소의 자리 |
|---|---|
| 정본 산출물(요구·아키텍처·화면설계·결정·TODO) | `frameworks/ui/plan/` (색인 `plan/index.json`) |
| 개발 산출물(소스) | `frameworks/ui/src/` |
| 시험 자산(test_root) | `frameworks/ui/test/` (색인 `test/README.md`) |
| 개발 도구(tools_root) | `frameworks/ui/tools/` (색인 `tools/README.md`) |
| 참조 색인(ref_root) | `frameworks/ui/ref/README.md` |
| 작업 이력·교훈 | `frameworks/ui/HISTORY.md`·`LESSONS.md` |
| 세션 계획·인계·체크포인트 | 세션 폴더(저장소 밖). `docs/work/` 를 두지 않는다 |
| 재사용 산출물 보관(ofinish) | `xkxk/.claude/archive/` (미추적) |

## 6. 이번에 하지 않은 것

- `Pd.Poc.*` 이름 전환은 2026-11-24 `xkxk.frameworks.ui.*` 로 했다(UTODO-0007, UADR-0002). 데이터 폴더·단일 인스턴스·Android 패키지도 2026-11-25 같은 규칙으로 전환했다(UTODO-0013·0014, UADR-0003). `apps/pd` 이전, 루트 공통 표준 본문 작성은 하지 않았다.
- NuGet 패키지화는 이 문서 작성 때 하지 않았다. 2026-11-26 프레임워크화 F5 에서 층별 패키지로 하기로 정했다(UADR-0007, 계획 `/mnt/c/DATA/Project/xkxk/frameworks/ui/plan/05_아키텍처/02_별별UI_프레임워크화_계획.md` §11).
- 경로가 박힌 시험 스크립트는 고치지 않고 `/mnt/c/DATA/Project/xkxk/frameworks/ui/plan/90_결정관리/이전기록_2026-11-24_경로고정목록.md` 에 UTODO-0001 로 남겼다.
- `plan/09_화면설계/02_메뉴구조.md` 생성기(`menu_tree.py`)는 옮기지 않았다. 재생성은 Pd 저장소에서 한다.
