# PoC UI 판정보고서 — Avalonia + Dock (Phase 1)

> 상태 기록(ADR-007) · 작성일 2026-10-28 · PoC 시험 회차의 판정을 그 시점 그대로 적은 닫힌 기록이고 본문은 고치지 않는다(`../05_아키텍처/53_Pd_용어_상태어휘_문서규칙.md` §3.3) · 마지막 판정 절은 §6.5.1 이다 · 관련 ADR-007, ADR-037
> 현행 정본은 다른 문서에 있다. 합격 기준·판정 값·판정 지표·측정 규칙·시험 기준 값은 `03_PoC_UI_합격기준_Mars실측.md`(§2·§6), 창 크롬 수치는 `05_창크롬_OS디자인_기준.md`, 창 동작 규칙의 정본 대응은 `03_PoC_UI_합격기준_Mars실측.md` §2.3, D 행의 현 상태는 결정 대기 부록(d)이다(D-498). 본문의 옛 판정 낱말(PASS·FAIL·사용자 승인 PASS 등)과 옛 기준 이름(A29·H-16 등)은 03 §6.1·§6.4 로 읽는다.
> 근거 경로 가운데 세션 폴더(`gate/<ID>.json`, `<세션 기록>`)와 git 이 추적하지 않는 `poc/ui/logs/` 아래 원자료는 저장소 미수록이다(근거 수준 미확인). 본문의 세션·팀원 이름과 세션 안 번호(D-C2-NN·D-SC2 류·구 행번호)는 그 회차의 기록 표기이고, 정본 D 번호나 현행 문서의 근거로 쓰지 않는다.
> 기준 문서: `03_PoC_UI_합격기준_Mars실측.md`(MDI-01~26, EXT-01~05, PERF/IME/MOB/WEB)
> PoC 소스: `poc/ui/`(빌드·실행 방법은 `poc/ui/README.md`)
> 시험 원본: 데스크톱 `poc/ui/logs/otest/desktop/result_desktop.md`(otest-1, 하단 RETEST 1차 절·증거 `retest1/`), 웹 `poc/ui/logs/otest/web/SUMMARY.md`(otest-w-1, RETEST 반영본)

## 1. 종합 판정

| 항목 | 결과 |
|---|---|
| 현행 판정(2026-11-01) | **제품 승격 불가(현 시점)** — 정본은 §6.5.1 이다. 남은 조건은 성능 측정, 품질 Q3·Q7·Q8 전수 점검, Q5 데스크톱·웹 앱 로그 ERROR/FATAL 전수 판독이다. 아래 행들은 2026-10-28 판정 경과다 |
| 종합 판정(2026-10-28 경과) | **재판정 대기**(포그라운드 시험에서 must MDI-18 = PERF-02 가 FAIL 로 확정됨. 대안 확인 여부에 따라 조건부 또는 데스크톱 탈락 — team-lead 판단 요청). 사이클2 최종 판정은 §6.5.1 |
| 포그라운드 시험 반영(2026-10-28) | 화면 안 20창 move-all p50 40~41ms·p95 54~67ms 로 PERF-02 FAIL, 그리드 스크롤 p95 50~97ms 로 PERF-03 스크롤 FAIL 이다. 1창 기동 1.70~2.15초로 PERF-01 은 6회 중 3회가 2초를 넘었다(20창 동시 열기 기동은 3.1~3.2초). MDI-12 는 조건부다. |
| 근거 요약(RETEST 1차 반영, 포그라운드 전) | 데스크톱 must 16개 중 PASS 13, 조건부 1(MDI-10 복원 후 탭 강조), 판정 불가 2(MDI-12 화면 밖 정상화, MDI-18 = PERF-02 20창 프레임 — 둘 다 포그라운드 승인 대기)이고 **FAIL 0** 이다. 웹은 L0 시나리오 6/6 PASS(100%)이고 MOB-01 은 PASS(조건부)다. |
| ADR-007 분기 | Avalonia + Dock 유지(조건부). 혼합 C안·B안으로 내려갈 근거는 없다. 남은 해소 조건 3건을 통과하면 확정한다. |
| 1차 판정(이력) | 1차는 must 중 PASS 10, 조건부 3, FAIL 2(MDI-09 재결합, PERF-02), 미검증 1이었다. RETEST 1차에서 재결합·최소화 복원·최대화 복원·MDI-23 이 해소됐다. |

판정 규칙은 다음과 같다.
- **합격**은 must 항목이 모두 PASS(또는 사람 확인 필요로 남은 항목만 있음)이고 WEB-01 PASS 비율 ≥ 80% 인 경우다.
- **조건부**는 must 중 FAIL 이 있으나 대안(Dock 설정·자체 구현)이 있는 경우, 또는 미검증(맥 실기기 등)이 must 에 남은 경우다.
- **탈락**은 데스크톱 MDI must(MDI-01·03·06·09·10·18) 중 하나라도 대안 없이 FAIL 인 경우다. 웹 항목만 대안 없이 FAIL 이면 "웹 탈락"으로 구분한다.
- **포그라운드 실입력 반영(2026-10-28 21:49 사용자 허용)**: 사용자 부재 동안 PD PoC 창에 한해 실제 마우스·키보드 입력 시험을 허용받았다. 따라서 그동안 "사람 확인 필요"로 남기던 항목(드래그 체감 제외 — 아래)을 실입력 실측 결과로 판정한다. 대상은 실제 Ctrl+휠·Ctrl+0, 실제 한글 IME, 우클릭, 탭 더블클릭, Esc, 트레이 실표시, 제목줄 버튼 실클릭, 화면 안 성능이다. 실입력 실측 PASS 는 PASS 로 인정한다. 명령 경로(L0)만 PASS 이고 실입력이 FAIL 이면 FAIL 로 본다.
- 드래그 "체감"은 실입력 드래그 중 프레임 측정값(PERF-02)으로 대신 판정하고, 주관적 체감은 계속 사람 확인으로 남긴다. 맥 실기기는 계속 미검증이다.

판정 경과는 다음과 같다.
- 1차 FAIL 이던 MDI-09 재결합은 RETEST 1차에서 PASS 가 됐다(`retest1/r01.json` 3문서 모두 Window=null, `r01_redock.png` MDI 영역 복귀). PoC 코드 결함이었고 Dock 라이브러리의 한계가 아니었다. 재결합 후 창 위치가 분리 전과 달라지는 점(위치 순서 미보존)은 남아 있다.
- PERF-02 는 1차에 FAIL(p95 40.0ms > 잠정선 25ms)로 적었으나 RETEST 에서 **판정 불가**로 바꿨다. 화면 밖 실행은 유휴 프레임 p50 이 전 시나리오에서 약 30ms 로 바닥이 깔려(원인 추정: 화면 밖 창 렌더 주기, 미확인) 합격·불합격 근거가 되지 못한다고 otest-1 이 판단했다. 화면 밖 참고값은 20창 move-all p50 26.7ms·p95 27.3~40.0ms 이며 판정에 쓰지 않는다.

- 포그라운드(화면 안) 시험에서 화면 안 값이 화면 밖보다 오히려 느렸다. 유휴 프레임 약 30ms 는 화면 안에서도 같아서 "화면 밖 실행 때문"이라는 가설은 반증됐다. 다만 부하 구간(간트 줌·스크롤)은 p50 13~14ms 로 30ms 보다 빠르다. 따라서 30ms 는 렌더 루프의 고정 하한이 아니고 유휴 시 RequestAnimationFrame 간격으로 보인다(추정, 미확인). 20창 move-all p50 40ms 는 실제 렌더 비용으로 판단한다. 원인(20개 창 시각 트리, 그림자·테두리, 렌더 백엔드 설정 등)은 확인되지 않았다.
- 원 목표는 "60fps 근접, 끊김 체감 없음"(`05_아키텍처/02_성능_최적화.md` §2)이다. p50 40ms(약 25fps)는 25ms 잠정선과 무관하게 목표에 못 미친다.
- **한계**: Mars WinForms 의 실측 FPS 기준선이 없다. 이번 PoC 범위에서는 Mars 실행 측정을 하지 않았으므로 "Mars 수준 이상" 여부를 성능 수치로 비교할 수 없다. Mars 의 드래그 지연은 `05_아키텍처/02_성능_최적화.md` §1 의 정성 기술뿐이다.

