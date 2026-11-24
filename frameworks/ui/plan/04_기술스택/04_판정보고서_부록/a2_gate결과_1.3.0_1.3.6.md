# 부록 a2 — /oto gate 결과 (§1.3 머리·§1.3.1·§1.3.3~1.3.6)

> 상태 기록(ADR-007). 판정보고서 본문 `../04_PoC_UI_판정보고서.md` 에서 2026-10-30 에 분리한 그 시점의 시험 기록이라 본문을 고치지 않는다(`../../05_아키텍처/53_Pd_용어_상태어휘_문서규칙.md` §3.3). 최종 판정의 정본은 본문 §1·§6.5.1 이고 합격 기준 수치의 정본은 `../03_PoC_UI_합격기준_Mars실측.md` 다.
> 증거 경로 `poc/ui/logs/`(git 추적 안 함)와 세션 기록(gate 판정 파일 포함)은 저장소 미수록이고 근거 수준은 미확인이다. 세션 사이 전달만 적힌 칸도 근거 수준 미확인이다. 결함 번호 `D-<영문·숫자>`·gate 번호 `Q<n>` 은 PoC 기록 전용 번호라 결정 대기 번호 D-NNN 과 다르다. 'M4(Fluent 동적 브러시 동기화)'는 시험 항목 이름이고 출시 단계 M4 가 아니다.
> §1.3 표와 §1.3.1 표의 '판정 대기'·'진행 중'·'재시험 전' 칸은 동결 당시 값이다. 그 뒤 판정은 a3~a6 의 해당 절과 본문 §6.5.1 이 갖는다.
> §1.3.6 ② 칸의 '결정(P87)'은 D-45(ADR-044 2026-11-01 개정)를 가리킨다.

## 1.3 /oto 목표 계약 gate 결과 (2026-10-29, 2차 동결 판본)

이 절의 Q1~Q17 은 /oto 목표 계약(goal.json acceptance)의 gate 번호다. §6.5 품질 체크리스트의 Q1~Q8 과는 다른 번호 체계다. gate 판정 원본은 세션 기록 gate/Q{n}.json 이며, 아래 증거 경로는 그 파일이 가리키는 프로젝트 안 파일이다. 판정이 아직 없는 칸은 '판정 대기'로 두었다. 추정은 결과로 적지 않았다.

판본 표기는 다음과 같다.
- 데스크톱 2차 동결 판본은 Work2(시험)·Product(제품) 00:52 빌드다(`poc/ui/logs/odev4b/freeze2_manifest.txt`).
- 웹은 시험 00:55, 제품 00:57 번들이다.
- Android 는 Q4 판정 APK md5 f070ba95…, Q16 판정 Debug APK 01:29 시험이다.
- 그 뒤 통합 진단 판은 i0136(01:32), i0156, i0215(02:15) 다. 산출물은 데스크톱 `bin/i0215_diag`, 웹 `publish_i0215`, 진단 APK md5 565e7931… 이다(`poc/ui/logs/odev4b/i0215_manifest.txt`). 표에 판본을 따로 적은 칸은 그 판본 결과다.

| gate | 대상 | 판정 | 판본 | 근거 요약 | 증거 |
|---|---|---|---|---|---|
| Q1 | 6종 빌드(Release·R2R, 시험·제품) | **FAIL** | 2차 동결 | 6종 rc=0·오류 0. 그러나 R2R 산출물이 새 판본이 아니다(아래 R2R 항목). | `poc/ui/logs/odev4b/build_freeze2.log`, `poc/ui/logs/odev4b/freeze2_manifest.txt` |
| Q2 | 데스크톱 재시험 | **FAIL**(23:40 R2R 기준 1차 자료) | 23:40 R2R | MDI-Z 재기동 문서 0개, layout.load 20창 p95 783ms, 그리드 열기 p50 1547ms 등이다. 새 판본 재시험 전이다. | `poc/ui/logs/otest/desktop/result_desktop.md` |
| Q3 | 웹 재시험(관리형 SDI·MRU·잠금) | **PASS** | 웹 00:55/00:57 | 관리형 SDI 버튼 실클릭, MRU zIndex, 잠긴 창 내용 영역 끌기 차단, 원격 33검사가 모두 통과했다. | `poc/ui/logs/otest_w2/run12.txt`·`run13.txt`·`run16.txt`·`run17.txt`, `poc/ui/logs/otest_w2/retest_web.json`, `poc/ui/logs/otest_w2/parity_table.md` |
| Q4 | Fold5 실기·성능 회귀 | **현재: i1505 조용한 창 재측정 — theme·ctrlWheel PASS, font 회귀 아님(UI-15 새 기능 비용), 데스크톱 빈칸 1.25·2.0 PASS·2.25 미검증, 상세 §1.3.7.** 당시: **FAIL** | APK f070ba95…(당시) / i1505 d26567d8(현재) | 이하 당시 경위. 기동은 정상이다(크래시 0, [loc] 경고 0). 23:10 APK 기준 대비 회귀가 있다(아래 표). i0451 A_ss 도 theme 498~511ms·font 338~346ms 로 여전히 FAIL 이다(§1.3.3). | `poc/ui/logs/otest_m1/gate_Q4_evidence_v3.md` |
| Q5 | PERF-UI 목표 | 판정 대기 | 데스크톱 i0215·i0136(성능 구간 5) / 웹 00:55 | 데스크톱 20창 move-all p50 은 약 53ms 다. '제목줄 버튼 min' 조건만 40ms 다(§1.3.1). 웹은 2차 판본에서 재측정하지 않았다(web_Q_results). | `poc/ui/logs/odev4b/perf5/perf5_summary.json`, `poc/ui/logs/odev4b/result_odev4b.md` §11 |
| Q6 | UI-13 윈도우 메뉴 13항목 | 데스크톱 31 중 29 PASS, #5·#6 FAIL(otest-1 원문). 웹 **PASS** 26·N/A 4·FAIL 0(i0451, §1.3.3) | 데스크톱 i0215_diag / 웹 i0451 | 데스크톱 FAIL 2건은 #5·#6 '모니터 맞춤' 에서 크기가 바뀌지 않는 것이다. 웹은 시험 미수행이다. | `poc/ui/logs/otest/desktop/rt3/q6_window_menu_desktop.json` |
| Q7 | UI-11 레이아웃 라이브러리 | 판정 대기 | 웹 00:55 | 웹은 기본 저장·복원만 PASS 다. 이름 저장·변경·삭제는 미시험이다. | `poc/ui/logs/otest_w2/web_Q_results.json` |
| Q8 | UI-12 자석 | 데스크톱 MDI **PASS**(odev-6r), Android **PASS**(otest-m-1), 웹 판정 대기(웹 부분 FAIL) | 데스크톱 i0215_diag / Android i0156 진단 APK(b7c12f3e) / 웹 00:55 | 데스크톱은 윈도우 메뉴 시험 #31(기본 24, 0 이면 비활성, 맞닿는 선 흡착)이 PASS 다. Android 는 자석 범위(0~48·2단위) 항목이 PASS 다. 웹은 MDI 바탕 4점이 #FFFFFF 다(기준 #778899). 자석 웹 시험은 명령이 없어 미수행이다. | `poc/ui/logs/odev6r/wm_i0215_b.json`, `poc/ui/logs/otest_m1/window_menu_android_i0156.json`, `poc/ui/logs/otest_w2/run15.txt`, `poc/ui/logs/otest_w2/q8_mdi_bg.png` |
| Q9 | 제품 빌드 진단 제거·유휴 rAF | **FAIL**(조건 ⑤) | 웹 제품 00:57 | 조건 ①~④ 는 충족했다. ⑤ 는 유휴 6초 rAF 361회(60.2/s)로 불충족이다. 원인은 Avalonia 쪽이다(아래). | `poc/ui/logs/otest_w2/q9_product_check2.json`, `poc/ui/logs/odev3/web_idle_raf.md` |
| Q10 | 그리드 첫 열기 ≤ 0.7초 | 판정 대기 | 데스크톱 R2R i0136(성능 구간 5) | R2R 연속 기동 5회는 1043ms(756~1115) 다. JIT 와 교차한 3회는 442ms(362~472) 다. 두 묶음 차이는 CPU 경합으로 추정한다(ctor.seed 근거, 확인 전). 다음 판에서 부하 지표를 붙여 다시 잰다(§1.3.1). | `poc/ui/logs/odev4b/perf5/perf5_summary.json`, `poc/ui/logs/odev4b/result_odev4b.md` §11 |
| Q11 | 창 크롬 독립 심사 | 판정 대기(예비 의견 정정 있음) | 00:34 사진 | 아래 Q11 항목 참조. | `poc/ui/logs/otest_m1/q11_review.md` |
| Q12 | Z순서·MRU 재기동 유지 | 데스크톱 FAIL 보고(SDI 핀 7/8, docs.list SDI zIndex null — 수정 예정). 웹 **PASS**(i0451, §1.3.3) | 데스크톱 i0215_diag / 웹 i0451 | 문서 복원 수·제목과 SDI 문서 집합은 유지된다. 복원 뒤 SDI 문서(Z1·Z2) zIndex 가 null 이다. | `poc/ui/logs/otest/desktop/rt3/t12_A_result.json` |
| Q13 | 판정보고서 기록·md_audit | 진행 중 | — | 이 절이 Q13 기록이다. | — |
| Q14 | 제목줄 한 줄 통합 | 데스크톱 FAIL(기본 기동이 두 줄 — 기본값 on 전환 예정), 웹 FAIL 보고 있음 | 데스크톱 i0215_diag / 웹 00:55 | 데스크톱 증거는 `poc/ui/logs/otest/desktop/rt3/q14_light.png`·`q14_dark.png` 다. 웹은 다음과 같다. | 한 줄 배치·캡션 숨김은 PASS 다. 로고·제목 대비는 FAIL 이다(PdBrushText 수정 전 판본). 끌기·더블클릭·스냅은 N/A 다. | `poc/ui/logs/otest_w2/run15.txt`, `poc/ui/logs/otest_w2/q14_titlebar.png` |
| Q15 | UI-14 패널 탭 합류 | 데스크톱: 합류·전환·분리·저장 유지 PASS, 끌기 순서 변경·끌어내기는 L2 시도 예정(otest-1 원문). 웹: 판정 대기(i0136 FAIL → i0215 재시험 대기) | 데스크톱 i0215_diag / 웹 00:55·i0136 | 웹 i0136 은 문서 합류 1→1(pdJoinTarget 0)이다. 로그 도구 합류(1→2)와 저장 유지는 PASS 였다. 원인과 대응은 §3.6 UI-14 에 적었다. | `poc/ui/logs/otest/desktop/rt3/t15_16_checks.json`, `poc/ui/logs/otest/desktop/rt3/q15_panel_tabs.png`, `poc/ui/logs/otest_w2/run20.txt`, `poc/ui/logs/otest_w2/run21.txt` |
| Q16 | UI-15 알림 패널 | Fold5 **PASS**, 웹 'PASS(부분 미판정)'(시험 축 원문. 미판정은 위치 메뉴 실클릭, 다른 패널 끌기 때 안내 판독), 데스크톱 **PASS**(i0215_diag, `poc/ui/logs/otest/desktop/rt3/t15_16_checks.json`). gate/Q16 은 데스크톱 시험 뒤 otest-1 이 세 축을 묶어 판정한다 | Fold5 01:29 / 웹 00:55 | Fold5 11검사 PASS·FAIL 0 이다. 웹은 위치 메뉴 실클릭과 안내 판독이 스크린샷 증거만 있다. 접힘(단일 문서 모드)에서는 패널이 visible=false 다. 접근 경로는 구현 중이라 판정 대기다. | `poc/ui/logs/otest_m1/android_panel.json`, `poc/ui/logs/otest_w2/run13.txt`, `poc/ui/logs/otest_w2/q16_right.png` |
| Q17 | UI-16 다국어 | 데스크톱: 전환·유지·누락 0·UI 한글 0 PASS, 영어 잘림 2건(Ctrl+휠 콤보, 간트 도구줄)(otest-1 원문). 웹 FAIL 보고 있음 | 데스크톱 i0215_diag / 웹 00:55 | 웹은 lang.set en 응답이 ko 였다(전환 안 됨). 웹 i18n.missing=0 이다. 알림 패널 영어 잘림은 i0215 에 수정이 들어갔고 Fold5 확인 대기다. | `poc/ui/logs/otest/desktop/rt3/q17_lang_desktop.json`, `poc/ui/logs/otest/desktop/rt3/q17_scan_en_main.json`, `poc/ui/logs/otest/desktop/rt3/q17_en_main.png`, `poc/ui/logs/otest_w2/run14.txt`, `poc/ui/logs/otest_w2/q17_probe_en.png` |

