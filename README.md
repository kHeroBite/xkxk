# XKXK 모노레포

> Pd 제품군의 공용 프레임워크·모듈·앱을 한 저장소에 모으는 모노레포다. 2026-11-24 첫 영역으로 별별UI(`frameworks/ui`)를 Pd 저장소에서 옮겨 시작했다.

## 영역 지도
| 경로 | 무엇 | 상태 |
|---|---|---|
| `frameworks/ui/` | 별별UI — 공용 UI 프레임워크(소스·시험·도구·디자인 자산·UI 정본 문서). 하네스 정본 영역 | 사용 중(Pd UI PoC 이전) |
| `docs/` | 저장소 전체에 걸친 문서(제품군 구조 지도·교차 결정·공통 표준·제품/모듈 포인터) | 자리와 포인터 |
| `modules/` | 업무 모듈(도메인 단위) 자리 | 비어 있음 |
| `apps/` | 실행 앱(제품 조립) 자리 | 비어 있음 |
| `libraries/` | 공용 비 UI 라이브러리 자리 | 비어 있음 |
| `tooling/` | 저장소 전체 개발 도구 자리(영역 도구는 각 영역 `tools/`) | 비어 있음 |

## 세션 시작 위치
- 별별UI 작업은 `frameworks/ui` 폴더에서 세션을 시작한다. 그 폴더의 `CLAUDE.md` → `AGENTS.md` 가 영역 규칙이고, `.claude/rules/docs-map.md` 가 문서 지도다.
- 저장소 루트의 `CLAUDE.md`·`AGENTS.md` 는 공용 하네스(Tier1) 링크라 저장소에 넣지 않는다.

## 정본 위치
- 별별UI 의 요구·설계·결정 정본은 `frameworks/ui/plan/`(진입점 `frameworks/ui/plan/README.md` → `plan/index.json`)이다.
- 별별UI 를 쓰는 다른 프로젝트(개발자·AI)용 소비자 문서·공개 API 명세는 `frameworks/ui/docs/`(진입점 [frameworks/ui/docs/README.md](frameworks/ui/docs/README.md), AI 안내 `frameworks/ui/docs/03_AI_활용안내.md`)다.
- 저장소 구조와 그 근거는 `frameworks/ui/plan/05_아키텍처/01_XKXK_저장소구조.md`, 구조 결정은 `frameworks/ui/plan/90_결정관리/02_결정기록_ADR.md` 의 UADR-0001 이다.
- 여러 영역에 걸친 결정이 생기면 `docs/decisions/` 에 둔다.
- 실행 환경은 `INFRA.md`(영역 INFRA 로의 링크, 커밋하지 않음)다.