남은 해소 조건(확정 전 필수)은 다음과 같다.
1. ~~PERF-02·03 화면 안 재측정~~ 완료했고 결과는 FAIL 이다. 해소 조건은 **렌더 최적화 PoC 1회**로 정했다(team-lead 결정, 2026-10-28). 내용은 렌더링 모드·GPU·Release/ReadyToRun·프레임 페이싱 진단과 전후 비교 화면 안 3회 측정, 그리고 PerfProbe 측정 타당성 진단이다.
   - 최적화 후 화면 안 PERF-02 p95 ≤ 25ms(또는 p50 ≤ 16.7ms 근접)를 달성하면 합격 또는 조건부로 판정한다.
   - 미달이면서 원인이 Avalonia/Dock 고유 한계로 입증되면 ADR-007 B안 검토를 사용자에게 결정 요청한다.
2. MDI-10 복원 후 작업표시줄 탭 강조가 ActiveId 와 다른 불일치 1건을 고친다(본문 창·Z순서는 동일, 시각 경미).
3. ~~MDI-12 포그라운드 시험~~ 완료했고 결과는 조건부다. 변조 좌표 4종 모두 창이 모니터 안으로 들어왔다. 그러나 앱 로그에 "MDI-12 normalize" 가 0건이고 이동 위치도 앱 계산(가장 가까운 모니터 clamp)과 맞지 않는다. 정상화 주체가 앱인지 OS·Dock 인지 확인해야 한다.
4. PERF-01 기동 시간을 확인한다. 화면 안 1창 기동 6회 중 3회가 2초를 넘었다(1.70~2.15초).

사람 확인(드래그 체감·실 한글 IME·맥 실기기·트레이 아이콘 실표시·실제 Esc·더블클릭 입력)은 자동 판정 범위 밖이므로 §6 목록으로 남긴다. MOB-01 은 RETEST 에서 PASS(조건부 — ☰ 아이콘 교체)가 됐다.


### 부록 색인 (2026-10-30 분할 — 줄 수 원칙 ADR-003, 본문 300줄 이하)

본문은 결론·판정표·남은 과제만 둔다. 상세 절은 부록으로 옮겼고 절 번호와 내용은 그대로다. 외부 문서의 `04_PoC_UI_판정보고서.md §x.y` 참조는 아래 표로 찾는다.

| 부록 | 포함 절 | 원 행 |
|---|---|---|
| [a1_RETEST_1.1_1.2.md](04_판정보고서_부록/a1_RETEST_1.1_1.2.md) | RETEST 2·3차 (§1.1·§1.2·§1.2.1) | 43~130 |
| [a2_gate결과_1.3.0_1.3.6.md](04_판정보고서_부록/a2_gate결과_1.3.0_1.3.6.md) | /oto gate 결과 (§1.3 머리·§1.3.1·§1.3.3~1.3.6) | 131~330 |
| [a3_원인결정_1.3.7_1.3.9_1.3.10.md](04_판정보고서_부록/a3_원인결정_1.3.7_1.3.9_1.3.10.md) | Q4 원인 체인·SDI 문서 뷰 재생성·UI-19 (§1.3.7·§1.3.9·§1.3.10) | 331~370, 486~529 |
| [a4_유휴렌더_1.3.8.md](04_판정보고서_부록/a4_유휴렌더_1.3.8.md) | 진단판 유휴 렌더 (§1.3.8) | 371~485 |
| [a5_UI20_분할_1.3.11.md](04_판정보고서_부록/a5_UI20_분할_1.3.11.md) | UI-20 도구 패널 분할 (§1.3.11) | 530~699 |
| [a6_최근판확인_1.3.12_1.3.2.md](04_판정보고서_부록/a6_최근판확인_1.3.12_1.3.2.md) | i1835rc·i2000rc 확인·Q15·Q17 재시험 틀 (§1.3.12·§1.3.13·§1.3.2) | 700~770 + §1.3.13 추가 |
| [a7_실측환경_데스크톱결과_2_3.2.md](04_판정보고서_부록/a7_실측환경_데스크톱결과_2_3.2.md) | 실측 환경·데스크톱 항목별 결과 (§2~§3.2) | 771~901 |
| [a8_Mars실측_3.3_3.6.md](04_판정보고서_부록/a8_Mars실측_3.3_3.6.md) | Mars 실측 사양 (§3.3~§3.6) | 902~1088 |
| [a9_성능_웹_모바일_4_5_6.x.md](04_판정보고서_부록/a9_성능_웹_모바일_4_5_6.x.md) | 성능·웹 동일성·모바일 축과 후속 (§4·§5·§6.1·§6.3·§6.6, 분할 뒤 §4.2 추가 2026-11-06~09, 반복 1·2·3 재측정 절은 a11 로 이동 2026-11-13) | 1089~1192, 1204~1207, 1227~1243, 1300~1328 |
| [a10_측정보류_목록.md](04_판정보고서_부록/a10_측정보류_목록.md) | 측정 보류 목록 H-01~H-17(ms 보류·실입력 보류·후속, 2026-11-11 신설) | 신규(분할 아님) |
| [a11_성능_웹_모바일_재측정.md](04_판정보고서_부록/a11_성능_웹_모바일_재측정.md) | a9 에서 옮긴 §4.2 반복 1·2·3 재측정(2026-11-13 신설) | 분할 뒤 추가분 |

**구 행번호 대응표.** goal 계약의 `plan_ref` 는 잠겨 있어 분할 전 행번호를 그대로 쓴다. 판정기(`c_oto_gate.py`)는 이 보고서를 읽지 않고 `gate/Q*.json` 만 보므로 판정에는 영향이 없다. 포인터는 아래 표를 따라간다. Q11(`05_창크롬_OS디자인_기준.md:43`)·Q18(`02_소스구조_분할규칙.md:19`)은 다른 파일이라 해당 없다.

| 계약 항목 | 구 행번호 | 현재 위치 | 절 | 현재 행 |
|---|---|---|---|---|
| Q1 | 139 | 부록 `04_판정보고서_부록/a2_gate결과_1.3.0_1.3.6.md` | §1.3 머리(게이트 표) | 13 |
| Q2 | 43 | 부록 `04_판정보고서_부록/a1_RETEST_1.1_1.2.md` | §1.1 | 5 |
| Q3 | 440 | 부록 `04_판정보고서_부록/a4_유휴렌더_1.3.8.md` | §1.3.8 | 74 |
| Q4 | 477 | 부록 `04_판정보고서_부록/a4_유휴렌더_1.3.8.md` | §1.3.8 | 111 |
| Q5 | 72 | 부록 `04_판정보고서_부록/a1_RETEST_1.1_1.2.md` | §1.1 | 34 |
| Q6 | 339 | 부록 `04_판정보고서_부록/a3_원인결정_1.3.7_1.3.9_1.3.10.md` | §1.3.7 | 13 |
| Q7 | 209 | 부록 `04_판정보고서_부록/a2_gate결과_1.3.0_1.3.6.md` | §1.3 머리(게이트 표) | 83 |
| Q8 | 210 | 부록 `04_판정보고서_부록/a2_gate결과_1.3.0_1.3.6.md` | §1.3 머리(게이트 표) | 84 |
| Q9 | 252 | 부록 `04_판정보고서_부록/a2_gate결과_1.3.0_1.3.6.md` | §1.3.3 | 126 |
| Q10 | 107 | 부록 `04_판정보고서_부록/a1_RETEST_1.1_1.2.md` | §1.2 | 69 |
| Q12 | 169 | 부록 `04_판정보고서_부록/a2_gate결과_1.3.0_1.3.6.md` | §1.3 머리(게이트 표) | 43 |
| Q13 | 8 | 본문(이 파일) | §1 | 8 |
| Q14 | 206 | 부록 `04_판정보고서_부록/a2_gate결과_1.3.0_1.3.6.md` | §1.3 머리(게이트 표) | 80 |
| Q15 | 212 | 부록 `04_판정보고서_부록/a2_gate결과_1.3.0_1.3.6.md` | §1.3 머리(게이트 표) | 86 |
| Q16 | 213 | 부록 `04_판정보고서_부록/a2_gate결과_1.3.0_1.3.6.md` | §1.3 머리(게이트 표) | 87 |
| Q17 | 214 | 부록 `04_판정보고서_부록/a2_gate결과_1.3.0_1.3.6.md` | §1.3 머리(게이트 표) | 88 |