Q4 Android 회귀 수치(23:10 APK 기준 대비, otest-m-1)는 다음과 같다.

| 조작 | 이번 판본 | 기준 | 차이 |
|---|---|---|---|
| theme.next 중앙값 | 809~858ms | 479ms | +69~79% |
| ui.font.family | 407~420ms | 274ms | +49~53% |
| layout.load | 851~860ms | 680ms | +25~27% |
| input.ctrlWheel | 338~351ms | 282ms | +20~25% |
| docs.sdi | — | — | -4~-11%(기준 이내) |

- 직전 판본(5c6f73c2)보다 theme·font·layout 이 더 느려져 회귀가 커졌다(gate/Q4 기록).
- 기준과 경과(otest-m-1 전달, 원자료 `poc/ui/logs/otest_m1/android_perf.json`·`android_perf2~4.json`·`gate_Q4_evidence.md`)는 다음과 같다.
  - 공식 기준은 23:10 APK(md5 2aa85531) 절대값이다. Fold5 에서 retest_android.py 로 23:29 에 쟀다. theme.next 479, font 274, ctrlWheel 282, layout.load 680 ms 이고, +10% 한계는 527 / 301 / 310 / 748 ms 다.
  - 바로 다음 23:42 APK(5c6f73c2, 12회×3)에서 이미 theme 648·673·651(+35~41%), font 310·322·323(+13~18%), ctrlWheel 309·316·301(+7~12%), layout 809·837·811(+19~23%) ms 였다. 그래서 theme·layout 의 기준 초과는 23:42 판부터다.
  - 그 뒤 판도 기준 ±10% 안에 든 적이 없다(i0136 theme +78~84%, i0451 +30~37%, i0955 +37~49% — i0955 는 고부하 측정).
  - 23:10·23:42 APK 파일이 남아 있지 않아 다시 잴 수 없다. 대용 기준선은 쓰지 않고, 조용한 창에서 최신 판 절대값만 3세트 재기로 했다(lead 결정).
  - 참고: UI-15 알림 패널 요구 원문 시각은 23:57 이다(§3.1). 그래서 23:10 APK 에는 알림 패널이 없었다고 본다. APK 를 열어 확인한 것은 아니다. 근사 재현 조건에는 `--notify-panel off` 를 넣는다(odev-3 Q4 분해 계획).

Q4 원인 조사 경과(odev-3, `poc/ui/logs/odev3/q4_findings.md`)는 다음과 같다. 판정이 아니라 조사 기록이다.
- **Color 짝 사전 55키 제거**(odev-7c ThemeApplier 0f57176a)는 효과 미확인이다.
  - 기대한 theme.apply.notifications_last −1 이 i0315 에서 확인되지 않았다. 같은 변형 안 전환은 2 그대로이고, 변형 전환은 0 이다.
  - A 조건 theme 은 i0235 682~728ms 에서 i0315 469~496ms 로 약 −30% 줄었다. 하지만 같은 구간에 다른 변경도 들어가 사전 제거 몫을 가를 수 없다.
- **그리드 중복 작업 제거**(GridDocumentView 0e8cf266)는 효과 미확인이다(해는 없음). 셀 Rectangle 측정 무효 수가 341 그대로였다(이 수는 측정 시점의 무효 컨트롤 개수이므로 효과 판정 근거가 아니다 — 아래 정정).
- **staticstroke 가설**: 테마 전환 때 DataGridCell 템플릿의 숨은 Rectangle(FocusVisual 안 2개, InvalidVisualElement)의 Stroke(Fluent 팔레트 DynamicResource)가 바뀌어 셀마다 측정이 무효화된다는 가설이다.
  - 근거로 든 i0315 census who_measure 의 'Rectangle 341 중 318' 은 **해석을 정정한다**(odev-3 확인, `poc/ui/src/Pd.Poc.Shell/Diagnostics/AutomationBridge.Census.cs` 95~110행을 직접 읽어 확인). who_measure 는 무효화 횟수가 아니다. 측정 시점에 `IsMeasureValid=false` 인 컨트롤 개수다. baseline 에서도 같은 수가 나오므로, 셀 Rectangle 318(또는 320)을 '테마 전환이 일으킨 무효화'로 읽을 수 없다. 이 가설의 효과 판정은 theme.next·font 시간과 sync/settled ms 로 한다.
  - 대조: 진단 스위치 `--grid-open staticstroke`(GridDocumentView.CellStrokes.cs 6d7b2caa, Stroke 를 토큰 브러시 인스턴스로 고정)로 다음 판에서 A 대 A+staticstroke 를 잰다. Android 는 otest-m-1(`poc/ui/logs/odev3/q4_next_measure_plan.md`), 데스크톱은 otest-1 이 맡는다. 셀 Rectangle 수, theme·font, 포커스 테두리 색을 본다. 검증 뒤 제품 기본 전환을 검토한다.
