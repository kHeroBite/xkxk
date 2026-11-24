# 문서 지도 — xkxk(모노레포 루트)

> 별별UI 영역 문서는 `frameworks/ui/.claude/rules/docs-map.md` 가 맡는다. 이 지도는 저장소 루트와 `docs/` 만 다룬다.

## 현행 문서
| 경로 | 역할 | 읽을 시점 |
|---|---|---|
| `CLAUDE.md` | Tier1 범용 규칙(공용 정본 symlink) | 세션 시작(자동 로딩) |
| `README.md` | 모노레포 지도(영역 표·세션 시작 위치·정본 위치) | 저장소 처음 볼 때, 영역을 고를 때 |
| `INFRA.md` | 실행 환경 — 영역 `frameworks/ui/INFRA.md` 로의 symlink(커밋 금지) | 빌드·배포·시험·접속 시(목차 Grep→절) |
| `docs/README.md` | 저장소 공통 문서 색인과 하네스 대응. 하위 `docs/decisions/README.md`·`docs/standards/README.md`·`docs/products/README.md`·`docs/modules/README.md` 포함 | 여러 영역에 걸친 문서를 찾거나 둘 때 |
| `docs/architecture/overview.md` | 제품군 구조 지도(정본은 `frameworks/ui/plan/05_아키텍처/01_XKXK_저장소구조.md`). 원문 폴더 `docs/architecture/source/` 포함 | 저장소 구조를 볼 때 |
| `modules/README.md` | 업무 모듈 자리. 같은 형식의 `apps/README.md`·`libraries/README.md`·`tooling/README.md` 포함 | 새 모듈·앱·라이브러리·도구 영역을 만들 때 |

## 보관 문서
| 경로 | 역할 | 읽을 시점 |
|---|---|---|