## 6. 사람 확인 필요·미검증 (자동 판정 불가)

- 실제 마우스 드래그 체감(MDI-14·18, PERF-02).
- 실제 한글 IME 입력(IME-01, Windows Chrome·Mac Safari — 웹 시험 브라우저는 Chrome 계열, 사용자 지시 2026-10-31 21:06).
- 맥 실기기 전 항목.
- 다중 실모니터 배치(MDI-13).
- (종결) 포그라운드 필요였던 MDI-12 화면 밖 창 정상화와 PERF-02·03 화면 안 재측정은 2026-10-28 포그라운드 시험으로 실행됐다(§1 해소 조건 1·3, 결과는 조건부·FAIL).
- 트레이 아이콘 실표시, 실제 Esc 키·더블클릭 입력, 실기기 터치(MOB-01).
- UI-20·REQ-25 시험에서 나온 후속 D 항목(D-SC2·D-TP1 종결·D-PF1·D-DAD2·D-PW1·D-PW2·D-GV1·D-RH1·D-UIM1)은 §1.3.11 ⑦ 미확인·후속 표에 모아 두었다.
- 여전히 미검증으로 남은 것은 플로팅 위치 복원, 핀 상태 재기동 복원, Ctrl+Tab 키 소비, 탭 좌/우클릭, 이름 변경, 프로젝트 동기화, 화면 캡처 방지다.

## 6.2 결정 대기 (사용자 확인 필요)