- **알림 패널 조건 C**(`--notify-panel off`)는 i0315 에서 적용된 것으로 판단한다. census 상 Panel 73→68, StackPanel 42→35, RepeatButton 48→44 로 요소가 줄었다. Rectangle 증가 338→386 은 전부 DataGridCell 몫(318→366)이다. 'notify_view=new' 는 화면 생성 시점에 기록하던 지표 결함이었고, 다음 판부터 부착 기준으로 기록한다. 그래서 A↔C 의 theme 차이에는 넓어진 문서 영역의 그리드 셀 증가가 섞인다. 알림 패널 몫은 그리드 없는 조건끼리 비교해 가른다.

Q9 ⑤ 원인(odev-3 조사, `poc/ui/logs/odev3/web_idle_raf.md`)은 다음과 같다.
- 제품·진단 번들 모두 유휴 rAF 가 56~60/s 이고, 호출 위치는 전부 `_framework/avalonia.js`(배포 번들 안 경로, 저장소 파일 아님) 다. 그래서 PD 코드(main.js·PerfProbe)는 출처가 아니다.
- Avalonia.Browser 12.1.4 의 `runAnimationFrames` 는 멈추는 분기가 없는 rAF 루프다. BrowserRenderTimer 에는 Stop 경로가 없다. 렌더 루프는 유휴 시 Tick=null 로 멈추지만 rAF 재등록은 계속된다. 대안(업스트림·로컬 타이머·기준 재정의)은 결정 대기다.

Q9 제품 빌드 문자열 검사의 허용 예외(lead 결정)는 다음과 같다.
- 데스크톱 제품 `Pd.Poc.Desktop.dll` 에 로그 문자열 `max-track released (user resize end)` 1건이 남는다. 근거는 `poc/ui/src/Pd.Poc.Desktop/DesktopWindowHost.MinMax.cs` 50행이다.
- 이것은 Q6 #5('모니터 맞춤') 제품 수정의 동작 로그다. 맞춤 때 WM_GETMINMAXINFO 로 최대 추적 크기를 늘렸다가, 사용자가 크기 조절을 끝내면 해제하면서 이 로그를 남긴다. 자동화·원격 제어·계측 같은 진단 기능이 아니다.
- 그래서 Q9 의 '자동화 명령 이름 문자열 0건' 대상에서 제외한다(odev-4b 전달, 2026-10-29).

