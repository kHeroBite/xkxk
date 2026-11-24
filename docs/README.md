# docs — 저장소 공통 문서

> 여러 영역에 걸치는 문서만 둔다. 영역 고유 정본은 각 영역의 `plan/` 이 맡는다(별별UI 는 `frameworks/ui/plan/`).

| 폴더 | 무엇 |
|---|---|
| `architecture/` | 제품군 구조 지도(`overview.md`)와 구조 논의 원문(`source/`) |
| `decisions/` | 여러 영역에 걸친 결정 기록 자리 |
| `standards/` | 저장소 공통 표준 자리 |
| `products/` | 제품(Pd 등) 포인터 |
| `modules/` | 업무 모듈 문서 포인터 |

## 하네스와의 대응
- 작업 단위 기록(제안안의 `docs/work/tasks/`·`handoffs/`·`workflow.md`)은 만들지 않는다. 하네스가 세션 계획서·체크포인트·인계 파일·`.claude/archive/` 색인·WBS 로 같은 역할을 한다.
- 영역 결정은 각 영역 `plan/90_결정관리/`, UI 상세 설계는 `frameworks/ui/plan/05_아키텍처/`·`09_화면설계/` 에 둔다.