| 항목 | Mars | PD PoC 현재값 | 확인할 것 |
|---|---|---|---|
| 항상 위(핀) 저장 | 저장하지 않는다(재기동 시 풀림, §3.3) | **저장한다**(레이아웃 `IsPinned`, 재기동 복원 — odev-7c 구현 기본값) | Mars 와 다른 기본값을 유지할지 |
| 창 외곽선 | 1px | 3D 기본 2px + BoxShadow(3,4,12, #70000000)다. 설정(MarsFrame)으로 Mars 식 1px 을 고를 수 있다. 여백 띠(3,4,2,2)와 제목줄 23px(고정)은 Mars 와 같다. 근거는 `Themes/MdiChrome.axaml`(md5 b505986f…)·`Docking/WindowCommands.Chrome.cs`(md5 a1f6d862…) 실측이다. | 기본 스타일을 무엇으로 할지 |
| 설정 숫자 입력(글자 크기) | — | 문서 기본값으로 유지 | 21:40 변경 이후에도 숫자 입력을 둘지(§3.1 UI-02) |
| ~~위치 고정 중 최대화·최소화~~ | — | **조사 근거로 확정**(아래 §6.3). 최대화·최소화를 허용한다. | 결정 대기에서 뺐다 |
| UI-10 활성 구분·입체감 강도 | 활성 외곽선은 제목줄 색을 30% 밝게 한 색(#7989AF)이다. 비활성 제목줄은 Gray 로 바꾸기만 하고 흐리게 하지 않는다. | 사용자의 3D·입체감 요구와 RETEST 2차 "활성 구분 약함"에 따라 바꿨다. pd-light 활성 외곽선은 Mars 보다 진한 **#22325A** 다(`Themes/MdiChrome.axaml:12`, `Theming/BuiltinThemes.cs`). 비활성 창의 제목 영역(`PART_Grid`)은 **투명도 0.7** 로 흐리게 한다(`MdiChrome.axaml:149`). 활성 창 그림자는 **0 4 16**, 알파는 토큰 값과 **0x90** 중 큰 값이다(`MdiChrome.axaml:25`, `WindowCommands.Chrome.cs:253~254`). 코드 실측으로 확인했다. | **확정(결정 대기 해제)** — 사용자 결정 "그래 Mars보다 강화해서 진행해"(2026-10-28 23:22). Mars 보다 강화한다. 단 사용자 정정(23:25 "진짜 윈도우나 맥의 창들처럼 완성도높게")에 따라 **현대 OS 스타일**로 바꾼다. 둥근 모서리(최대화 시 0), 1px 외곽선, 부드러운 고도 그림자, 비활성 반투명 처리를 쓰고 3px 베벨·그라데이션안은 대체됐다. 기준은 `05_창크롬_OS디자인_기준.md` 다. Mars 원형은 MarsFrame 옵션으로 둔다. |
| 그림자 범위 | 코드는 `CS_DROPSHADOW` 를 켜지만, 이 스타일은 최상위 창 전용이라 MDI 자식에는 그림자가 나오지 않는다. 픽셀 실측에서도 그림자는 0px 였다(합격기준 문서 §4.1). | **모든 창에 반투명 그림자를 둔다. 활성은 진하게, 비활성은 약하게 한다**(사용자 요구 "창 자체가 돌출되 보이도록 창 아래에 반투명 그림자", **Mars 보다 강화**). 퍼짐을 작게 잡아 20창 move-all 을 직전 그림자 on 값(약 40ms) 이하로 유지하는 것이 목표다. 테두리도 단색 2px 에서 다층 베벨(바깥 하이라이트/섀도우 1px + 그라데이션 본체 + 안쪽 1px)로 바뀐다(odev-7c). 최대화 창 그림자 없음과 전역 끄기(설정 "창 그림자")는 유지한다. 이전 방침(활성 창만, 성능 근거)의 근거는 odev-4b 화면 안 MDI 20창 실측(`poc/ui/logs/odev4b/slotB/sh_*.perf.json`)이다. move-all p50 은 그림자 on 39.8~40.9ms(3 vsync), off 26.7~32.3ms(2 vsync)였다(파일 재확인 일치). 활성 전환은 두 지표로 잰다. 1회 소요(`steps[step=="cmd:next"].ms` 10회 최대, 명령 실행부터 단계 완료까지의 벽시계 시간)는 on 85.7~119.7ms, off 57.3~71.2ms 다. 구간 프레임 간격 최대(`phases[name=="cmd:next"].frame_max_ms`)는 on 48.8~88.6ms, off 32.5~50.6ms 다(odev-4b 지표 정의 회신, 파일 재계산 일치). 두 지표 모두 on > off 다. 전 창 그림자의 20창 성능은 RETEST 에서 다시 잰다. | **확정(결정 대기 해제)** — 사용자 결정 "그래 Mars보다 강화해서 진행해"(2026-10-28 23:22). 전 창 그림자(활성 진하게·비활성 약하게)로 Mars 보다 강화한다. 테두리는 23:25 정정에 따라 3px 베벨 대신 현대 OS 스타일(1px 외곽선 + 부드러운 고도 그림자, `05_창크롬_OS디자인_기준.md`)로 한다. |
| 제목줄 높이(현대 OS 스타일) | 23px | 현재 23px(Mars 동일)다. Windows 11 표준은 32px(상호작용 요소가 있으면 48px)다(`05_창크롬_OS디자인_기준.md`). | **확정(리드 판단, 스타일별 높이)** — Windows 스타일(기본)은 Win11 표준 32px, MarsClassic 은 23px 다. 그림자 오프셋·블러·투명도 등 공식 문서에 없는 값(확인 불가)은 PD 설계값으로 두고, Win11 실제 창 캡처와 비교해 조정한다. |
| 기본 렌더 모드 | 해당 없음(WinForms GDI) | 기본은 **AngleEgl + WinUI 합성 유지**(Avalonia Win32 기본값). 화면 안 20창 move-all p95 3회 측정에서 기본은 28.2/27.9/65.6ms, WGL 은 20.8/21.9/23.2ms, Software 는 25.5/24.7/28.8ms 였다. | WGL 로 바꿀지. 단 WGL·Software 는 시간 기반 60fps 타이머 틱이라 74Hz 모니터에서 60fps 로 묶이고, 틱 의미가 기본(DWM commit 완료 기준)과 달라 "WGL 이 실제로 더 부드럽다"고 이 수치만으로 단정할 수 없다(odev-4b, 추정·미확인 포함). 그리드 스크롤 p95 39~58ms 는 렌더 모드와 무관한 DataGrid CPU 비용으로 판단했다(odev-4b). 출처는 `poc/ui/logs/odev4b/result_odev4b.md` 다. |

Mars 와 다르거나 PoC 에서 모의로 처리한 항목(결정 대상은 아니지만 판정 때 감안한다)은 다음과 같다(odev-7c 보고).
- 제목줄 글리프는 이모지 대신 벡터 아이콘이다. 의미는 같고 교체·색 표시도 같다.
- 캐시 적중 비율은 모의값이다. 같은 종류를 읽을수록 0→37→75→100% 로 오른다. 실제 서버 캐시 연동은 본 개발 범위다.
- 도움말은 모의 메뉴 DB 를 쓴다. 입력폼은 "아직 작성되지 않았습니다"를 띄운다.
- 새로고침은 확인 플라이아웃 뒤 창을 닫고 같은 위치·크기·상태·배율·프로젝트·SDI 로 다시 연다. 문서 Id·Seq 는 새 값이 된다.

## 6.4 사용자 결정 4건 (D-193 권장안 확정, 결정 114)

| # | 결정할 것 | 현재 상태 | 선택지 | 근거 |
|---|---|---|---|---|
| 1 | `pd/.claude/scripts/safe_click.ps1` 처리 | Mars 하네스 도구의 바이트 단위 사본이다(594줄, md5 동일). 제품 코드가 아니고 배포되지 않는 시험 도구이지만, 원칙 4(Mars 소스 복사 금지)를 엄격히 적용하면 복사에 해당한다. Mars 고유 실측 주석(107~111줄)도 남아 있다. | ① 범용판으로 하네스 공용 도구에 승격하고 Mars·PD 는 symlink 로 가리킨다(odev-6r 권고) ② PD 전용 최소판(약 150줄)으로 새로 쓴다 ③ 개발 도구 예외로 그대로 두고 첫 줄에 "Mars 하네스 도구 사본"을 표기한다 | `poc/ui/logs/odev6r/mars_copy_audit.md` "별도 판정" 절 |
| 2 | 항상 위(핀) 저장 | PD 는 저장(레이아웃 `IsPinned`, 재기동 복원)이 기본이다. Mars 는 저장하지 않는다. | 저장 유지 / Mars 처럼 저장 안 함 | §3.3, §6.2 |
| 3 | 글꼴(글자) 크기 숫자 입력 유지 | 21:40 변경으로 크기 조절은 Ctrl+휠(Ctrl+0)로만 한다. 설정의 숫자 입력은 문서 기본값으로 남아 있다. | 숫자 입력 유지 / 제거 | §3.1 UI-02, §6.2 |
| 4 | 기본 렌더 모드 | AngleEgl + WinUI 합성(Avalonia 기본) 유지다. WGL 은 20창 이동 p95 가 더 낮게 나왔으나 60fps 타이머라 직접 비교가 어렵다. | 기본 유지(odev-4b 권고, 사람 체감 확인 병행) / WGL 전환 | §6.2, `poc/ui/logs/odev4b/result_odev4b.md` |

- UI-13 실측에서 나온 2건(자동저장 끈 채 종료 시 삭제, SDI 모두닫기 확인)은 리드가 확정했다(§3.5 "PD 결정과 Mars 대비"). 삭제하지 않고 저장만 하지 않으며, 모두닫기는 Mars 처럼 확인 없이 닫는다.
- **권장안 확정(D-193, 결정 114, 2026-11-11 사용자 답 A-11).** 옵션화 대원칙(ADR-089: 값은 초기 제시값이고 회사·사용자 설정으로 바꾼다)과 원칙 4(Mars 소스 복사 금지)로 판단한 권장안이다. ① safe_click.ps1 은 하네스 공용 도구로 승격하고 Pd 저장소 사본은 지운다(odev-6r 권고). ② 핀 저장은 설정 옵션으로 두고 초기 제시값은 '저장', Mars 방식(저장 안 함)을 고를 수 있다. ③ 글자 크기 숫자 입력은 설정 옵션으로 유지한다. ④ 렌더 모드는 기본 AngleEgl + WinUI 합성을 유지하고 WGL·Software 를 설정 옵션으로 둔다(odev-4b 권고). `Pd.Poc` 이름 공간 개명 시점(§6.5)은 제품 저장소 착수(M0) 때로 둔다.
- §6.2 의 같은 행에 상세 수치가 있다. 사용자가 결정하면 §6.2 해당 행을 "확정"으로 바꾸고 이 표에서 뺀다.

## 6.5 제품 승격 조건 (ADR-038)

사용자 지시(23:45)에 따라 이 PoC 가 속도와 품질을 만족하면 제품 기본 클라이언트로 승격한다. 아래 조건을 모두 만족해야 승격한다.

성능 조건은 PERF-UI 목표 전 항목 통과다(§4.1 의 잠정 기준: 즉시 반응형 ≤ 100ms, 창 열기 ≤ 500ms, 테마·글자 크기 20창 전체 반영 ≤ 300ms, 프레임 p95 ≤ 25ms). PERF-01~05 도 포함한다. 화면 안·렌더펌프 on 실측만 판정에 쓴다.

품질 체크리스트는 다음과 같다.

| # | 조건 | 확인 방법 |
|---|---|---|
| Q1 | 기능 결함 0 | 판정보고서 must 항목 전부 PASS(사람 확인 항목은 확인 완료) |
| Q2 | 시각 결함 0 | 겹침·잘림·빈 화면·글리프 깨짐 0건(스크린샷 판독) |
| Q3 | 계층 분리 | UI 중립 ViewModel·셸·문서·호스트·진단 계층이 분리되고 계층 간 참조 방향이 지켜진다(ADR-007 `Pms.Presentation` 원칙) |
| Q4 | 파일 300줄 이하 | 소스 파일 전수 줄 수 검사 0건 초과(ADR-036 원칙 1) |
| Q5 | 오류 처리와 로그 | 예외가 삼켜지지 않고 로그에 남는다. 앱 로그 ERROR/FATAL 0건(시험 기준) |
| Q6 | 진단 기본 꺼짐 | 제품 빌드에서 원격 제어·자동화 브리지·perf 가 기본 꺼짐이고, 켜면 127.0.0.1·토큰·CORS 금지를 지킨다(UI-08) |
| Q7 | 자동 시험 유지 | L0 시나리오·원격 명령 회귀 시험이 승격 후에도 빌드마다 돈다 |
| Q8 | Mars 복사본 없음 | 제품 저장소에 Mars 코드 복사가 0건이다(odev-6r 감사 방법, ADR-036 원칙 4) |

아직 남은 항목(RETEST 3차 결과 전 기준)은 다음과 같다.
- 성능: PERF-02(20창 이동 p95 41ms, 화면 안), PERF-03 그리드 스크롤, PERF-UI 20창 글자 크기·폰트 반영, SDI 분리·재결합, 20창 레이아웃 복원, 웹 7문서 테마 전환, 웹 perf-ui 전체 모드 완주.
- 기능: 문서 본문 빈 화면, UI-09 캐시 버튼, MDI-10 탭 강조, UI-08 0.0.0.0 바인드, wheelMode 복원, 실제 창 끌기(수정 대기 재시험), UI-11·12·13 구현.
- 품질: Q3·Q4·Q6·Q7·Q8 전수 점검 미실시, safe_click.ps1 처리(§6.4 1번).
- 미검증: 맥 실기기 전 항목, 실키입력(Esc·Ctrl+0·한글 IME), 물리 폴딩.
- 결정: `Pd.Poc` 이름 공간 개명 시점(ADR-038). 권장안은 제품 저장소 착수(M0) 때다(§6.4 아래 권장안, 확정 D-193, 결정 114).

### 6.5.1 최종 판정 (oto 사이클2 conv_179346042968, 2026-11-01 기록)

판정 기준은 사이클2 계약(goal 2026-11-01 00:30)의 `gate/<ID>.json`(판정자 ≠ 구현자, `c_oto_gate.py item <ID>` rc)이다. 판본은 최종 빌드 z(진단 c2dev_z·제품 c2prod_z, manifest `poc/ui/logs/odev_c2/build/manifest_c2dev_z_c2prod_z.md`)이고, z 이전 판본(u·v·w·x·y8)에서 확정되고 z 회귀 범위 밖인 항목은 그 판본 판정을 그대로 쓴다. 진단 z 의 Desktop.dll·Shell.dll 은 Q2 #16 측정판 c2dev_y8 과 md5 가 같다(gate/Q2.json final_build_z).

**결론은 "제품 승격 불가(현 시점)"다.** 사이클2 계약 gate 38개는 모두 rc=0 이다. 그러나 승격 조건(§6.5)에는 gate 밖 조건이 있고, 다음 조건이 남아 있다.
1. 성능 조건은 사용자 지시로 측정에서 뺐다. 그래서 충족 여부를 판정할 수 없다.
2. 품질 Q3 계층 분리·Q7 자동 시험 유지·Q8 Mars 복사본 없음은 이번 사이클에 전수 점검하지 않았다.
3. 품질 Q5 는 부분 확인에 그쳤다. 데스크톱·웹 앱 로그 ERROR/FATAL 전수 판독이 남았다.

Q2 #16 SDI 창 그림자는 그림자 별도 창 방식(접근 C)으로 고친 뒤 재측정해 PASS 가 됐다(D-C2-61·62). Q14 ③ Win11 스냅도 시험 축 otest-4 의 재측정으로 PASS 가 됐다(D-C2-56). 그래서 둘 다 남은 조건에서 뺐다. 근거와 한계는 아래 한계 목록과 §6.5.2 Q14 행에 있고, Q2 에서 넘어온 개선 과제는 아래 "다음 사이클 개선" 목록에 있다.

승격 여부는 사용자가 결정한다. 이 절은 결론과 남은 조건을 사실대로 적는다.

| 조건 | 대응 gate(사이클2) | 결과 | 증거 |
|---|---|---|---|
| 성능: PERF-UI 목표 전 항목 | Q5·Q10, Q4(Android 회귀) | **측정 제외(사용자 지시)** — goal scope_out, 결정 D-C2-28. 사이클2 계약의 Q5·Q10 gate 는 계약 이전 파일이라 판정에 쓰지 않는다 | goal.json scope_out, D-C2-28 |
| 품질 Q1 기능 결함 0 | Q3·Q6·Q8·Q14·Q17, U1~U21, D21·D22 | **PASS** — Q14 데스크톱 ③ 스냅은 시험 축 otest-4 재측정으로 PASS(D-C2-56). Q3·Q6·Q8·Q14·Q17·U1~U21·D21·D22 모두 gate PASS(rc=0) | gate/Q14.json, `poc/ui/logs/otest/c2/w_fg/t14.log`, `poc/ui/logs/otest/c2/snap_t4/user_windows_compare.txt`, gate/Q3.json·Q6.json·Q8.json·Q17.json |
| 품질 Q2 시각 결함 0 | Q2·Q11·Q14 | **PASS(z)** — Q2 #16 SDI 그림자는 그림자 별도 창 방식(접근 C)으로 고친 뒤 기준 v3.2 0·A0~A4·B1~B8 PASS(측정판 c2dev_y8, 최종 z 와 dll md5 같음). 바깥 16px 밝기 감소 합(왼/아래)은 활성 390.4/447.1·비활성 234.1/290.0 이다(이전 x 는 활성=비활성 111.5/162.1). Q11·Q14 도 PASS. 한계는 아래 목록에 있다 | gate/Q2.json, `poc/ui/logs/otest/c2/q16_t4/criteria.md`, `poc/ui/logs/otest/c2/q16_y8/q16_profile.json`, gate/Q11.json |
| 품질 Q3 계층 분리 | (gate 없음) | **미점검** — 이번 사이클에 전수 점검을 하지 않았다. D-C2-19 코드 검토의 계층 분리 확인은 수정 파일에 한정한 부분 근거다 | `poc/ui/logs/otest/c2/review/d_c2_19_code_review.md` |
| 품질 Q4 파일 300줄 이하 | Q18·Q19(사이클2) | **PASS(w 동결 소스 기준)** — src_line_gate live rc=0(대상 383, 위반 0, 예외 0), partial_gate live rc=0(위반 0). x 의 수정 5파일(ChromeLogo.cs·WindowChromeStyle.cs·ChromeResources.axaml·DesktopMonitorProvider.cs·DesktopWindowHost.cs) 뒤 src_line_gate 전수 재실행 기록은 없다. 다만 oreview-3 가 x 수정 5파일 줄 수를 실측했고 42·294·110·127·294 로 모두 300 이하다(gate/Q13.json reviewer_measure). z 에서 고친 SdiShadowWindow.cs 262·SdiWindowHost.cs 235 줄도 300 이하다(gate/Q2.json known_limits_carryover_D-C2-62). 이 역시 src_line_gate 전수 재실행은 아니다 | `poc/ui/logs/otest/c2/review/q18_src_line_gate_w.txt`, `poc/ui/logs/otest/c2/review/q19_partial_gate_w.txt` |
| 품질 Q5 오류 처리와 로그 | Q2·Q4(로그 판독) | **부분 확인** — Fold5 는 x·w 모두 FATAL·ANR 0 이다. 데스크톱·웹의 앱 로그 ERROR/FATAL 전수 판독은 하지 않았다 | `poc/ui/logs/otest/c2/fold5/fold5_result.md`, `poc/ui/logs/otest/c2/fold5/x/` |
| 품질 Q6 진단 기본 꺼짐 | Q9 | **PASS(z)** — 제품 z 데스크톱(검사 45)·Android(검사 690)·웹(검사 62) 정적 금지 적중 0, 진단 심볼 0. 웹 ⑤ 유휴 rAF 는 c2prod_z 로 2회 쟀고(otest-w-4), 두 회차 모두 6구간 rAF 0(0/초)이며 전역 pdAutomation·__pdPoc 는 undefined 다. 서빙 루트는 `poc/ui/src/Pd.Poc.Browser/bin/web_c2prod_z_publish/wwwroot`(216파일, Shell wasm Pd.Poc.Shell.g2usi5ixs8.wasm)이고 서빙 기록에 남겼다. 부팅 완료는 canvas 출현으로만 확인했다 | gate/Q9.json, `poc/ui/logs/otest/c2/review/q9_c2prod_z_desktop.json`, `poc/ui/logs/otest/c2/review/q9_c2prod_z_apk.json`, `poc/ui/logs/otest/c2/review/q9_c2prod_z_web_static.json`, `poc/ui/logs/otest_c2w/z/raf1/reg.json`, `poc/ui/logs/otest_c2w/z/raf2/reg.json`, `poc/ui/logs/otest_c2w/z/serve_root.txt` |
| 품질 Q7 자동 시험 유지 | (gate 없음) | **미점검** — 빌드마다 도는 회귀 시험 체계는 아직 없다. 이번 사이클은 기존 시험 도구(`poc/ui/test/`)를 수동으로 돌렸다 | — |
| 품질 Q8 Mars 복사본 없음 | (gate 없음) | **미점검** — 이번 사이클에 감사를 다시 하지 않았다. safe_click.ps1 처리(§6.4 1번)는 D-193 으로 방향 확정(결정 114)이며 제거 이행 전이다 | §6.4 1번 |
| 빌드 재현성 | Q1 | **PASS(z)** — 데스크톱·웹·Android 진단·제품 6종 rc=0, 오류 0 | gate/Q1.json, `poc/ui/logs/odev_c2/build/manifest_c2dev_z_c2prod_z.md` |
| **최종** | — | **승격 불가(현 시점)** — 계약 gate 38개는 모두 rc=0 이다. 남은 조건은 성능 측정, Q3·Q7·Q8 전수 점검, Q5 데스크톱·웹 앱 로그 ERROR/FATAL 전수 판독이다 | 위 행 전부 |

아래 목록은 PASS 항목에 붙은 측정 한계와, 측정 불가로 남은 항목이다. 측정 불가로 적은 것은 PASS 로 바꾸지 않았다.
- Q2 #16 SDI 창 그림자는 그림자 별도 창 방식(접근 C)으로 PASS 다(gate/Q2.json retest_y8_t4, 원자료 `poc/ui/logs/otest/c2/q16_y8/`). 근거와 한계는 다음과 같다.
  - 근거는 넷이다. 첫째, 기준 v3.2(`poc/ui/logs/otest/c2/q16_t4/criteria.md`)를 첫 증거(`poc/ui/logs/otest/c2/q16_y8/build_md5.txt`)보다 먼저 고정했고 측정 뒤 md5 가 같다. 둘째, 바깥 16px 밝기 감소 합(왼/아래)이 활성 390.4/447.1·비활성 234.1/290.0 으로 활성이 더 진하고, oreview-4 가 원본 캡처로 독립 재계산해 일치했다(`poc/ui/logs/otest/c2/q16_y8/q16_profile.json`). 셋째, A0~A4(밝기 비교)·B1~B8(창 구조·z 순서·이동·최소화·닫기·최종 6종 빌드)이 모두 PASS 다. 넷째, 최종 z 의 Desktop.dll·Shell.dll 이 측정판 y8 과 md5 가 같다.
  - 한계는 넷이다. 메모장 기준값은 같은 회차가 아니라 08:03 캡처(`poc/ui/logs/otest/c2/w_fg/q2_16/np_active.png`)를 고정 상수로 썼고, c2dev_y 결과를 본 뒤 기준 조항을 바꿨다(임계값은 유지, D-C2-58·61). Alt+Tab 직접 확인은 측정 불가다(작업표시줄은 그룹 표시라 창 수 구분 불가, `poc/ui/logs/otest/c2/q16_y8/b1_taskbar_mid.png`). 다중 모니터·DPI 는 측정 불가다(물리 모니터 1대). 회귀 retest_window_menu 는 PASS 28·HOLD 4·FAIL 0 이다. HOLD 4건은 #4 메인 창 초기화·#5 싱글 모니터 최대화·#6 멀티 모니터 최대화·#7 SDI 위치 초기화이고, 사유는 포그라운드 노출 불가(기존 보류)다(`poc/ui/logs/otest/c2/q16_y8/regress_window_menu.log`). 화면 안 보정(MDI-12)은 두 회귀 도구(retest_window_menu·retest_remote_z)에 항목이 없어 회귀에 포함되지 않았다. 그림자는 별도 창이라 SDI 좌표 보정 로직은 바꾸지 않았다. 이 회귀는 다음 사이클로 넘긴다(D-C2-64).
- Q6 ⑥ 멀티 모니터 최대화는 가상 모니터 2대(`--primary-override 6000,0,3840,1080 --virtual-monitors 2`)로 흉내만 냈다. 물리 모니터가 1대(DISPLAY1 1920x1200)라 실제 다중 모니터는 측정하지 않았다. 코드 경로는 `DesktopWindowHost.cs:134` 의 Screens.All 합집합으로 확인했다(증거 `poc/ui/logs/otest/c2/w_fg/x/q6_6/max_multi.json`).
- Q14 ③ Win11 스냅은 시험 축 otest-4 의 재측정으로 PASS 다(D-C2-56, gate/Q14.json, 원자료 `poc/ui/logs/otest/c2/snap_t4/`). 근거와 한계는 다음과 같다.
  - 근거는 넷이다. 첫째, 합격 기준(frame 네 변 ±2px)을 결과를 보기 전에 고정했다(`poc/ui/logs/otest/c2/snap_t4/criteria.md`). 둘째, c2dev_x(`poc/ui/logs/otest/c2/snap_t4/build_md5.txt`)에서 Snap Layouts 50/50 두 칸을 2회 쟀다. 오른쪽 칸은 frame (960,0)-(1920,1152), 왼쪽 칸은 frame (0,0)-(960,1152)로 기대값과 오차 0 이다(`poc/ui/logs/otest/c2/snap_t4/m1_frame.txt`·`poc/ui/logs/otest/c2/snap_t4/m2_frame.txt`). 셋째, 사용자 창 16개를 측정 전후 전수 대조했고 rect·최대화·최소화 변화가 0 이다(`poc/ui/logs/otest/c2/snap_t4/user_windows_compare.txt`). 넷째, 플라이아웃 소유(explorer '팝업 호스트')를 클릭 전에 확정하고 첫 시도부터 그 PID·창제목을 지정해 가드 차단이 0 이다(`poc/ui/logs/otest/c2/snap_t4/flyout1_owner.txt`).
  - 한계는 셋이다. Win+←/→·가장자리 끌기는 측정 불가다(입력 가드 F10-3, safe_click 에 Win 키 없음). 제품판 c2prod_x 는 측정 안 함이다. 마우스 커서는 복원하지 못했다(수단 없음).
  - 아래는 재측정 전 이력이다. 판정 근거로 쓰지 않는다.
  - 시험 축 otest-3 은 Win+←/→ 를 SendInput 으로 보내는 일회성 스크립트가 입력 가드 F10-3 에 막혀 측정하지 못했다(우회하지 않음). **이전에 이 칸을 PASS 로 둔 것은 근거 없는 판정 오류였다.** 그때 근거는 한 줄 배치와 더블클릭뿐이었다(D-C2-52).
  - 이어서 DEV 라운드의 odev-snap 이 Snap Layouts 플라이아웃(최대화 버튼 hover → 50/50 왼쪽 칸 클릭)으로 OS 스냅을 일으켰다. 창 프레임이 (0,0)-(960,1152), 곧 작업영역 왼쪽 절반 960×1152 가 됐다(c2dev_x, 증거 `poc/ui/logs/otest/c2/snap/`). **이 실측은 보조 근거로만 쓴다(D-C2-54).** 사유는 둘이다. 첫째, 플라이아웃이 탐색기(explorer) 소유 팝업이라 safe_click 창소유권 가드에 막혔고, 그 뒤 기대 PID 를 explorer 로 바꿔 재시도했다. 이것은 가드 차단 뒤 표현을 바꿔 재시도한 것이라 L-597 에 저촉될 소지가 있고, 리드가 사전 승인한 것은 오류였다. 둘째, DEV 팀원의 확인은 판정 근거로 쓰지 않는다(D-C2-47). Win+화살표·가장자리 끌기·다른 레이아웃은 측정하지 않았다.
  - 부작용이 하나 있었다. 클릭한 칸의 오른쪽 칸에 Win11 스냅 제안으로 사용자 터미널(WindowsTerminal)이 있어서, 그 창이 클릭 전 rect 가 최대화 기하와 같은 상태(rect -8,-8,1928,1160, 최대화 플래그는 미기록이라 추정, `poc/ui/logs/otest/c2/snap/hover.log` 11행)에서 오른쪽 절반으로 함께 배치됐다. 곧바로 최대화로 원복했다(`poc/ui/logs/otest/c2/snap/side_effect_restore.log` zoomed=True). 원복 뒤 rect 는 클릭 전과 같은 -8,-8,1928,1160 이고 zoomed=True 다(`poc/ui/logs/otest/c2/snap/now_user_windows.txt`). 메모장 등 다른 사용자 창은 바뀌지 않았다.
  - 그때 정한 재측정 조건은 셋이었다. 측정은 구현자가 아닌 시험 축이 한다. 사용자 창 보호책을 먼저 둔다(스냅 제안 칸에 사용자 창이 없는 레이아웃·칸을 고르고, 사용자 창 위치·상태를 앞뒤로 기록해 비교한다). 탐색기 소유 팝업을 누르는 기준은 미리 정한다. otest-4 재측정은 이 셋을 지켰다(D-C2-56).
- 측정 불가로 남은 것은 셋이다. 트레이 아이콘 실표시(셸 오버플로 클릭이 safe_click 대상 제한에 걸림), Q2 #18 최소화 버튼 pressed 상태(hover 와 같은 색이라 미확정), Fold5 관리형 창 로고 터치(N/A, MDI 문서 로고만 실측 — `poc/ui/logs/otest/c2/fold5/x/logo_touch.json`)다.

**다음 사이클 개선** — Q2 #16 판정 뒤 넘긴 15항목이다(gate/Q2.json known_limits_carryover_D-C2-62). 측정판 c2dev_y8 을 유지하려고 이번 사이클에는 그림자 코드를 고치지 않았다(D-C2-62). 판정을 바꾸는 항목은 아니다.
1. [중간] `SdiShadowWindow.cs:120` 의 _drawn 키에 모양·켜짐이 들어 있지 않다. 그래서 그림자 끔·Mars 클래식 전환·리소스 변경이 다음 이동이나 활성 변경 때까지 반영되지 않는다.
2. [중간·정적] SDI 만 닫힐 때 그림자 정리가 WM_DESTROY 훅 하나에 기댄다(비소유 창이라 OS 가 자동으로 파괴하지 않음). B7 세 경로 측정에서는 남지 않았다. 안전망(Closed 정리·IsWindow 확인)을 넣는다.
3. [낮음] 훅 안의 Sync/Draw 예외 보호와 GetDC 실패 확인이 없다.
4. [낮음] `SdiWindowHost.cs:32` 주석이 '소유 레이어드 창' 이다. 지금은 비소유 창이다.
5. [낮음] Sync 경로에 재진입 가드가 없다(지금은 재귀 원천이 없음).
6. [낮음] 항상 위 해제 분기(HwndNoTopmost 선행)에서 그림자가 잠깐 깜빡일 수 있다.
7. [참고] 권장 200줄을 넘는다. SdiShadowWindow.cs 262·SdiWindowHost.cs 235 줄이다(상한 300 이내).
8. [관찰] B7 ① WM_CLOSE 직후 제목 없는 Pd 가시 창 1개가 보였다. 원인은 확인하지 못했다.
9. [도구 — 해결됨] retest_user_files 격리의 --data-dir 필수화, 재기동 비격리 앱 누수, 기본 모드의 사용자 Pd 0개 선행 조건(중간 3)과 stop_pid PID 재사용 경합, 하위 폴더 판정, README 59행(낮음 3)이다. odev-tool round2(retest_user_files a1cbc497·remote_z 8830b2ba·window_menu f37f22b3·README 251e556a)에서 반영했고, oreview-4 재검토와 otest-4 --isolated 15/0 실행 확인을 마쳤다. 남은 것은 아래 10~12번이다. 제품 영향은 없다.
10. [도구 낮음 a] 재기동 격리가 실패했을 때 remote-control.json 의 pid 가 사용자 Pd 이면, 도구가 띄운 앱을 찾지 않아 남을 수 있다(retest_remote_z.py 126~135행·retest_window_menu.py 81~88행).
11. [도구 낮음 b] 비격리 사전 거부가 run_desktop_bg 로 띄우지 않은 사용자 Pd 만 센다. 다른 시험 도구의 Pd 가 있으면 wait_app_exit 시간 초과 뒤 복원한다(retest_remote_z.py 185~189행·retest_window_menu.py 168~172행). 사용자 Pd 정의는 그대로 둔다(리드).
12. [도구 낮음 c] 거부 문구가 옛 규칙('--data-dir/--profile-dir')이다(retest_window_menu.py 177행과 retest_remote_z.py 같은 문구). 현 규칙은 --data-dir 필수다.
13. [원칙] 회귀 도구의 비격리 기본 모드는 앞으로 쓰지 않는다(리드 결정). 사용자 Pd·다른 시험 Pd 가 함께 있으면 복원 경합 위험이 있다.
14. [예외] 앱은 격리 모드(임시 --data-dir)에서도 사용자 폴더(%LOCALAPPDATA%\PdPoc)에 remote-control.json 을 쓴다(기존 동작). layout·settings 에는 영향이 없다. '사용자 폴더에 쓰지 않음' 원칙의 예외이고, '--isolated 쓰기 0' 은 도구 기준으로만 성립한다.
15. [회귀 미포함] 화면 안 보정(MDI-12) 회귀는 기존 도구에 항목이 없어 y8 에서 돌지 않았다. 다음 사이클에 회귀 항목을 추가한다(D-C2-64).
16. [미해결 결함, 2026-11-11 추적 등재] D-PW1(알림 위치를 옮겼다 되돌리면 끌어 맞춘 폭을 기억하지 못함)과 D-PW2(접기·되살리기 뒤 측면 패널 폭 0) 는 i2000rc 에서 세 축 FAIL 또는 판정불가로 남았고 다음 판(i2400) 확인 결과가 기록되지 않았다(부록 a6 §1.3.13). 제품 단계에서 재시험한다.

### 6.5.2 사이클2 결과 기록 (oto 사이클2 Q13)

판정은 `gate/<ID>.json`(판정자 ≠ 구현자)이 정본이고, 증거는 `poc/ui/logs/otest/c2/`·`poc/ui/logs/otest_c2w/` 아래 계약 시각(2026-11-01 00:30) 이후 생성 파일이다. 성능 수치는 기록하지 않는다(scope_out, 사용자 지시). 괄호 안 글자(u·v·w·x)는 판정에 쓴 판본이다. 경로는 저장소 루트 기준이고, gate 파일은 세션 폴더의 `gate/` 아래에 있다.

| 항목 | 내용 | 데스크톱 | 웹 | Fold5 | gate·증거 |
|---|---|---|---|---|---|
| UI-11 | 레이아웃 템플릿·사용자 레이아웃 저장/복원 | 이전 증거(사이클1 w15, 2026-10-31 22:59 gate Q7 PASS) — 이번 c2 사이클 미재측정. 보조 근거로 c2 Q2 #22 레이아웃 템플릿 목록·적용(열린 문서 3→3 유지)·기본 템플릿 삭제 거부(ReadOnly) PASS(w) | 이전 증거(사이클1 w14, gate Q7 웹 7-1~7-7 PASS) — 이번 c2 사이클 미재측정 | 이전 증거(사이클1 w15, gate Q7 펼침 a~g·접힘 b·c PASS) — 이번 c2 사이클 미재측정 | c2 계약 gate 없음(D-C2-28). 이전 증거 `poc/ui/logs/otest/desktop/i2410fg/w15_desktop_result.md`, `poc/ui/logs/otest/desktop/i2410fg/w15_fold5_result.md`, `poc/ui/logs/otest/desktop/i2410fg/w14_gate_web.md`. c2 보조 근거 `poc/ui/logs/otest/c2/w/fix/q2w_fix_rows.json`(#22, 데스크톱만) |
| UI-12 | 자석(이동·크기 조절, 범위 설정) | PASS — #31~#36 범위 기본 24·0 비활성, 실시간 흡착, 리사이즈 끄는 가장자리, 고정·최대화 제외, 놓은 위치=보인 위치 | PASS(w) — 관리형 창 #31~#35. #36 프레임당 시간은 성능이라 제외 | PASS — 펼침 MDI #31~#35. 접힘 NAV 는 MDI 가 없어 N/A | gate/Q8.json. `poc/ui/logs/otest/c2/q6q8_desktop.json`, `poc/ui/logs/otest_c2w/w/q6q8/q6q8_web.json`, `poc/ui/logs/otest/c2/fold5/wm_open.json` |
| UI-13 | Mars 윈도우 메뉴 기능(Q6) | PASS(x, 조건부) — 백그라운드 22 PASS(재기동 유지 6 포함), 포그라운드 ④⑤⑦ PASS. ⑥ 은 가상 모니터 흉내(물리 1대, 실다중 미측정) | PASS(w) — PASS 27·N/A 4·FAIL 0, 재기동 유지 5행 PASS. N/A 는 #4 메인 창 초기화(OS 창 아님)·#5·#6 단일 뷰 최대화·#13 화면 캡처 방지(브라우저 적용 수단 없음) | PASS(w) — 펼침 PASS 28·N/A 4. 접힘 FAIL 4(#124~#127)는 폰 NAV 전제가 없는 도구 판정이라 D-FOLD 로 N/A 처리 | gate/Q6.json. `poc/ui/logs/otest/c2/q6_persist.json`, `poc/ui/logs/otest/c2/w_fg/x/q6_6/max_multi.json`, `poc/ui/logs/otest/c2/fold5/w/wm_open.json`, `poc/ui/logs/otest/c2/fold5/w/wm_fold.json` |
| Q14 | 제목줄 한 줄 메뉴 [파일·창·보기·시스템]·빈 공간 끌기·더블클릭·스냅 | **PASS(x, D-C2-56 재측정)** — 한 줄 배치(로고·메뉴 4·최소·최대·닫기 같은 y, 순서 일치)·① 빈 공간 끌기·② 더블클릭 최대화/복원은 PASS. ③ Win11 스냅은 시험 축 otest-4 가 Snap Layouts 50/50 두 칸을 2회 쟀고, 오른쪽 (960,0)-(1920,1152)·왼쪽 (0,0)-(960,1152)로 오차 0 이다. 사용자 창 16개 변화 0, 가드 차단 0, 기준은 사전 고정(criteria.md)이다. 한계는 Win+←/→·가장자리 끌기 측정 불가, c2prod_x 측정 안 함, 커서 미복원이다. 이전 개발 확인(odev-snap)과 사용자 터미널 부작용은 이력으로 §6.5.1 한계 목록에 남겼다 | PASS — 제목줄 0~32 안 로고·메뉴 4 한 줄. 캡션 버튼·빈 공간 끌기·스냅은 브라우저 기능이라 N/A(D-WEBCAPTION, W-01) | PASS — 펼침·접힘(402dp) 모두 한 줄. 메인 캡션 3버튼은 N/A(D-C2-20). 빈 공간 끌기·더블클릭·스냅은 OS 창이 없어 N/A | gate/Q14.json. `poc/ui/logs/otest/c2/u2/rows.json`, `poc/ui/logs/otest/c2/u2/menu_main.png`, `poc/ui/logs/otest/c2/w_fg/t14.log`, `poc/ui/logs/otest_c2w/misc_r2/misc_web.json`, `poc/ui/logs/otest/c2/fold5/fold5_result.md`, `poc/ui/logs/otest/c2/snap_t4/criteria.md`, `poc/ui/logs/otest/c2/snap_t4/m1_frame.txt`, `poc/ui/logs/otest/c2/snap_t4/m2_frame.txt`, `poc/ui/logs/otest/c2/snap_t4/user_windows_compare.txt`. 이력 `poc/ui/logs/otest/c2/snap/snap_click.log` |
| U1~U5 | Ctrl+0 · 클래식 표시명 · 활성 전환 렌더(GPU) · 얇은 탭 스크롤 · 탭 hover | PASS | PASS | PASS | gate/U1~U5.json 전부 PASS. `poc/ui/logs/otest/c2/u/rows.json`, `poc/ui/logs/otest/c2/u2/u2_settings.json`, `poc/ui/logs/otest/c2/u3_sys.json`, `poc/ui/logs/otest/c2/u/u4_scrollbars.json`, `poc/ui/logs/otest/c2/u/u5_hover.png` |
| U6~U10 | 캡션 아이콘색·최소화 위치 · 최소화 표시줄 제거 · 하단 모서리 · 기본 창 UX · 벡터 아이콘 | PASS | PASS | PASS | gate/U6~U10.json 전부 PASS. `poc/ui/logs/otest/c2/chrome_judge.log`, `poc/ui/logs/otest/c2/u/u7_items.json`, `poc/ui/logs/otest/c2/u9_desk.log`, `poc/ui/logs/otest/c2/u/u10_scan.json` |
| U11~U15 | 캡션 간격 · MDI 로고 · 중앙 드롭 MDI · 분할 해제 · SDI 단일 탭 숨김 | PASS | PASS | PASS | gate/U11~U15.json 전부 PASS. `poc/ui/logs/otest/c2/u/u11_Windows.png`, `poc/ui/logs/otest/c2/chrome/mdi_Windows.png`, `poc/ui/logs/otest/c2/u2/u13_after.png`, `poc/ui/logs/otest/c2/u2/u14_after.png`, `poc/ui/logs/otest/c2/u2/u15_sdi.png` |
| U16~U21 | 창 메뉴 통합 · 'Pd' 글자 제거 · 알림 서랍 합류 · 패널 선택 반전 · 모니터 선택 · Tab 포커스 | PASS(U20 은 가상 모니터 흉내, 실다중 미측정) | PASS(U20 은 N/A — W-03 브라우저 단일 표면) | PASS(U20 은 N/A — Android 단일 표면) | gate/U16~U21.json 전부 PASS. `poc/ui/logs/otest/c2/u2/menu_lists.json`, `poc/ui/logs/otest/c2/u2/menu_tree.json`, `poc/ui/logs/otest/c2/u2/u18_joined.png`, `poc/ui/logs/otest/c2/q2b/q2b_rows.json`, `poc/ui/logs/otest/c2/u20/max_button.json`, `poc/ui/logs/otest/c2/u2/u21_focus.json` |
| 최종 판정(§6.5 승격 조건 대비) | — | 승격 불가(현 시점) | 승격 불가(현 시점) | 승격 불가(현 시점) | §6.5.1 과 같다. 계약 gate 38개는 모두 rc=0 이고, 남은 조건은 성능 측정·Q3·Q7·Q8 점검·Q5 데스크톱·웹 앱 로그 ERROR/FATAL 전수 판독이다 |

## 7. 부록 — 합격 기준 누락 대조

- Mars 고유 심볼 164개(+속성 1개)를 합격기준 문서와 스크립트로 대조했다(2026-10-28). 누락 0, 매핑 129개(+`IsEffectivelyMaximized`), 제외 35개다.
- 증거 png 는 데스크톱 1차 31장(SDI PrintWindow `sdi_win_0~2.png` 포함)과 RETEST `retest1/` 36장, 웹 21장(M1~M7 포함)이다(목표 ≥ 6).