Q11 예비 의견 정정(otest-m-1, `poc/ui/logs/otest_m1/q11_review.md` §2-1, 00:55)은 다음과 같다.
- Mars 실측 캡처와 대조한 결과 2건이 'Mars 와 일치'로 정정됐다. 하나는 PD 창 띠 두께(좌우 2px·하단 3px)이고, 다른 하나는 MarsClassic 활성 제목줄 높이(26px 실측)다. 예비 의견의 해당 개선 제안 2건은 철회됐다.
- 남은 불일치는 MDI 바탕(#FFFFFF, Mars #778899)이다. gate/Q11 은 2차 판본 재촬영으로 최종 판정한다.

R2R 절차 결함(gate/Q1 기록)은 다음과 같다.
- `bin/R2R/net10.0`(2026-10-29 확인 시 없음 — 이후 정리됨) 의 Shell.dll 은 2026-10-28 23:40:04 구판(1,462,272B)이다. 2차 동결 Work2·Product 는 일반 Release(JIT) 빌드다. 2차 동결 빌드 로그에는 ReadyToRun/publish 흔적이 0건이다.
- R2R 게시 스크립트 `poc/ui/logs/odev4b/publish_r2r.sh`(21:55)는 `-p:PdDiagnostics` 를 지정하지 않는다. 그래서 한 구성만 만든다. 동결 빌드 절차와 매니페스트에도 들어 있지 않다. 결과적으로 Q1 이 요구하는 '시험·제품 R2R' 을 같은 절차로 다시 만들 경로가 없다.

gate 판정 외 추가 실측은 다음과 같다(gate 판정은 해당 시험 축이 따로 쓴다).
- 집중 템플릿의 측면 패널 숨김·복원(`SetSidePanelsHidden`)을 i0215 데스크톱에서 실측했다(odev-6r 집중 ③). 결과는 PASS 다.
  - 핀이 없는 회차(#125)는 숨기기 전후 사각형이 ±2 안이다(Documents 204,100 912×607 · LeftPane 0,100 200×763 · BottomPane 204,711 912×152). 분할선 개수 복귀와 원격 크기 조절도 PASS 다. 앞 회차(#124)에서 이어 재도 누적 오차가 없었다.
  - 선핀 회차(#127)는 핀 항목이 1 → 0(집중 중) → 1 이다. 글자는 '탐색기', 위치는 왼쪽이며, `panel.list` pinned 필드와 트리 판정이 일치했다.
  - i0136 에서는 복귀 크기가 −13% 였다. 숨긴 동안 바뀐 형제 칸 비율을 복원하지 않은 것이 원인이었고, i0215 에서 해소됐다.
  - 증거는 `poc/ui/logs/odev6r/wm_i0215_b.json`(판정 정정 뒤)과 `poc/ui/logs/odev6r/focus_i0136.json`(수정 전)이다.
- Android 알림 패널 재기동 위치 유지(Q16)는 i0235 4회 중 r4 1회만 재기동 직후 Right 였다. b·r2·r3 은 Left PASS 다.
  - r4 는 인자 없이 layout.load 로 같은 파일을 다시 읽으면 Left 였다.
  - 원인은 복원 전 조회 경쟁으로 추정한다. 기동 자동 복원이 끝나기 전에 panel.list 를 읽었다는 것이다. 근거는 세 가지다. layout.save 는 파일 교체가 끝난 뒤 응답한다. 파일에는 Left 가 있었다. i0235 에는 복원 완료 신호(StartupRestoreDone)가 없었다. r4 의 첫 응답이 1039ms 로 다른 회차(513~776ms)보다 느렸던 것도 이 가설과 맞는다.
  - i0315 재시험(NotReady 재시도 포함)으로 확정한다. 증거는 `poc/ui/logs/otest_m1/android_panel_i0235_r4.json` 과 같은 폴더의 b·r2·r3 이다.

### 1.3.1 성능 구간 5 측정값 (2026-10-29 02:47~03:14, odev-4b — 판정은 otest-1 gate 뒤)

이 절은 측정값만 옮긴다. 판정 칸은 otest-1 의 gate 결과가 나올 때까지 '판정 대기' 다. 출처는 `poc/ui/logs/odev4b/result_odev4b.md` §11 과 `poc/ui/logs/odev4b/perf5/perf5_summary.json` 이다. 공통 조건은 화면 안(`--bg-pos 20,20`), 모든 기동 `--fresh`, frame_pump=1 이다. 값은 중앙값(최소~최대, n)이다.

| 항목 | 측정값 | 조건·판본 | 판정 |
|---|---|---|---|
| Q10 그리드 첫 열기(R2R 연속 기동) | 1043ms (756~1115, n=5) | R2R i0136_r2r_diag, 구간 맨 앞 연속 기동 | 판정 대기 |
| Q10 그리드 첫 열기(R2R·JIT 교차) | 442ms (362~472, n=3) | 같은 R2R 판본을 JIT 와 번갈아 기동 | 판정 대기 |
| Q5 20창 move-all p50(그림자 off) | 53.12ms (53.01~53.24, n=5) | i0215. 그림자 on 은 53.68, 알림 패널 off 는 53.21, i0136 은 53.15 | 판정 대기 |
| Q5 20창 move-all p50(제목줄 버튼 min) | 40.13ms (39.96~52.52, n=5) | i0215 그림자 off + 제목줄 버튼 min. 중앙값이 1 vsync 줄어든 유일한 조건이다 | 판정 대기 |
| 기동 startup_ms | R2R 1057ms (986~1064) 대 JIT 2175ms (1822~3143), 각 n=3 | R2R i0136 대 JIT i0215 교차 | 판정 대기 |
| MDI 빈 영역 4점 색 | 4점 모두 #778899(±2), pd·mars 두 프레임 PASS | i0215 pd-light, 그리드2+입력폼2 계단식 | 판정 대기(Q11 심사 대상) |
| Q4 원인 대조 DC(`--notify-panel off`) | 적용 미확인. census baseline 이 DA 2981 보다 12 많은 2993 이었다 | i0215_diag | 판정에 쓰지 않는다 |

- Q10 두 묶음의 차이(1043 대 442)는 CPU 경합으로 추정한다(ctor.seed 근거, 확인 전). 처음 odev-4b 는 디스크·JIT 캐시 상태 차이로 추정했다. 다음 판에서 부하 지표를 붙여 다시 잰다. 판정은 그 재측정과 이 두 묶음을 함께 보고 한다.
- `--fresh` 사용자 파일 보호 결함이 이 구간에서 드러났다. `--fresh` 로 띄웠는데도 사용자 layout.json 이 바뀌었다(640aee92 → 90502b76, 03:12:27).
  - 원인: perf-ui 의 layout.save 단계(`AutomationBridge.PerfUi.cs:72`)가 `--fresh` 를 보지 않는 SaveLayoutAsync 를 불렀다.
  - 조치: odev-2 의 FreshGuardStores 가 `--fresh` 동안 레이아웃·설정·개인 레이아웃 저장소를 메모리로 격리한다.
  - 한계: Android 는 초기화 때 settings.json 을 1회 읽는다. 다만 쓰기는 막힌다.
  - 검증: i0315 판에서 확인한다(대기). 원복은 원본을 가진 odev-6r 에게 요청돼 있다(result_odev4b.md §11).
- move-all p50 은 13.4ms(74Hz) 배수로 양자화된다(40.1 = 3 vsync, 53.1 = 4 vsync). 알림 패널 off 는 p50 을 바꾸지 않았다(odev-4b 관찰).
- DC 적용 확인 수단은 다음 판에서 생긴다. `panel.joinProbe` 응답의 `notify=`·`notifyView=` 와 perf text `notify_view` 다. 참고로 Android i0235 에서는 `--notify-panel off` 적용이 `panel.bounds` NotificationPane=null 로 확인됐다. 그런데도 census 가 A 2913 보다 큰 2955 였다(odev-3 보고). 그래서 census baseline 증감만으로는 이 스위치의 적용 여부를 가릴 수 없다.
- MDI 4점 증거는 `poc/ui/logs/odev7c/mars_compare/mdi4_i0215_pd.png` 와 `poc/ui/logs/odev7c/mars_compare/mdi4_i0215_mars.png` 다.

### 1.3.3 i0451 결과 (웹 otest-w-2, 데스크톱 otest-1 중간 — 2026-10-29 05시대)

웹(i0451 진단 번들, 근거 `poc/ui/logs/otest_w2/web_Q_results.json`)은 다음과 같다.
- Q6 윈도우 메뉴는 PASS 26·N/A 4·FAIL 0 이다. #7·#125 를 포함한다. 근거는 `poc/ui/logs/otest_w2/window_menu_web_i0451.json` 과 `run40.txt` 다.
  - #125(집중 해제 뒤 분할선 끌기)는 i0315·i0352 전체 실행에서 FAIL 이었는데 i0451 에서는 나타나지 않았다. 원인은 확정되지 않았다. 다음 판 split·docDocks 기록으로 가른다.
  - ⑦-2(키운 창의 위치 초기화)에서 1094×649 창이 (40,0), 596 높이 창이 y=53 에 놓였다. 공식 (40,40)/(70,70) 과 다르다. 이 편차에 대한 판정 규칙은 다음과 같이 결정됐다(lead 결정, §3.5 ⑦ 공식 문구·플랫폼 제약 행).
    - 웹은 공식 위치 (xf, yf) 를 구한 뒤 창 전체가 창 레이어(W×H) 안에 들도록 x=min(xf, W−w), y=min(yf, H−h) 로 당기고 최소값은 0 이다. 크기는 바꾸지 않는다. 당긴 뒤 창끼리 좌상단이 서로 달라야 하며, 같으면 FAIL 이다. 다음 판에는 층보다 큰 창 2개 시험을 추가한다.
    - 데스크톱은 공식 그대로 판정한다(당김 없음).
    - 웹 당김은 앱 코드(`SdiResetLayout.Compute`)에는 없는 동작이다. 원본은 Dock.Avalonia.Mdi 의 `ClassicMdiLayoutManager.ClampBounds` 다(odev-2r, Dock 12.1.0.12 디컴파일 보고). 이 함수는 `Arrange`(표시 위치만 당기고 `MdiBounds` 는 바꾸지 않음)·`GetDragBounds`·`GetResizeBounds` 에서 적용된다. `MagnetMdiLayoutManager` 가 이 관리자에 위임하므로 자석 결과도 다시 클램프된다.
- Q12 는 웹 PASS 다. windows.list 의 boundsSource=managed-window(창마다 다른 사각형)이고, SDI 활성화 순서대로 docs.list zIndex 최상단이 바뀐다. 이전 판의 컨테이너 전체 bounds·zIndex 불변 결함이 해소됐다. 근거는 `poc/ui/logs/otest_w2/run37.txt` 와 `retest_web10.json` 이다.
- Q15 ⑥(빠르게 지나쳐 놓기)은 i0451 에 hoverLog 진단이 들어가지 않아 다음 판에서 확정한다. ⑦ 은 PASS 다(dwellMs=103·accepted=False).

데스크톱(i0451, otest-1 중간 보고, `poc/ui/logs/otest/desktop/result_desktop.md`)은 다음과 같다.
- 보기 메뉴는 7항목 중 5개 PASS 이고, 6·7번은 미시험이다.
- Q15 수정은 PASS 다. 무효 지점 3곳에 놓아도 탭이 유지되고 새 최상위 창이 생기지 않았다. 합류와 복원도 정확했다. 다만 i0339 탭 소실의 원인(Float 로 새 창이 생겼다는 추정)은 직접 확정하지 못했다.

새 결함 **D-2(Q7)**: 집중 템플릿에서 계단식으로 바꾼 뒤 활성 문서 본문이 빈 화면이 된다. 세 판에서 공통으로 나타나 회귀는 아니다. 원인은 아래 '원인 확정' 과 같다(H1·H2 는 원인이 아니었다).
- otest-1 판별 결과(i0451, 증거 `poc/ui/logs/otest/desktop/i0451/d2_i0451_diag/`)는 다음과 같다. 탭 배치(tabbed)에서 MDI(계단식)로 바꾼 뒤 활성 문서 하나만 `DocumentControl`(IsVisible=False) 아래에 남고 `MdiDocumentWindow` 로 옮겨지지 않는다. 그 문서의 zIndex 는 None 이다. 다른 문서는 `MdiLayoutPanel`→`MdiDocumentWindow` 아래에서 보인다. 집중(focus) 배치에서 시작해도 경로가 같다(`path_focus_cascade_namedFalse.json`).
- 원인 확정(odev-2r, Dock 12.1.0.12 디컴파일 보고)은 다음과 같다.
  - `DocumentDockControl` 템플릿은 탭용 `DocumentControl` 과 `MdiDocumentControl` 을 같은 Panel 에 두고 IsVisible 만 바꾼다.
  - 둘 다 `DeferredContentControl`+`ControlRecyclingDataTemplate` 를 쓴다. `ControlRecycling.Build` 는 캐시한 뷰를 기존 부모에서 떼어 가져간다. 그런데 표시 준비 조건이 가시성을 보지 않는다.
  - 그래서 탭 모드를 한 번 거친 뒤에는 숨은 탭 presenter 가 활성 문서 뷰를 MDI 창에서 빼앗는다. H1·H2 와는 관계가 없다.
- 수정은 `App.axaml` 에 숨은 쪽 `PART_ContentPresenter` 의 Content 를 x:Null 로 두는 스타일 2개를 넣은 것이다(md5 75c895…). 다음 판 실측으로 검증할 예정이다. 두 번째 셀렉터가 템플릿 경계를 넘어 매칭되는지는 확인하지 못했다.
- 재현 범위는 데스크톱·웹·Android **3플랫폼 공통**이다.
  - 웹 i0451 은 6/6 FAIL 이다(otest-w-2, `poc/ui/logs/otest_w2/retest_web11.json`). form·grid·gantt 활성 각각에서 tpl.focus→tpl.mdi-cascade 와 tpl.tabbed→tpl.mdi-cascade 뒤 활성 문서 본문이 보이지 않았다.
  - Android i0451 은 윈도우 메뉴 #125~#128 의 본문 확인 단계가 FAIL 이다(otest-m-1, `poc/ui/logs/otest_m1/i0451_resume_summary.md`). Grid-7 의 grid.lblCount 가 tpl.focus·tpl.tabbed·tpl.mdi-cascade 뒤 effectivelyVisible=false 였다(bounds 는 0 이 아님). otest-m-1 은 원인 판정을 하지 않았다. 같은 결함으로 묶은 것은 lead 판단이다.

Android(i0451 재기동 뒤 재실행, 2026-10-29 09:34~09:42, Fold5, otest-m-1 `poc/ui/logs/otest_m1/i0451_resume_summary.md`)는 다음과 같다.
- 언어는 PASS 다. 펼침·접힘 모두 한글 잔존 0건이고, 재기동 뒤 en 이 유지됐다(`android_lang_i0451r.json`).
- 알림 패널은 7항목(0~5·7) 모두 PASS 다(`android_notify_i0451r.json`).
- 윈도우 메뉴는 PASS 24·N/A 4·FAIL 4(#125·#126·#127·#128)다(`window_menu_android_i0451r.json`). FAIL 4건은 위 D-2 와 같은 본문 확인 단계다.
- Q4 는 여전히 FAIL 이다. A_ss(staticstroke) 3회 측정 모두 유효했고 theme 498~511ms, font 338~346ms 다. 같은 판 A 조건(theme 500~509ms, font 351~365ms)과 비교하면 staticstroke 효과는 theme 에서 거의 없고 font 에서 약 −4% 다. 단, D-6(staticstroke 미적용) 때문에 이 A_ss 측정은 무효일 수 있다(§1.3.4).

### 1.3.4 i0944 결과 (2026-10-29 09시대 — 지금 확정된 사실만, 다음 판 결과는 받은 뒤 채운다)

**D-6(Q4 진단 스위치 staticstroke 미적용)**
- 데스크톱에서 재현됐다(otest-1, `poc/ui/logs/otest/desktop/i0944/ss_staticstroke.json` 09:54:21). `grid_open_mode=staticstroke` 를 줬는데 적용 확인값 `grid_stable_strokes` 가 0 이다. 대조로 `ss_litecell.json`(09:54:34)은 `grid_lite_cells=160` 이라 litecell 은 적용됐다.
- 원인은 `GridDocumentView.CellStrokes.cs` 의 적용 함수가 생성자에서 `this.TryFindResource("PdBrushSelectionLine")` 로 토큰을 찾는 데 있다. 생성자 시점에는 뷰가 아직 시각 트리에 붙지 않아 앱 리소스까지 올라가지 못하고, 그래서 아무것도 적용하지 않고 false 를 돌려준다.
- 수정판(md5 39b21e13)은 찾지 못하면 `Application.Current` 리소스를 직접 본다. 파일 주석에 이 원인이 적혀 있는 것을 직접 확인했다. 효과는 다음 판에서 실측한다.
- 영향: 이전 A_ss 측정(데스크톱, Android i0451 의 theme 498~511ms·font 338~346ms, §1.3.3)은 staticstroke 가 실제로 꺼진 채 잰 것일 수 있어 **무효일 수 있다**. Android i0451 A_ss 증거(`poc/ui/logs/otest_m1/i0451_ass/`)에서는 `grid_stable_strokes` 값을 찾지 못했다. 그래서 그 측정의 적용 여부는 확인할 수 없다. 당시 'valid=True' 는 `grid_open_mode` 만 본 것이다(`i0451_resume_summary.md`).

**override Pin Run1(odev-4b, `poc/ui/logs/odev4b/pin_i0944/`)**
- 09:53:08 에 `--primary-override -20000,0,2560,1440 --offscreen-guard on` 으로 기동했다. 결과는 `run1 blocked=2 pin=3 fatal=0` 이고, 규칙대로 Run2 는 생략했다(`run.log`).
- blocked 2건은 기동 직후 실제 모니터 위치 (0,100) 로 옮기려던 요청을 guard 가 막은 것이다(`run1_host.txt` 09:53:10.564·10.928).
- pin 3건은 ④ 메인 초기화(2회)와 ⑤ 맞춤 위치를 override 화면 좌표로 고정한 것이다. (0,266)→(-19428,266), (0,0)→(-20007,0) 이다. 그 결과 ④·⑤·⑥ 이 모두 override 화면(x=-20000 대)에서 실행됐다.
- 사용자 화면 노출은 0건이다(odev-4b·lead 보고). 따라서 §6.6 의 ④⑤ 노출은 이 판에서 해소됐다. 화면 캡처로 직접 확인한 것은 아니고 호스트 로그 좌표 기준이다.

**D-2(tabbed/focus → MDI 전환 뒤 활성 문서 본문 소실) 수정 경과 — 3차까지 모두 FAIL. 이후 수정판에서 3플랫폼 종결(데스크톱·웹 i1023b·i1113, Android i1137 — §1.3.6 ①)**
- 1차(스타일, `App.axaml` 75c895 — 숨은 쪽 `PART_ContentPresenter` Content=x:Null 스타일 2개): i0942·i0944 에서 효과가 없었다. 셀렉터가 템플릿 안 부품에 매칭되지 않은 것으로 추정한다(`poc/ui/logs/otest/desktop/retest3_checklist.md` 'D-2 다음 수정판 시험').
- 2차(코드 가드, `Docking/HiddenPresenterGuard.cs` 426b2d — 지금은 삭제됨): i0955 에서도 재현됐다. 탭 → 계단식 단계가 ok=false 이고, 문서를 활성화할 때마다 MDI 창 밖 문서가 1개 남았다(`poc/ui/logs/otest/desktop/i0955/d2_fix.json`).
- 3차(예비안 B, `Themes/DocumentDockMode.axaml` fce033 + `Docking/LayoutModeGate.cs` 52de2a): i1006 데스크톱 트리 검사에서는 계단식 10지점 모두 문서 3개가 보였다. zIndex 도 전부 유효했고, MDI 모드에서 숨은 `DocumentControl` 이 사라졌다(`poc/ui/logs/otest/desktop/i1006/d2_b.json`). 그래서 처음에는 PASS 로 보고됐다. 그러나 **철회했고 FAIL 이다.** 같은 판에서 데스크톱·웹 모두 측면 패널(탐색기·로그)과 탭·집중 모드 문서 영역이 그려지지 않는 회귀 **D-11** 이 확인됐다. 정상인 것은 MDI 뿐이다(증거 `poc/ui/logs/otest/desktop/i1006/bk_i1006_diag_*.png`, `poc/ui/logs/otest_w2/i1006/px/`, lead 정정).
- D-11 원인 후보로 고아 알림 화면이 추적됐다. 알림 패널 템플릿이 Content=null 에도 일치해 문서 영역 래퍼·콤보 선택 상자에서 알림 화면이 생긴 것이다. odev-3 이 `NotificationPanelView.axaml.cs` 991c37b6 에서 일치 조건을 더했다. 효과는 다음 판에서 확인한다.
- **판정 방법 교훈:** 시각 트리의 effectivelyVisible·zIndex 만으로는 렌더 누락을 잡지 못한다. 3차의 오판이 그 사례다. 이후 D-2 와 UI 판정에는 **영역 잘라내기 화소 검사**를 의무로 한다. 기준판과 대조하고, 빈칸 기준은 고유색 수 ≤4 또는 표준편차 <6 이다(`poc/ui/logs/otest/desktop/i1006/blank_chk.py`). 화면은 PrintWindow 와 내부 스크린샷 둘 다로 본다(team-lead 10:2x).

**D-5(웹 SDI 관리형 창 위치·크기 복원)·⑦ — i1006 웹 PASS**(otest-w-2, `poc/ui/logs/otest_w2/i1006/retest_web12.json`·`run12.log`)
- D-5 는 세 경로 모두 PASS 다. 자동 슬롯 저장 뒤 새로고침 기동 복원(A1), 새로고침 뒤 layout.load(A2), 개인 레이아웃 저장 뒤 새 페이지에서 이름으로 복원(B)에서 위치·크기가 그대로였다. 수정은 odev-2r(LayoutSerializer.cs 12f417·ManagedWindowHostCommands.cs cff67c·WindowCommands.Sdi.cs a8c1bd)이 했다.
- 자동 저장도 PASS 다. ⑦ 초기화 뒤 자동 슬롯이 초기화 위치를 담고(C1), 수동 저장 없이 새로고침해도 초기화 위치가 보였다(C2).
- ⑦(Q6 #7)은 웹 ClampBounds 규칙 공식 (40,40)/(70,70)·겹침 없음·크기 불변·⑦-2·⑦-5 가 모두 PASS 다.
- 단서: 같은 파일의 사전 단계 '사용자 경로로 위치·크기 변경' 1건은 '판정불가(변경 실패)' 로 기록돼 있다. 위 PASS 는 그 단계 이후 경로의 판정이다.

### 1.3.5 M4(Fluent 동적 브러시 동기화) 판정 근거 정정 (2026-10-29, odev-7c 보고·lead 지시)

- **정정:** M4 census 의 바인딩 판별식(`unbound_check`·`table_bound`)은 처음부터 근거가 될 수 없었다. Avalonia 12.1.4 의 `GetDiagnostic` 이 Diagnostic 문자열을 항상 null 로 돌려줘서(ValueStore.GetDiagnostic 디컴파일 확인 — odev-7c) 판별식이 늘 0 만 냈다. 그래서 '`unbound_check=0`' 은 '바인딩이 모두 살아 있다' 는 뜻이 아니었다.
- **조치:** 판별식을 지우고 census 출력에 `bound_check=unsupported` 를 내도록 바꿨다(`Theming/ThemeApplier.Diag.cs` md5 024b24c9 — 소스 75행 주석·122행 출력 확인).
- **M4 PASS 의 근거(바로잡음)** 는 다음 두 가지다.
  - (a) 디컴파일 정적 대조: Fluent 테마의 동적 참조 85개 = 동기화 표 41 + 44(`Theming/ThemeApplier.Fluent.cs` 9807fbf0 계열).
  - (b) 화소 비교: `--fluent-palette on/always` 3테마와 컨트롤 갤러리 18쌍의 차이가 0 이다.
- **미확인으로 남김:** 동기화가 쓰는 `SetValue` 가 기존 바인딩을 대체(끊음)하는지는 확인하지 못했다. 판별 수단이 없으므로 위 (a)(b) 를 넘는 결론은 내리지 않는다.
- 이 보고서에는 앞서 `unbound_check` 를 근거로 쓴 문장이 없었다(검색 0건). 근거 원문은 otest 체크리스트 'M4 시각 판정 절차'(`poc/ui/logs/otest/desktop/retest3_checklist.md`)에 있다.

### 1.3.6 2026-10-29 오후 결과 종합 (i1023b·i1113·p1121·i1137)

출처 표기: 증거 파일이 있는 것은 경로를 적었다. 증거 파일을 찾지 못하고 lead 전달(2026-10-29)만 있는 것은 '(lead 전달)' 로 적었다. 추정은 추정이라고 적었다.

| # | 항목 | 판정 | 근거 |
|---|---|---|---|
| ① | D-2(탭/집중→MDI 활성 문서 본문 소실) | **3플랫폼 종결 — 데스크톱·웹 PASS(i1023b·i1113), Android PASS(i1137)**. Android 는 i0451 FAIL 4건(윈도우 메뉴 #125~128 본문 확인)이 i1137 재시험에서 모두 해소됐다(PASS 28·N/A 4·FAIL 0) | 데스크톱: i1023b_diag(Shell 2f8006ce, 10:31~10:32) `poc/ui/logs/otest/desktop/i1023b/f_i1023b_diag.json`·`.out`·`f_i1023b_diag_*_{pw,in}.png`(6상태 4영역 화소 검사 + 같은 뷰, 스크립트 `otest/desktop/d11_full.py`), i1113_diag(Shell 39265df3, 11:20~11:22, firstFull 기준 재판정) `otest/desktop/i1113/f_i1113_diag.json`·`.out`·`f_*_{pw,in}.png`(otest-1). 웹: i1023b(10:31~10:35) `poc/ui/logs/otest_w2/i1023b/retest_web11.json`·`run11.log`(단계별 pixel.colors form 368·grid 265·gantt 219 — 전 단계 10색 이상, 판정 행 PASS 11·기록 2 직접 확인), 탭 모드 캡처 `i1023b/d2_grid_tabbed-mdi-cascade_1_tabbed.png`, i1113(11:22~11:27) `otest_w2/i1113/retest_web11.json`·`run11.log`(otest-w-2). 비고: i1006 의 '보강 PASS' 는 측면 패널 빈칸을 놓친 오판이었고 FAIL 로 정정했다(`poc/ui/logs/otest/desktop/retest3_checklist.md` 정정 기록, §1.3.4 3차). Android PASS `poc/ui/logs/otest_m1/i1137/d2/window_menu_android_i1137.json`(summary PASS 28·N/A 4, #125~128 PASS 직접 확인)·`window_menu_run.log`·`d2_focus_then_cascade.png`·`d2_tabbed_focus_cascade.png`. 이전 FAIL 근거 `poc/ui/logs/otest_m1/window_menu_android_i0451r.json`. 수정 경과는 §1.3.4 |
| ①-1 | D-11(측면 패널·탭/집중 문서 영역 미표시 회귀) | **3플랫폼 PASS** | 데스크톱: ①과 같은 파일(i1023b·i1113 f_*_diag). 회귀·대조 기준은 FAIL 판 i1006 `otest/desktop/i1006/bk_i1006_diag_*.png`·`.json`, 대조 i0944 `bk_i0944_diag_*`, 내부 캡처 대조 `dd_i1006_diag.json`·`dd_*_{pw,internal}.png`(i1006 '보강 PASS' 는 오판으로 FAIL 정정 — checklist). Android: `poc/ui/logs/otest_m1/i1023b/fcheck_result.json`(조건 A notify_view_instances=1/1, C_nogrid=0/0, render_kind=gpu-egl)·`fcheck_A_tabbed.png`·`fcheck_C_nogrid_tabbed.png`(탭 모드 화소·육안), i1113 `poc/ui/logs/otest_m1/i1113/func/A5_tabbed_one_doc.png`·`func_result.json`. 웹: ①과 같은 파일. **단, 웹의 측면 패널(탐색기·로그) 영역은 자동 화소 검사 항목이 아니다 — 캡처를 눈으로 확인했고, 자동 판정은 활성 문서 본문 영역만 했다**(otest-w-2). 웹 FAIL 대조판 i1006(10:08~10:18) `otest_w2/i1006/px/retest_web11.json`·`blank_probe.log`·`blank_*.png`(탭·집중 단계 1~3색, 측면 패널 빈칸). 판정 방법은 PrintWindow + 내부 screenshot 화소 검사(`retest3_checklist.md` 'D-2 판정 필수' 절) |
| ② | Q9 ⑤ 유휴 rAF | **제품판 PASS(패치판)** — p1121 에서 Avalonia 타이머 출처 유휴 rAF 60.1·60.1·57.5/s → 0/s, 30초 유휴 뒤 기능 회귀 없음 | `poc/ui/logs/odev3/web_idle_raf_patch_step2.md` 결과 절, `poc/ui/logs/otest_w2/p1121/retest_web16_idleraf.json`. 결정은 ADR-044(PoC 웹 기본 패치판, 제품 채택은 사용자 확인 대기 — 당시 서술. 그 뒤 결정(P87)로 제품 채택이 확정됐다, `plan/90_결정관리/02_결정기록_ADR.md` 색인 ADR-044 행) |
| ③ | M4(Fluent 동적 브러시 동기화) | **PASS** — 근거는 디컴파일 정적 대조(85 = 41+44)와 화소 비교(차이 0) | §1.3.5(unbound_check 근거 무효 정정) |
| ④ | Q11 Mars 디자인 대조 | i1137 재측정에서 **A1·A2·A7·A8 PASS**. A7 활성 탭 #40568D·흰 글자, A8 Windows 스타일 ON #FF4500·비활성 흐림 #DFDFDF·캐시 0% #C0C0C0, MarsClassic 전부 PASS | `poc/ui/logs/odev7c/q11_design_review.md` ②-1(61행), `retest3_checklist.md` 'Q11 A8 기대값 최종'. 나머지 A 항목은 그 문서 ③ 목록을 따른다 |
| ④-1 | Q11 의도적 차이(수정 대상 아님) | 글꼴·크기(Mars 굴림 9pt ↔ PD Noto Sans KR 14px 계열), 크기 조절 손잡이(Mars #C0C0C0 4×4 표시 ↔ PD 투명), Windows 스타일 흰 글리프(OFF #FFFFFF) | `q11_design_review.md` ① 대조표 5·14·26행과 ②-1 |
| ⑤ | Q4 테마·글꼴 전환 성능 회귀 | **Android 조용한 창 재측정(i1505): theme·ctrlWheel PASS, font 는 '회귀 아님(새 기능 비용)' — 최적화 후속 후보.** 데스크톱 Q4 빈칸은 앱 확대 1.25·2.0 PASS, 배율 2.25 미검증. 수치·근거는 §1.3.7 끝 '조용한 창 재측정 결과' 절. 이하 경위(당시 판정 '미종결(수정 진행 중)'): 2b 결과로 회귀 주원인이 Android 알림 패널 틀 때문에 그리드 폭이 1.1dp 진동하는 것으로 좁혀졌다. 계측 계획은 `poc/ui/logs/odev3/q4_regression_plan.md` '단계 2b' 다. 수정 뒤 조용한 창 재측정으로 판정한다 | 2b 원자료 `poc/ui/logs/otest_m1/i1113/q4_2b/`(q4r1_A_*.json 18개 + q4r1_summary.json, 11:49 종료, 부하 loadavg 6.9~10 기록됨). '1.1dp 진동' 근거 필드(otest-m-1 판독, 직접 확인): `q4r1_<조건>_<n>.json` 의 `per_transition.theme.lastsize.grid_measure_lastsize` = "516.667x383.111->517.778x383.111"(A·A_empty·A_legacy), `sizechg_delta.grid_measure_sizechg` = 60~80(A_off 는 0), `ops."grid.measure".count` = 80(A_off 는 null). 원인은 알림 패널 '내용' 이 아니라 분할 구조(Dock ProportionalStackPanel) 쪽으로 좁혀졌다(odev-3 공유). 수정은 odev-3 담당이고 방안은 lead 보고 뒤 정한다 |
| ⑥ | UI-18 모바일 폼팩터(ADR-041) | **웹·Android 모의 PASS, 실제 두 손가락 핀치는 사람 확인 필요.** 웹: 핀치 모의(pinch.sim) 6/6 PASS(확대 +3·축소 -2·문서 밖 거부 등), 핀치 CDP 시험 **11/11 PASS**(P0·P3, FontSize P1·P2·P5·P6, WindowZoom P1·P2·P4·P5·P6 — 앞서 '14/14' 는 otest-w-2 가 11/11 로 정정. 원본판 i1113·p1121 은 WindowZoom P6 FAIL 로 10/11). 이 핀치 시험은 데스크톱 레이아웃(1280×800, hasTouch)에서 했다. **웹 '태블릿/폰 모드' 전환(ui.mode) 재시험(retest_web20, 판정 3행)은 PASS 2·FAIL 1 이다.** M1(좁은 화면 400: 폰 고정·태블릿 비활성) PASS, M3(넓은 화면 태블릿↔폰 전환과 레이아웃 변화) PASS, **M4(전환 뒤 문서 상태 유지) FAIL** — 폰 모드가 새 그리드 뷰 인스턴스를 만들어 원래 뷰는 숨고 선택·스크롤이 유지되지 않는다(태블릿 복귀 때는 원래 뷰로 돌아옴). odev-2r 원인 조사 중. M2(넓은 화면 auto 기본 상태)는 판정 없이 기록만 했다. **갱신(i1313, F1 판): M4 PASS** — retest_web20 M1·M3·M4·M5 PASS, M2 기록(`poc/ui/logs/otest_w2/i1313/retest_web20_uimode.json` 직접 확인, 원인과 수정은 §1.3.9). Fold5: 재판정 PASS(핀치 모의·좁은 머리줄·B3·A4). 측면 패널 단일 탭 숨김 반영. 비고: Dock ▾ 메뉴 문자열이 번역되지 않는 것이 새로 관찰됐다(UI-16 결함 후보, odev-7c 수정 중) | 웹 `poc/ui/logs/otest_w2/p1137/pinch_sim.json`(6건 PASS 확인), Fold5 `poc/ui/logs/otest_m1/i1137/recheck/recheck_result.json`(C·H·A3·A4·B3 항목, 판정은 lead 전달). 웹 핀치 CDP `poc/ui/logs/otest_w2/p1137/retest_web14.json`·`run14.log`, P6 탐침 `pinch_p6_probe.log`·`pinch_p6b_probe.log`(p1137 11:49~11:50, otest-w-2). 웹 모드 전환 `poc/ui/logs/otest_w2/p1137/retest_web20_uimode.json`(M1~M4 판정 직접 확인)·`run20.log`·`uimode_probe.log`·`uimode_*.png`(6장) |
| ⑦ | 화면 밖 시험 안전 | i1023b 에서 메뉴 팝업 화면 노출 사고가 있었다(10:3x~10:40, 6회×1~2초, 좌표 ko (2357,32)-(2560,377)·en (2315,32)-(2560,377), 포커스 탈취 0). 대책은 진단판 OverlayPopups(--bg-pos 일 때 팝업을 창 안 오버레이로)이고, 확인 결과 메뉴를 연 직후 **새 최상위 창 0개**다 | `retest3_checklist.md` '팝업 화면 노출 사고'·'팝업 시험 허용 절차' 절, 결과(직접 확인): 제품 R2R 판 i1104_r2r_product(11:09)에서 UIA 로 파일 메뉴(shell.mnuFile)를 펼친 뒤 같은 프로세스의 보이는 최상위 창 수가 그대로(`NEW_WINDOWS 0`) — `poc/ui/logs/otest/desktop/i1104/expand_check.out`, 호스트 로그 `poc/ui/logs/desktop_20261029_110918_503.log` 5행 'bg-window-policy background=True overlay-popups=True'(기동 출력 `i1104/launch.out`). 진단판 기준의 별도 측정(frame_bounds·popup COUNT)은 없다(odev-4b). 시스템 창도 비전경 모드(ADR-042·시스템 메뉴 BackgroundMode). **추가 사고(i1239_diag, 2회):** 데스크톱 SDI 시험 중 SdiWindowHost 가 표시 직후 주 모니터 안 (1904,40) 1920×1023 에 놓였다가 offscreen-window 감시기가 (4680,40)으로 옮겼다. 사전 요청은 (204,100)→(4720,40) 이었다. 1회차는 12:42:28.490 요청 → .517 이동(27ms), 2회차는 12:43:46.946 요청 → .969 이동(23ms)이다. 실제로 그려졌는지는 확인하지 못했고, 최대 수십 ms 노출 가능성이 있다. 원인은 사전 화면 밖 수정(d8ade10c)이 표시 이후 재배치를 막지 못한 것이다. 처리: 데스크톱 SDI 시험 재보류, odev-4b 원인 수정 중. 1회 시험 허용은 team-lead 결정이었다. 추가 사고 증거: `poc/ui/logs/otest/desktop/i1239/`(sdi_once.json·sdi_hash.json·launch_*.out), 호스트 로그 `poc/ui/logs/desktop_20261029_124149_466.log` 58~59행(1회차)·`poc/ui/logs/desktop_20261029_124332_756.log` 60~61행(2회차) — 시각·좌표 직접 확인. **기동 노출 가능성(가능성, 미확정, 2026-10-29):** bg 기동 중 메인 창에 OS 의 SetWindowPlacement 화면 보정이 걸려 창이 (1144,0,1416x908), 즉 주 모니터 오른쪽 끝으로 옮겨졌다가 Opened 처리기에서 화면 밖으로 되돌려지는 경로가 있다(odev-7c 코드 조사). 표시 상태에서 일어났는지는 확인하지 못했다. 두 번째 요청은 Opened 94ms 전이다(odev-7c 전달). BgWindowPolicy 는 메인 창을 감시에서 제외하므로, 지금까지의 'moved 0' 기록은 메인 창에 대한 증거가 되지 못한다. 가드 차단 기록(직접 확인): i1402 14:14:47.933·14:14:49.696, i1350 14:13:53.392·14:13:54.207 에 'offscreen-guard blocked req=(1144,0,1416x908) flags=0x14' 가 각 2회씩 있다. 두 판 모두 그 뒤 (-8,-8,2576x1456) flags=0x8020 요청도 1회 막혔다. 조치(lead 결정): 안 A(bg 모드 기동 구간 동안 모니터와 겹치는 이동 차단)를 구현하고 WM_WINDOWPOSCHANGED 확인 기록을 넣는다. 담당은 odev-7c 다. **규모(otest-1 집계, slot_runs.log 로 직접 재집계):** ① 오늘(10-29) otest-1 데스크톱 기동은 02:31:33~14:37:47 사이 234회다. 이 중 --offscreen-guard on 은 2회(14:13:45·14:14:37)뿐이고, 232회는 가드 없이 기동했다. ② 12:52 이후 가드 없는 기동은 40회다. 경계 줄 12:52:21 을 넣으면 41회이고, 12:52:21 다음부터 세면 40회다. 여기에는 14:24:37 i1350bx·14:37:47 i1350bp 벤치 기동이 들어 있다. ③ 메인 창이 실제로 보였는지는 판단할 수 없다(가능성, 미확정). 'moved 0' 은 메인 창 근거가 되지 못한다. 기동 직후 1회 열거에서는 모두 (4680,0) 화면 밖이었다(otest-1 전달). ④ 다른 팀원 기동도 같은 경로를 탔을 수 있으나 노출 관점으로 집계하지 않았다. 같은 로그에 odev-4b 83회·odev-6r 9회(이 중 가드 on 3회)가 있다. 근거는 `poc/ui/logs/odev4b/slot_runs.log`(owner·args 기준 집계)다. **안 A 검증: PASS(i1441).** i1441_diag 를 가드 없이 화면 밖 auto 로 3회 기동했고 3회 모두 PASS 다. 회차마다 main poschanged 5건(중복 줄 제외) 중 onscreen=1 은 0건이다. main bg pin 이 '(1144,0,1416x908)->(4680,0) flags=0x14' 를 1회씩 막았고, 'main bg pin end' 는 3/3 이다. show=1·visible=1 시점의 rect 는 모두 (4680,0,1416x908) 화면 밖이다(lines_r1~3.txt 직접 확인). 결론: OS 보정에 의한 기동 중 화면 안 배치는 i1441 부터 막혔다(i1446 포함은 lead 전달). 그 이전 판의 가드 없는 기동(321회, slot_runs.log 집계: otest-1 232 + odev-4b 80 + odev-6r 9)에서 노출이 있었는지는 여전히 미확정이다. 운영 규칙: 앞으로도 데스크톱 기동에는 --offscreen-guard on 을 함께 쓴다(이중 안전). 근거: `poc/ui/logs/otest/desktop/i1441/summary.txt`·`lines_r1.txt`~`lines_r3.txt`. 근거: `poc/ui/logs/otest/desktop/i1402/summary.txt`(가드 차단 줄 — lead 가 적은 f_i1402_diag.out 에는 이 줄이 없고 summary.txt 에 있다), `poc/ui/src/Pd.Poc.Desktop/DesktopWindowHost.OffscreenGuard.cs` 머리 주석(SetWindowPlacement 화면 안 보정 설명) |
| ⑧ | Q10 그리드 첫 열기 | **PASS(조용한 창, otest-1)** — 제품판 i1505_product doc.open grid 유효 5회(load<2) 532·567·587·786·816ms, 중앙값 **587ms**(기준 ≤700ms). 단 회차 직전 Windows CPU 32% 이상인 회차는 약 0.8초로 따로 묶인다. 근거 `poc/ui/logs/otest/desktop/quiet_i1505/result_quiet_i1505.md` 10행(직접 확인), 원자료 `poc/ui/logs/odev4b/q10/q10_i1505_product_*`. 이하 경위(당시 판정 '판정 보류'): 데스크톱 제품판 i1104_r2r_product(Shell b2436180) 11:10~11:12 5회, doc.open grid 542·571·459·590·355ms → 중앙값 542ms(기준 ≤700ms). load 6~11 이라 참고값이다. UIA count_ms 는 1078·915·778·1017·627(외부 관측 참고). 조용한 창 재측정 대기 | 원자료 `poc/ui/logs/odev4b/q10/q10_i1104_r2r_product_111026/pdlog_doc_open.txt`(5줄 직접 확인)·`results.csv`·`run.log`, 회차별 부하 `poc/ui/logs/otest/desktop/i1104/q10_load.txt`. 판정 기준 `retest3_checklist.md` 'Q10 판정 기준' |
| ⑨ | D-12 | **재현 불가 수준, 관찰 유지([d12] 진단 로그 상시).** i1113: 30회×2 = 60회 미재현(모든 회차 lost 없음). i1137 고부하: base 30회·fontorder 30회 = 60회 미재현(lost 없음). 이 중 부하 15 이상을 만족한 회차는 base 10회이고, 나머지 50회(base 20·fontorder 30)는 15 미만이었다. 누적 재현은 121회 중 1회(i1023b 2번째 세트)다(누적 수치는 lead 전달) | i1137 `poc/ui/logs/otest_m1/i1137/d12hl/d12_base.json`·`d12_fontorder.json`(각 rounds 30, load.below15 집계 직접 확인)·`base_run.log`·`fontorder_run.log`·`d12_*_logtail.json`. i1113 `poc/ui/logs/otest_m1/i1113/d12/d12_base.json`(rounds 30)·`d12_sdi_then_save.json`(rounds 30) — 직접 확인. 실행 로그 `poc/ui/logs/otest_m1/i1113_d12_base_run.log`·`i1113_d12_sdi_run.log`, 로그 꼬리 `d12_*_logtail.json`. 합계 집계 파일은 따로 없다 |
| ⑩ | i1201 데스크톱 회귀 | **PASS.** 진단판(b18bef72): D-11·D-2 6단계(start·tabbed·focus·cascade·tabwalk_cascade·activate_x3) 모두 ok, 빈 영역 0·같은 뷰 유지. 시스템 메뉴 26항목(ko·en) canExecute=false 0건, sys.open About 은 화면 밖 좌표(onscreen=false)에 열림, 메뉴 펼침 새 창 0. ko→en→ko 문구 수 263·263·263, en 에 남은 한글은 그리드 데이터뿐. 제품판(59875604): 기동·정상 종료, UIA 첫 그리드 열기 OK(제품 로그 doc.open grid 1354ms 는 부하 중 참고값), Q9 진단 문자열 51개 실행 로그 hits 0(otest-1 전달). 12:08~12:14 KST | `poc/ui/logs/otest/desktop/i1201/reg/` — `f_i1201_diag.json`·`.out`·`f_i1201_diag_*_{pw,in}.png`(12장), `sys_check.json`(26항목·창 좌표 직접 확인), `lang_scan.json`(counts 직접 확인)·`lang_*.png`, `prod_open.out`·`prod_uia.out`·`launch_prod*.out` |
| ⑪ | 헤드리스 고DPR(2.625) 웹 시험 | **조건 오류 확정·종결 — 실제 장치(Fold5 Chrome)와 헤드리스 force 조건 모두 앱 정상.** 헤드리스 force 조건(--force-device-scale-factor=2.625 + device_scale_factor=2.625, i1300 웹 진단판)에서 devicePixelRatio 2.625·innerWidth 800·ui.mode viewWidth 800·isCompact false 였다(값 직접 확인, 실행 인자는 파일에 없어 otest-w-2·odev-3 전달). Fold5 실제 Chrome, i1300 웹 진단판에서 innerWidth 805·devicePixelRatio 2.25·visualViewport 805.33(scale 1)일 때 앱 ui.mode viewWidth 805.3·isCompact false(일반 모드)였다(직접 확인). 실제 장치에서는 앱 폭 = CSS 폭이므로 헤드리스의 304.8 은 dpr 에뮬레이션 산물이다. 이하 경위. 헤드리스 Chromium 에 device_scale_factor=2.625 만 주면 ResizeObserver devicePixelContentBoxSize(dpcb)가 장치 px(2100)가 아니라 CSS px(800)로 나온다(브라우저 값 실측). --force-device-scale-factor=2.625 를 함께 주면 dpcb 2100 으로 실제 장치와 같은 조건이 된다(실측). 앱(i1222)에서 otest-w-2 가 잰 ui.mode viewWidth 는 304.8 이고 폰 모드로 떴다. 304.8 = 800(dpcb)/2.625 로 연결한 것은 Avalonia.Browser 가 ClientSize 를 픽셀 크기 ÷ dpr 로 계산하는 코드(`vendor/avalonia-12.1.4/src/Browser/Avalonia.Browser/webapp/modules/avalonia/rendering/resizeHandler.ts` 30~56행, `vendor/avalonia-12.1.4/src/Browser/Avalonia.Browser/Rendering/BrowserSurface.cs` 59~64행)에 근거한 추론이다(odev-3). 그래서 앱 결함이 아니라 시험 환경 산물로 판단한다. force 조건 앱 viewWidth 는 i1300 에서 800 으로 확인됐다(위). 이 보고서 본문에는 'dpr 2.625' 조건으로 낸 판정이 없다(2026-10-29 '2.625'·'dpr' 검색 0건). 따라서 앞으로 이 조건의 결과(웹 census dpr 재측정 수, Q4 웹 dpr 판정 등)는 force 인자를 함께 준 재측정으로만 싣는다. 앞으로 헤드리스 고DPR 시험은 force 인자를 함께 준다 | 헤드리스 force `poc/ui/logs/otest_w2/i1300/hscrollD_dpr2_625.json`(devicePixelRatio·innerWidth·ui.mode 직접 확인). Fold5 `poc/ui/logs/otest_m1/i1300/web/cdp_viewwidth.json`(04:09:48Z=13:09:48 KST, innerWidth·devicePixelRatio·visualViewport·ui.mode 직접 확인, Chrome 156 은 odev-3 전달 — 파일에 버전 없음). 브라우저 프로브 `poc/ui/logs/odev3/dpr_probe/dpr_probe_result.txt`(13:05 KST, 빈 페이지 브라우저 값 — 2행 dpr 1·dpcb 800 정상, 3행 dpr 2.625·dpcb 800 오류 조건, 5행 force+2.625·dpcb 2100 정상, 직접 확인), 스크립트 `dpr_probe.py`·`dpr_probe_force.py`(같은 폴더). 앱 viewWidth 304.8 은 otest-w-2 i1222 실측(lead·odev-3 전달) |

