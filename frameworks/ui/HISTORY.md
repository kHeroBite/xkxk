# HISTORY — 별별UI 작업 이력

> 별별UI(frameworks/ui) 작업 이력만 적는다. 규칙은 여기 두지 않는다. 최신 항목을 위에 둔다.
> 2026-11-24 에 Pd 저장소(`Pd:HISTORY.md`, 기준 커밋 157403c)에서 UI 관련 항목만 원순서로 옮겨 만들었다. 추출 기준은 본문에 `poc/ui`·`UI PoC`·`09_화면설계` 중 하나가 있는 `## ` 항목이다.
> 옮긴 항목 안의 경로는 Pd 저장소 기준이다(`poc/ui/` 는 이 저장소의 `frameworks/ui/` 에 해당한다).

## 2026-11-24 — 표시 이름 'PD UI PoC'·'PD' → '별별UI'(O2, conv_179552721972)
- 창 제목(`src/xkxk.frameworks.ui.shell/App.cs`), 트레이 툴팁(`src/xkxk.frameworks.ui.views/PdApp.axaml`), 브라우저 탭·웹 manifest(name·short_name), Android 라벨 4곳(AndroidApp.cs·MainActivity.cs·AndroidManifest.xml·csproj ApplicationTitle), 언어 리소스 4개(shell.{ko,en}.json `app.title`, sysview.{ko,en}.json `sysview.about.product`)를 '별별UI' 로 바꿨다. 번역명은 만들지 않았다.
- `com.pd.poc`·`PdPoc` 데이터 폴더·단일 인스턴스 이름·AUMID·머리 주석은 그대로 두었다.
- 빌드 3종 rc=0·오류 0개. 브라우저 산출물 `<title>` 과 Fold5 설치 APK 라벨(aapt) '별별UI' 확인. 데스크톱 실기동 창 제목은 사용자 앱 실행 중이라 미확인(재실행 시 확인).
- 시험 중 `tools/check_layers.py` rc=1(기존 300줄 초과 5건)을 발견해 다음 사이클로 넘겼다.

## 2026-11-24 — 프로젝트·어셈블리 이름 전환(Pd.Poc.* → xkxk.frameworks.ui.*, O3, conv_179552317865)
- 커밋 f93767a. 프로젝트·어셈블리·네임스페이스·실행 파일 이름을 폴더 경로 기준 `xkxk.frameworks.ui.*` 로 바꿨다(UTODO-0007 완료, UADR-0002, AC-019). 이름 대응표와 함정은 `/mnt/c/DATA/Project/xkxk/frameworks/ui/plan/04_기술스택/02_소스구조_분할규칙.md` 에 반영했다.
- 이름 변경 뒤 조용히 꺼지는 함정 3곳(선택적 연결 `?.`, 접두 검색, 접두 충돌)을 막았고, 검증자가 추가 함정 1건을 더 찾았다.
- 시험 중 Windows `tasklist` 기본 출력이 이미지 이름을 25자로 자른다는 회귀를 발견해 CSV 출력 완전 일치 비교로 고쳤다.
- 빌드 3종(데스크톱·브라우저·Android)과 웹·Fold5·데스크톱 스모크가 통과했다(RETEST PASS).
- `tools/run_desktop_bg.sh` 는 날짜별 로그 대조에서 8자리 날짜 파일만 제외하고, 옛 이름 프로세스는 건너뛰며, rc=11 판정에만 쓰도록 했다.
- 사용자 결정 Q1 로 pending 을 재기준화했다. 작업 표시줄 바로 가기는 새 실행 파일 이름으로 다시 고정해야 한다.
- 유지한 것은 Android 패키지 `com.pd.poc`(UTODO-0013)와 사용자 데이터 폴더·단일 인스턴스 이름(UTODO-0014)이다. 새로 등재한 과제는 UTODO-0015(문서 탭 접근성 이름에 타입명 노출)·UTODO-0016(`.log.rc` 이름 불일치)이다.

## 2026-11-24 — 이전 후속 정리(문서 인수·경로 고정 해소·판정기 보정·새 홈 재측정, conv_179551233102)
- B1 문서(커밋 fed7b89·384c502). UTODO-0002~0005 를 이관하고 사용자 파일 대조 보고서를 등재했으며, Pd 의 열린 UI 과제를 발췌해 UTODO-0006~0011 로 올렸다.
- B2 경로 고정(커밋 11e47c3). `test`·`tools` 의 Pd 경로 고정 70개를 새 홈 기준으로 고치고 `tools/run_desktop_bg.sh` 사용자 파일 대조 빈틈 3건을 보강했다(검출을 늘리는 방향만). 이제 남은 옛 경로는 동작 줄이 아닌 2건이다. 처리 결과는 `/mnt/c/DATA/Project/xkxk/frameworks/ui/plan/90_결정관리/이전기록_2026-11-24_경로고정목록.md` §5 에 있다.
- B3 판정기(커밋 c34b15f). AC-012 판정기에 어두운 테마의 낮은 색차 보정을 더했다. 문턱 0.05 는 바꾸지 않았다. 판정기 자체 시험 selftest_judge 11/11·motion_effects_judge 16/16 이다.
- B3 재측정(새 홈 Release 빌드, PC 비움, 실제 ms 판정).
  - AC-031 FAIL. medium PERF-13 p95 28.67·26.97·32.20ms(3/3 FAIL), medium PERF-12 비 1.078·1.193·0.961(기준 1.15, PASS·FAIL·PASS 흔들림), PERF-02 는 합성 이동만 쟀다(DEFERRED).
  - AC-012 PASS(조건부 근거). 어두운 테마 changed 0.1721(낮은 색차 12 보정), 기본 색차 30 은 0.0489, 정지 화면 잡음 0.0, 멈춘 화면에서 창이 실제로 잘린 것을 육안 확인했다. 밝은 테마 대조는 하지 못했다.
  - AC-010 웹 닫기. 1회차는 overlap.left1.button 1건이 관찰 시간 안에 끝 모습이 되지 않아 INVALID, 2회차는 59건 전부 PASS 다.
  - H-03 Fold5 first_frame. 펼침 중앙값 1263ms(5회 1253·1263·1722·1260·1320, 기준 2000ms 이하, 이전 2069)와 전면 중앙값 1753ms 모두 PASS 다.
- DEFERRED. AC-026·028 은 화면 안 기동까지 했으나 사용자 복귀(19:25 이후)로 도구가 중단했다. AC-013·014·015 실입력은 같은 이유로 못 했고, AC-020·022·024·029 Fold5 는 Debug APK 설치가 선행돼야 한다. H-08 은 가드 수정이 필요해 범위 밖이다. 사용자 PdPoc 폴더는 쓰지 않았고 시작·끝 스냅샷이 같았다.
- 문서 마감(커밋 전, B4). `HISTORY.md`·`plan/04_기술스택/04_판정보고서_부록/a10_측정보류_목록.md` §6.2·`plan/90_결정관리/03_향후과제_TODO.md`(UTODO-0001 완료, 0003·0005 진행, 0012 신설)·`tools/README.md`·`test/README.md` 를 갱신했다. `tools/classify_layers.py` 의 생성 문구를 새 홈 기준으로 고치고 `LAYERS.md` 를 재생성했다(`--check` rc=0, 기존 LAYERS.md 가 소스와 달라 532→718개로 바뀌었다).
- 미측정 보완. H-01·H-02·H-07 은 이번 사이클에 측정하지 못했다(근거는 `plan/04_기술스택/04_판정보고서_부록/a10_측정보류_목록.md` §6.2 에만 있었으므로 여기에도 적는다).
- push 는 하지 못했다(이 저장소에 원격이 없다). 커밋은 로컬뿐이다.

## 2026-11-24 — Pd 에서 이전(UI PoC → XKXK 모노레포 별별UI, O4, conv_179547985218)
- Pd 의 UI PoC(`poc/ui` 추적 파일)와 UI 관련 정본 문서·디자인 자산·GPT 구조 대화 원문을 원본 무변경으로 `/mnt/c/DATA/Project/xkxk/frameworks/ui` 로 복사했다.
- 저장소 구조 정본은 `/mnt/c/DATA/Project/xkxk/frameworks/ui/plan/05_아키텍처/01_XKXK_저장소구조.md`, 결정은 UADR-0001, 남은 경로 고정 과제는 UTODO-0001 이다.
- 이전 경위·복사 범위·남은 작업은 `/mnt/c/DATA/Project/xkxk/frameworks/ui/plan/90_결정관리/이전기록_2026-11-24.md` 에 있다.
- 시험 결과(otest PASS). AC-01~13·14a·15~21 총 21항이 xkxk_gate.py 로 PASS 했고, Debug 빌드는 rc=0(오류 0·경고 8)이다. sha256 표본 20/20 일치, menu_icons 236/236, check_layers 위반 5건(원본과 동일), git dry-run 1,494건에 INFRA·bin·obj·logs·비밀값은 없었다. AC-14b(첫 커밋 제목·Co-Authored-By)는 커밋 뒤 판정한다.
- 커밋은 이 저장소 main 의 첫 커밋이다(제목 `🎉 별별UI 저장소 초기화 — Pd UI PoC·UI 정본 문서 이전`). 해시는 `git log` 로 확인한다. 원본 기준 커밋은 Pd 157403c 이고 push 와 원격 추가는 하지 않았다.
- 잔여 과제는 UTODO-0001(경로 고정 시험 스크립트 정리)과 대화 기록 jsonl 수동 이전이다.
- 아래 항목부터는 Pd HISTORY 에서 옮긴 이력이다.

## 2026-11-24 — 사용자 설정 폴더 대조(읽기 전용, O2 후속)

- 범위
  - 사후 질문 Q8(읽기 전용 대조 허용)의 후속이다. 사용자 설정 폴더 대조를 읽기 전용으로 했다. 대상은 `/mnt/c/Users/rioky/AppData/Local/PdPoc` 이고, 기준은 `poc/ui/logs/user_files/20261116_132507_113.md5`(pending 기준) 이다.
  - 방식은 `poc/ui/tools/run_desktop_bg.sh` 57행 `uf_md5` 와 같은 식을 읽기 전용 스크립트로 재현한 것이다. 대조 때 Pd 프로세스는 0개였다. 보고서는 `/tmp/cc-11076133-f9e3-4458-8913-889fe907b92d/session-env/5a01cad7-2b06-412f-a040-38c16799d4ad/followup/user_files_compare.md` 에 있다.
- 결과(판정: 설정 차이)
  - settings.json 이 다르다. `Theme` 는 Light→Dark, `ThemeId` 는 pd-light-prev, `MagnetRange` 는 24→10 으로 바뀌었고, 새 키가 50여 개 더해졌다.
  - layout.json 도 다르다(`Version` 3→5, 패널 비율, 활성 문서 등).
  - logs 의 일자 로그 이름 목록도 다르다. 옛 일자 로그 7개가 사라지고 11-17~11-23 일자 로그 5개가 생겼다.
  - 대조 전후 스냅샷 md5 가 같았다. 대조 과정에서 사용자 파일은 바뀌지 않았다.
  - 설정이 바뀐 구간은 2026-11-18 04:41 ~ 11-22 16:10 이다. 그 사이 사용자 폴더를 쓰는 Pd 실행이 있었던 것은 사실이지만, 사용자 본인 사용인지 시험이 남긴 창인지는 기록으로 가릴 수 없다.
- 처리(R3)
  - 설정 파일 내용이 기준과 달라 R1(완전 일치)·R2(설정 같고 이름만 차이) 조건을 못 채운다. pending 은 유지했다.
  - AC-026·028 은 DEFERRED 를 유지했다(시험 보고서 §14.6). TEST 집계는 바뀌지 않았다.
  - 사용자가 현재 설정이 본인 상태가 맞는지 확인한 뒤 pending 을 지우거나 백업으로 되돌려야 한다(TODO-674).
- 범위 밖 관찰(TODO-675 로 넘김)
  - `uf_md5` 는 이름 목록에 `logs/pd-YYYYMMDD.log` 일자 로그를 넣는다. 그래서 설정이 같아도 날짜가 바뀐 뒤 Pd 를 한 번만 실행하면 불일치가 난다.
  - `run_desktop_bg.sh` 58행은 Pd 가 1개 이상 떠 있으면 대조를 건너뛴다. --multi-instance 기동은 pending 을 갱신하지 않는다. 그래서 11-16 이후의 설정 변경이 검출되지 않았다.
- 손대지 않은 것
  - 사용자 파일(AppData PdPoc), pending, 가드 스크립트는 쓰지 않았다. 문서만 고쳐 커밋했고 push 하지 않았다.

## 2026-11-23 — 디자인 효과 강화(O5) 움직임 효과 닫기 덮개·전환 짝 결함 수정 5~11차

- 범위
  - 창 닫기 덮개와 DOCK 탭 전환 짝 나감 그림을 고쳤다(`poc/ui/src/Pd.Ui.Views/Depth/`). 원인은 아래 사슬 순서로 하나씩 드러났다.
  - 설계 정본 `plan/09_화면설계/21_창_움직임효과_배경_설계.md` §4.1(바탕·배율 1:1·글자 회색조·바탕 여러 겹)·§10·§11 을 함께 고쳤다.
  - 단위 콘솔 `poc/ui/test/unit/snapshot_geometry/`(앱 없음, Avalonia.Headless + Skia)를 새로 만들어 배율·글자·바탕을 기기 없이 먼저 본다.
- 원인 사슬(차수 = 수정 판)
  1. 5~6차 — DOCK·NAV 덮개 바탕 없음. 자기 바탕이 없는 탭 본문만 찍어, 덮개가 비쳐 닫힌 뒤 화면이 보였다. 조상 바탕 한 겹을 까는 합성을 넣었다(Backdrop). NAV 닫기 재생 0 은 현재 문서 판정이 화면 DataContext(MobileNavViewModel)를 보던 결함이었다.
  2. 6~7차 — 덮개 확대(웹 DPR 2.25 에서 MDI 약 5.2배·DOCK 2.25배, Fold5 약 5배). Avalonia 12.1.4 의 DrawImage 원본 사각형은 화소인데 2인자 DrawImage·Image 는 DIP(Bitmap.Size)를 넘겨 배율만큼 커졌다. 또 dpi 배율 RenderTargetBitmap 은 BoxShadow 가 있는 MDI 창에서 클립·그림자 복원 뒤 뒤 배율을 한 번 더 곱해(s²) 찍기에서도 커졌다. 화소 원본 사각형 명시(SnapshotGeometry), dpi 96 비트맵 + 그리기 변환 배율(SnapshotRenderer)로 고쳤다. 배율 이중은 HEAD 부터 있던 결함이고 DOCK·NAV 합성으로 한 번 더 곱한 것은 6차 변경분이다.
  3. 7~9차 — Fold5 덮개 한글이 꽉 찬 네모(■)·굵은 획. 투명 RenderTargetBitmap 에 LCD(서브픽셀) 글자를 그린 것이 원인이었다. 8차 진단 판(찍기 방식 5종)으로 회색조(aa·aanohint)만 정상임을 가렸고(NAV 오차 0.19~0.29·DOCK 2.23~2.38 PASS, scaled·dpi FAIL 3.65~4.8), 9차에 찍기 문맥 글자를 회색조로 고정했다. 6차 프레임에도 같은 깨짐이 있어 7차 회귀가 아니었다.
  4. 7~9차 — 콤보 서랍·새 탭 열기 깜박임 후보. 들어오는 화면이 한 프레임 다 보였다가 효과가 시작됐다. Loaded 가 첫 Render 보다 늦게 오는 경로에서 효과 시작이 늦었다. 9차에 Post 전에 대상 위치를 찾고(Locate), 배치가 끝난 뒤(LayoutUpdated, AfterLayout) 시작하게 했다. Fold5 9차: 콤보 깜박임 0/10, 열기 깜박임 없음 3/3.
  5. 10차 — 웹 glass 테마 탭 묶음 닫기에서 덮개가 반투명해 남은 문서가 비쳤다. MDI 창 안 탭 묶음은 DOCK 탭 배치로 찍히고, 조상 한 겹 규칙이 glass 반투명 본문 바탕만 깔았다. 조상 바탕을 불투명 바탕까지 겹쳐 깔고(주인 자리·모서리 기준) 없으면 테마 `PdBrushBackground` 를 맨 아래에 까는 규칙(SnapshotBackdrop)으로 고쳤다. MDI 겹침·최대화 닫기는 바꾸지 않았다.
  6. 11차 — 웹 DPR 2.25 에서 탭 묶음 닫기(glass·dark 모두)의 덮개 첫 화면이 닫기 전과 어긋났다. 같은 회차에 바탕을 까는 DOCK·NAV 사례가 모두 PASS 라 바탕·배율 경로는 정상이었다. 덮개가 활성 탭 본문만 가려서, 묶음이 풀리며 바뀌는 묶음 창 머리(탭 줄·제목)가 덮개 밖에서 먼저 끝 모습으로 바뀐 것이다. 결함은 배율과 무관하게 있었고, DPR 1 에서는 덮개가 머리 다시 그리기보다 먼저 잡혀 드러나지 않았다(추정). MDI 창 안 탭 묶음의 탭 닫기는 묶음 창 전체(머리 포함)를 덮개로 찍고 MDI 경로·MDI 효과 키로 재생하게 했다. 닫힌 뒤 같은 자리에 남은 문서 창이 그려지므로, 일반 MDI 창과 달리 조상 바탕을 겹쳐 깐다.
  - 별건(남은 과제): 웹 DPR 1 base.group.dark 의 '중간 프레임 0'(2회 재현). 처음에는 닫기 1.5초 전 테마 전환의 잔여 작업으로 봤으나, 대기를 5초로 늘려도 2/3 FAIL 이라 그 가설은 기각했다. 재생 320ms 동안 앱이 그린 프레임이 2~3장(간격 137~854ms)뿐이다. 덮개 쪽 매 프레임 일은 불투명도 한 단계와 잘라냄 갱신뿐이라(코드 확인), 덮개 아래 장면 다시 그리기가 dark 에서 비싼 것으로 추정한다. 11차 판에서 고친 판정기로 다시 잰 3회는 모두 INVALID(정체, 최대 간격 786.6·643.2·398.9ms)였다. §7 정체 규칙에 따른 판정이며, 원인 요소 특정은 TODO-671 로 넘겼다.
- 판정기 결함(같은 사이클에서 수정)
  - 웹 DPR 판정이 앱의 DIP 좌표를 CSS 화소로 바꾸지 않았다(`--dpr`·CSS 정규화, 7차). 그리기·판정 영역의 배율 k(웹 DPR 2.25 에서 CSS = DIP × 2.25, TopLevel ClientSize 622.2 DIP)는 맞다(확인, r11/web2).
  - 9차 웹 close_dpr225 의 overlap.occlusion FAIL·명령 경로 '끝 모습 미도달' INVALID 는 판정기 기준 그림 결함이었다. 처음·끝 기준 그림(screenshot 3150x2025 → LANCZOS 축소)과 프레임(screencast 1400x900, 브라우저 축소)의 축소 경로가 달랐다. 기기 배율 ≠ 1 이면 기준 그림도 screencast 한 장으로 받게 고쳤다(`motion_close_rig.py` 0f44dd89, ref_source 기록). 판 judge 28e25696·rig 0f44dd89·rules 8e3189d2, 자체 시험 62/62(odev-2 기록).
  - 누름 빗나감: DPR 2.25 에서 마우스 누름이 배율 1 점에서만 ✕·탭에 닿았다. 배율 측정(`/home/rioky/.claude/session-env/5a01cad7-2b06-412f-a040-38c16799d4ad/work/tmp/test/r11/scale_probe/web_input_scale_probe.json`)으로 가렸다. Playwright 의 device_scale_factor 흉내에서는 앱 TopLevel 이 622.2 DIP(k 2.25)가 되고 배율 1 점만 닿았다. Chrome 실제 배율(force)에서는 main.w 1400·k 1 이고 배율 1 점이 닿았다. 흉내 방식에서만 생기는 일이라 제품 결함이 아니다. 판정기는 DPR ≠ 1 을 흉내가 아닌 실제 배율로 열도록 바꿨다(odev-2).
  - 같은 측정에서 흉내 DPR 2.25 의 앱 화면 배치(622.2 DIP)는 실제 배율(1400 DIP)과 다르다는 것도 드러났다. 흉내 방식으로 잰 DPR 2.25 결과(10·11차 close_dpr225)는 '배율 2.25 그리기' 시험으로는 유효하지만 실제 Chrome 고배율 화면 배치와는 다르다. 탭 묶음 머리 결함(11차)은 배율과 무관한 덮개 범위 결함으로 판단해 그대로 고쳤다(추정).
  - 9차 콤보에서 전환·열기 화면에 닫기용 판정을 적용해 거짓 FAIL 10 을 냈다. 깜박임 규칙(혼합값 되돌림·첫 변화 = 끝 그림)을 `motion_close_rules.py` 5f7bcf86(자체 시험 57/57)에 넣었다. 전환·열기 판정은 `poc/ui/test/mobile/measure/motion_nav_fold5_frames.py` 에서 닫기 판정과 나눴다. 콤보 전환은 cf995aca 판, 열기 사례·행 메타·낱말 완전 일치는 43a02e5c 판이다(자체 시험 17/17, `motion_nav_fold5.py --self-test` rc=0).
  - 그 밖: 재생 기록만으로 PASS 를 낸 Fold5 닫기 식(6차, 거짓 PASS), 덮개 바탕 누락을 못 잡는 중간 프레임 유무 식(5차), 제목줄 hover 를 포함한 비교 영역(5차).
  - (d) 정체 판정 미적용(11차): 판정기의 play_ms 처리 결함으로, 재생 중 앱 프레임 간격이 효과 시간을 넘어도 §7 정체 규칙(INVALID)이 적용되지 않아 base.group.dark FAIL 이 그대로 FAIL 로 셌다. odev-2 가 고쳤다. 최종 판은 judge_session 59ca0eeb·judge 7ae7dfbc·rig 3717be16·rules 78aa29e9 이다.
- 중단·복원
  - 2026-11-23 09:22 무렵 WSL 급종료로 팀원 4명(otest-1·odev-1·odev-2·odev-3)이 모두 사라졌다. `/oresume` 으로 재개했고, 같은 이름의 후임을 otest-1 → odev-2 → odev-1 → odev-3 순으로 다시 띄웠다. 각 후임은 `work/<이름>_result.json` 기록의 멈춘 지점부터 이어받았다. 세션 메타는 수동으로 복원했다(`resume_restore.py --check` rc=5 는 옛 사이클의 FINISH_DONE 때문에 난 오판).
- 시험 결과
  - 단위 콘솔 `snapshot_geometry` rc=0 — 기하 240건, 덮개 바탕 18건(5사례 + 탭 묶음 창 group_window 1사례 × 배율 1·1.5·2.625) PASS. glass 사례에서 옛 한 겹 규칙과 새 규칙의 평균 차 57.1·62.9.
  - Fold5 9차 선확인 통과 — 닫기 화소 PASS 5/5(오차 0.32~2.38, 한글 정상), 콤보 깜박임 0/10, 새 탭 열기 깜박임 없음 3/3, DOCK 탭 전환 짝 PASS 2/2.
  - 10차 웹: DPR 1 base.group.glass PASS(첫 변화 alpha 0.001·off 0.28, 중간 7장). DPR 1 base.group.dark FAIL(중간 0, 별건). DPR 2.25 base.group.glass·dark FAIL(묶음 머리 → 11차). 그 밖의 DPR 2.25 사례는 PASS.
  - 11차 웹(근거 `/home/rioky/.claude/session-env/5a01cad7-2b06-412f-a040-38c16799d4ad/work/tmp/test/r12/web/`): close_dpr1 58/58 PASS(3부 합산, 같은 판). close_dpr225(Chrome 실제 배율)는 PASS 51·FAIL 0·INVALID 7 이다. 탭 묶음 dark·glass 가 PASS 라 덮개 첫 화면 FAIL 이 풀렸고, 누름 빗나감은 0 이다.
  - INVALID 7(겹침 left1·left2·효과 3종 등)은 관찰 5초 안에 끝 모습에 닿지 않은 것이다. 판정기 문제가 아니라, headless 소프트웨어 렌더가 6.4M 화소를 합성하느라 늦은 것으로 추정한다(odev-2 분석 `/home/rioky/.claude/session-env/5a01cad7-2b06-412f-a040-38c16799d4ad/logs/odev2_r11_dpr225_overlap_invalid.md`). a10 W-10 으로 실제 GPU·실기기 재측정에 넘긴다.
  - base.group.dark DPR 1 × 3: PASS·FAIL·PASS. FAIL 회차는 재생 중 앱 프레임 2장(간격 677ms)이다. 판정기 결함 (d) 를 고친 뒤 다시 잰 3회는 모두 INVALID(정체, 최대 간격 786.6·643.2·398.9ms, play_ms 320)이다.
  - 18:25 시점 TEST 종합(36 AC): PASS 7·FAIL 2(AC-003·006, 기존 위반)·INVALID 1(AC-010, 정체)·UNDET 1(AC-022, Fold5)·DEFERRED 24·CANARY 1. AC-021 은 11차 재시험 12/12 PASS. 최종 값은 아래 보강 시험에 있다.
  - 18:25 시점에는 데스크톱 실입력·성능을 측정하지 않았다(사용자 결정 전 보류, a10). 사용자가 데스크톱 앱 실행을 허용한 뒤 아래 보강 시험에서 쟀다.
  - 범위 밖 관찰: Fold5 9차 보강에서 제목 탭 길게 누름 메뉴가 ESC 뒤 다음 화면 구조에도 남았다(앱 강제 종료로 원복). 뒤로 가기·바깥 터치 확인은 아래 보강 시험에 있다.
- 보강 시험(11차 판, 2026-11-23 21:24~22:46, 근거 `/home/rioky/.claude/session-env/5a01cad7-2b06-412f-a040-38c16799d4ad/plans/test_report_conv_179530386670.md` §13.2·§14~§14.4)
  - 이번 보강에서 앱 소스는 바꾸지 않았다. 고친 것은 측정기·판정기뿐이다(앱 src·Pd.Ui.Views.dll md5 9590a40d… 그대로).
  - Fold5 11차(otest-m-1, 11차 APK 3a915cc7…, 사용자 파일 3종 원복·일치)
    - AC-022 PASS — 본 판정 5종 5/5(rc=0). 선확인 ①~④ 19/19 PASS(닫기 4·콤보 전환 10·DOCK 새 탭 열기 3·DOCK 탭 전환 2), none 대비 2/2·콤보 키 3/3.
    - 실시간 판정과 frames 재판정이 갈린다. 실시간 감싸개는 모든 사례를 닫기 덮개 규칙으로 보고, `--judge` 재판정은 사례 이름 낱말(combo·open → 들어오는 화면 규칙)로 규칙을 고른다. 그래서 콤보·열기 장면은 실시간 FAIL 13+3 이 재판정에서 모두 PASS 다. 재판정을 정본으로 한 것은 9차 기준(§11.5 의 증거 인용과 판정기 코드 경로)을 따른 것이며, 이 기준을 적은 명문화된 결정 문장은 없었다. 이번 적용은 리드가 확인했다. 실시간 FAIL 은 보고서에 함께 남겼다.
    - tab_hold 700ms 프레임 사례 3/3 FAIL 은 관찰(집계 제외)이다. 긴 누름은 닫기가 아니라 메뉴 열기라 덮개 판정 대상이 아니다(9차와 같은 기준). 긴 누름 메뉴 기능은 3/3 열림으로 확인했다.
    - 긴 누름 메뉴는 ESC 로 닫히지 않는다. 뒤로 가기 1/1·바깥 터치 1/1 로는 닫혔다(범위 밖 관찰, TODO-669 갱신).
    - crash 버퍼의 2026-11-22 21:57 FavoriteBarView FATAL(`$parent[views:FavoriteBarView]` ReflectionBinding 형식 해석 실패)은 개발 단계에서 이미 잡아 22:02 에 Click 경로로 고친 P0 다. 11차 소스에 같은 코드 경로가 없고, 11-23 crash 는 0건이다.
  - 데스크톱 14차~14.4(otest-1, 화면 밖 격리 인스턴스, PC 비움)
    - 측정기·판정기 수정 4건(odev-1). ① `perf_motion_open.py` — 측정 동안 재생 시간 키 motion 을 subtle(120ms)로 덮어쓰고 끝나면 되돌린다. ds-classic 은 0ms 라 '효과 켬'에도 재생이 없었다. ② `judge_session.py` FindMain — PID 로 EnumWindows 해 찾고, `--offscreen-guard` 의 같은 PID 숨은 소유자 창을 허용한다. ③ `capture_icon` — 앱 RenderTargetBitmap 대신 PrintWindow 플래그 3(OS 합성 캡처). ④ `window_load_judge.py --burst-capture screen` — UI 막힘 중 화면 픽셀(CopyFromScreen) 캡처.
    - AC-018a·b PASS(스피너 모양 110~117화소), AC-018c PASS(막힘 1493ms 안 3장 모두 스피너, 회전 차 113·96). AC-018c 는 DISPLAY1 에 처음부터 띄운 인스턴스로 화면 안 26.8초 노출(TOPMOST 3.6초, 실입력·전경 변경 0)로 판정했다. AC-019 PASS, AC-023 PASS(PERF-12 비 0.999·PERF-13 p95 15.23ms).
    - AC-031 FAIL — 14차 medium PERF-13 p95 29.45ms FAIL, 재측정 2회도 0/2(재 1 PERF-13 32.68ms, 재 2 PERF-12 비 1.242). 열기 재생과 문서 첫 배치가 겹치는 비용이며 TODO-672 로 넘겼다.
    - INVALID 로 남은 것: AC-010(웹 정체, TODO-671), AC-012(interrupt.drag_start 비율 0.0489 < 문턱 0.05, TODO-673). DEFERRED 로 남은 AC-026·028 은 사용자 Pd 실행으로 도구 전제 '다른 Pd 0개'가 충족되지 않았다(TODO-674).
  - 최종 집계(36 AC): PASS 20·FAIL 3(AC-003·006 기존 위반, AC-031)·INVALID 2(AC-010·012)·UNDET 0·DEFERRED 10(009·011·013·014·015·020·024·026·028·029)·CANARY 기대 FAIL 1, overall=FAIL. DEFERRED 는 합격이 아니다.
- 문서 — 21 §4.1·§7·§10·§11, `poc/ui/test/README.md`, a10 W-08~W-10·§6.1, TODO-667~674(`plan/90_결정관리/03_향후과제_TODO_2.md`), 번호 대장. git 은 로컬 커밋만 하고 push 하지 않았다(사용자 결정).
- 정정 — 아래 2026-11-21 PC 비움 대기·2026-11-21 c8·2026-11-22 PC 성능 기준 항목의 변경은 그 항목들의 '로컬 커밋' 기록과 달리 남아 있던 `index.lock`(2026-11-21 14:13 생성) 때문에 커밋되지 않았다. 잠금을 지운 뒤 이번 사이클 커밋에 함께 넣었다.
- 정정 — 보강 시험 중 '사용자 Pd'로 본 PID 33552 는 실제로는 시험 담당이 21:28 에 시험용 실행 사본(`bin/run/otest1_r14`)을 `--help` 로 띄운 뒤 남긴 창이었다(부모 wsl.exe, 포트 5180·5220 미사용). 약 1시간 반 동안 사용자 화면 (104,104)에 떠 있었고, 23:07 무렵 리드 판단으로 정상 종료했다. AC-026·028 의 '다른 Pd 0개' 전제 미충족도 이 오인에서 나왔다(TODO-674).
- AC-026·028 재시도(23:05)는 사용자 파일 보호 가드(`poc/ui/tools/run_desktop_bg.sh` rc=9 USER_FILES_CHANGED, `poc/ui/logs/user_files/pending` 2026-11-16 13:25·기준 20261116_132507_113 미해결)에 막혀 DEFERRED 를 유지했다(집계 그대로). 앞선 14차~14.4 기동은 남아 있던 시험 창 때문에 'Pd 0개일 때만 대조' 조건에 걸려 사용자 파일 대조를 건너뛰었다. 사용자 AppData(PdPoc)는 열어 보지 않았다.

## 2026-11-21 — PC 잔여 시험 마무리 c8(O3, 계측·시험 도구 추가)

- 범위
  - TODO-652 `NarrowToolChrome.cs` 에 계측 `ui.narrowtool.apply.narrow|wide` 와 `poc/ui/test/perf/measure/perf_narrow_tool.py`, TODO-653 `ui.apply.design.firstframe.ms`(팝업은 `.popup.ms`)와 `perf_dock_float.py`(원격 분리 `docs.sdi on=true`·`sdi.host.firstframe` 은 기존 재사용), TODO-654 `snap_assist_fg_k11.py` 4케이스 구현. 새 동결판 a527174d83405084(진단판), 빌드 오류 0.
- 시험 결과(최종)
  - W-03 좁은 창 도구 방송 n=20: 좁아질 때 frame p50 3.115ms·p95 15.30ms·max 17.04ms, 콜드 13.27ms(기준 없음, 측정값만 기록). 디자인 테마 전환 회귀 PASS: n=10 p95 95.5ms(기준 300ms 이하).
  - W-04 분리창 디자인 첫 프레임 n=10: p50 24.29ms·p95 68.52ms·min 13.91ms(기준 미정, 측정값만 기록).
  - K11-② 스냅 배치 실입력 4종(카드·흐린 칸·바깥 실클릭·Esc 실키) 모두 PASS. 시험 도구 결함 2건(파서가 목록을 문자열로 둠, 흐린 칸에 자동화 요소가 없음)은 같은 사이클에서 수정. 바깥 실클릭 진입 흔들림은 수정 전 판 2회·최종 판 0회.
  - Fold5(R3CW70202FP) W-02 펼침 DOCK PASS(K10 6케이스 중 2건은 도구에 DOCK 기대값이 없어 화면 대조로 판정, 후속 TODO-656·657), W-07 좁은 창 도구 여백 PASS(넓을 때 8.0dp·좁을 때 4.0dp, 데스크톱 10px↔6px). W-01 접힘 NAV PASS — 기기를 접지 않고 adb `cmd device_state state 0`(접힘 덮어쓰기)으로 전면 화면 904x2316 을 켜고 시험, ui.style effective=Mobile·401.8dp, K10 6케이스 전부 PASS, 원복 확인.
  - 관찰: 분리창 띄울 때 Dock 바인딩 경고 9줄(예외 없음).
- 문서 — `plan/04_기술스택/04_판정보고서_부록/a10_측정보류_목록.md` §5, `plan/90_결정관리/03_향후과제_TODO_2.md` TODO-652~654 상태·TODO-656·657 신설, `plan/90_결정관리/09_번호_대장.md` 다음 TODO 를 TODO-658 로. git 은 사용자 지시로 보류(커밋 안 함).

## 2026-11-19 — 결정 142 — 정본 대원칙 재검토(ADR-126, conv_179489367442, O5·plan 문서만)

- 범위
  - 사용자 지시(2026-11-17, 원문 `plan/00_요구사항/01b_사용자요구사항_원문_3.md` §3-0x.1)로 지금까지 만든 계획 전체를 대원칙(P-01~P-12) 기준으로 다시 검토하고 고쳤다. 비용·시간을 이유로 이름·로직·프로세스 변경을 거부하지 않는다는 지시다.
  - batch 1 원천 확정. 원천 12개 정의 문면(모듈 단위 정의·이름 규칙·공통 기반 4분류·조립 세 단·설정 키·계약 ID)을 확정하고 `plan/05_아키텍처/53_Pd_용어_상태어휘_문서규칙.md`·d488·이름대응표를 만들었다(c951816, 재리뷰 R0 27건 닫힘).
  - batch 2 전 폴더 재검토(읽기 전용). 조각 rv-01~rv-24 와 횡단 X1~X7·XH 리뷰어가 발견을 모으고 병합·조치 배정을 했다.
  - batch 3 수정. Wave W1~W6 과 재리뷰 R1~R3 를 돌렸다. 모듈마다 자기 스키마와 08·10 문서 쌍(새 모듈 17), 기능 부록 인용 맞춤, 설정 키 등록부 생성물 CSV, 일정 검사기, 결정·TODO 대장 정리, 캐시 우선 조회와 개인화 반영이 들어갔다.
  - batch 4 결과 기록. 결과 07 가족·ADR-126·d553·원문 §3-0x·REQ 표·색인·이 항목과 LESSONS·봉인이다.
- 판정
  - 발견 원건수 2,180건(조각·횡단 2,095 + 끊긴 리뷰어 복원 85) → 열린 발견 2,053건을 묶음 266개(B-001~B-266)로 합침 → 조치 행 F-P 331개(수정 270·이관 32·권장안 28·유지 1, 유지는 ADR-054 근거)다. 결과는 `plan/90_결정관리/07_정본_대원칙_재검토_결과.md`, 발견 목록은 `07a_정본_대원칙_재검토_발견목록.md`(F-P-001~270)·`07b_정본_대원칙_재검토_발견목록_2.md`(F-P-271~331)다.
  - 재리뷰 4회(R0~R3) 244건은 고침 240·넘김 1·고치지 않음 2·철회 1 로 닫았다(`plan/90_결정관리/07_정본_대원칙_재검토_재리뷰처리표.md`).
  - R3 수정 커밋 1aa5b52 기준 run-checkers 9항목 rc=0, check_schedule 0, R3 열림 0 이다.
- 사용자 요구 반영(재검토 중 추가, 원문 §3-0x.2)
  - 캐시 우선 조회 REQ-110. pd-server 의 모든 읽기는 원천과 무관하게 캐시를 먼저 확인한다. 정본 `plan/05_아키텍처/07_캐시서버_아키텍처.md` §10, D-553~D-555.
  - 개인화 REQ-111. 즐겨찾기·최근 조회·개인 순위·추천을 모든 목록에 한 벌로 쓴다. 정본 `plan/09_화면설계/20_개인화_빠른찾기_설계.md`, D-561~D-566, TODO-640.
  - 개인화 정본은 처음 09/18 로 만들었으나 pd-bb 의 `plan/09_화면설계/18_디자인테마_설계.md` 와 번호가 겹쳐 20 으로 옮겼다(54ae0a4, 인용 22개 파일).
- 새 문서(결정 142 몫)
  - 아키텍처 `plan/05_아키텍처/53_Pd_용어_상태어휘_문서규칙.md`·`53_…_2.md`·`21m_Pd_연계명세_후보연계.md`·`23a_…_2.md`·`30_…_2.md`·`32_…_2.md`, 화면 `plan/09_화면설계/20_개인화_빠른찾기_설계.md`·`03_…_2.md`·`08_…_2.md`, 기능 `plan/06_기능목록/01c_요구기능추적.md`·`20_…_2.md`·`a05_PLT_2.md`·`설정키등록부.csv`.
  - 데이터·API·이벤트. 새 모듈 문서 08·10 쌍(`bizplan`·`col_booking`·`contact`·`fin_ledger`·`goal`·`hrs_att`·`hrs_pay`·`iss_sla`·`kms_doc`·`money`·`notify`·`site`·`sorder`·`suggest`·`vault`·`wcal`·`wiki`)과 `fin_3.md`·`lic_2.md`·`iam_2.md`·`plt_2.md`·`plan/10_API설계/00_공통규약_2.md`·`plan/11_이벤트/01_outbox_이벤트목록_2.md`.
  - 결정 관리. `plan/90_결정관리/07` 가족 4개, `08_결정142_이름대응표.md`(_2~_5), `09_번호_대장.md`, `10_회차번호_대응표.md`(_2~_6), `03_향후과제_TODO_2.md`·`03b_향후과제_TODO_종결보관.md`, d 부록 `d001_초기결정_대장.md`·`d488_결정142_확인대기.md`·`d553_결정142.md`, `02_결정기록_부록/ADR-126.md`.
  - 검사기 `plan/_build/check_d_inline.py`·`check_module_docs.py`·`check_schedule.py`·`key_registry.py` 와 규칙 모듈들.
- 번호
  - 결정 142(142-1~142-7), ADR-126, REQ-110·REQ-111, D-488~D-568(D-488~D-552 는 d488, D-553~D-568 은 d553), TODO-231~642(다음 TODO-643), 다음 D 는 D-569 다. 번호 정본은 `plan/90_결정관리/09_번호_대장.md` 다.
  - 새 기능 ID 넷 ORG-06·IAM-14·LIC-10·COL-18(D-552)이다. ADM-12 는 결정 133 과 충돌해 철회하고 ADM-07 을 넓혔다.
  - D-488~D-568 의 사용자 확인이 남았다. 상태는 d488·d553 상태 칸이 정본이다. 특히 D-568(웹 끌기 판정값)은 2026-11-14 사용자 결정을 대장에 올린 것인데 그 원문이 저장소에 수록돼 있지 않다.
- 진행 사고
  - Fable 한도 정지 2회. 2026-11-18 00:07~07:20 은 계정 변경으로 재개했고, 2026-11-18 11:11 정지 뒤 사용자가 모델을 Opus 로 바꾸고 팀원도 교체하라고 지시했다(12:10). 검토 앞부분(c951816~70e2db2)은 Fable, 그 뒤는 Opus 다.
  - 사용자 일시정지(2026-11-18 19:35)가 길어져 R3 수정 담당 6명을 종료했고, 재개(2026-11-19, 계정 변경 뒤) 때 멈춘 지점부터 교대했다.
  - 원본 폴더 `/mnt/c/DATA/Project/Pd/.git/index.lock`(0바이트, 소유 프로세스 없음) 때문에 원본 폴더 ff 가 실패한 채 남아 있다. 공유 폴더라 지우지 않았다(사용자 조치 사항).
- 커밋
  - c951816 → 1d73616 → 925380f → 3585ea1 → e526ac9 → 36af370 → 905fb19 → af69e11 → 70e2db2 → b7bacb6 → 6df599c → 34f35c7 → bd30609 → 6bb0a2f → c6bf5ae → 9ea17af → dfdec70 → a1f630b → 9c441c1 → abe092b → 244b15d → 1aa5b52(중간 push). 그 뒤 pd-bb 커밋 d561d0b 위로 rebase 해 48df8a3(결과 기록 1/2)·54ae0a4(개인화 문서 번호)를 쌓았고, 마지막은 봉인 커밋(이 항목 포함)이다.
- 일정 추정
  - 출시까지 레인 병렬 575~968일(약 115~193.5주, 추정)이다. 결정 140 값은 552.5~936일이었다. 재산정 이력은 `plan/03_개발범위/31b_Pd_일정표_재산정이력.md` §20~§22 다.
- b4-7 회차 측정 일지 이동 결과(출처 세션 `work/review142/b4/out/b4-7_logs.md` §결과·§확인한 자리)
  - 옮길 회차 측정 일지는 0줄이다. 저장소 쪽은 고치지 않았다.
  - `plan/09_화면설계/04_웹_화면설계.md` §4 시험 환경과 `plan/09_화면설계/06_모바일_화면설계.md` §6 은 규칙·판정값 정의·정본 포인터만 있고, 회차별 측정 값은 이미 판정보고서 부록(a9·a10·a11)과 HISTORY 를 가리킨다.
- 이월
  - D-488~D-568 사용자 확인(d488·d553), D-487 사용자 확인.
  - pd-bb 이관 묶음(30b WBS-M4-49 매핑·COL-18 매핑·레인 이름 LN-*·합계 행 등, 세션 `work/review142/b4/out/pdbb_handoff_b4.md` 포함)은 pd-bb [작업종료] 때 함께 보낸다.
  - 원본 폴더 index.lock 정리(사용자).

## 2026-11-19 — 디자인 테마 C4 이월 마무리: 글자색 동기화(D-13)·menuBar(D-16)·디자인 테마 전환 비용 개선(K13, goal G-DTHEME-C5, conv_179489831590, O4·/oto)

- 범위
  - 아래 2026-11-17 항목(C4)의 이월을 이어받았다. 그 사이클은 커밋 전이라 이번 커밋에 함께 들어간다.
  - D-13 글자색: 테마를 바꿔도 일부 글자색이 남는 결함을 고쳤다. `FluentDynamicSync.cs` 가 정적 브러시 59개를 동기화한다(사전 점검 8/13 불일치 → 13/13).
  - D-16 menuBar: 메뉴가 제목줄 안에 있어 MenuBar·Text 색이 화면에 반영되지 않았다. 색 역할을 MainTitleBar·MainTitleText 로 바꿨다(`ThemeElements.cs`, 문서 17 을 먼저 고침).
  - K13 디자인 테마 전환 비용(odev-perf-7)
    - 원인은 디자인 Styles 마다 넣던 표식 리소스(PdDesignStylesSlot)였다. 넣고 뺄 때마다 ResourcesChanged 가 셸 전체에 방송됐다(삽입 68%·제거 59%).
    - 후보 1 로 표식 리소스를 없애고 참조로 추적한다. 후보 2 로 내장 5종 규칙을 한 Styles 에 미리 계산해 두고 최상위 창 클래스(`pdDesign-<ID>`)로 고른다(`ThemeDesignApplier.Switch.cs` 신규). 사용자·회사 값이 있으면 단일 경로를 쓴다.
    - 리뷰 r2 반영: 첫 렌더 전(TemplateApplied) 표식, 늘 켠 표식 `pdDesignOn` 으로 Setter 등급 통일, 표식 불일치 시 단일 경로 폴백, 팝업 루트 표식 끄기, 웹 기동 첫 프레임 miss 수정(TopLevel.Content 변경 훅).
    - 동치 점검 결과 차이 16건이 나왔다. 전부 NumericUpDown·AutoCompleteBox 안쪽 PART_TextBox 의 모서리·여백이며, 설계 의도와 맞아 의도된 변경으로 확정했다(C5-D-26).
  - DepthDiag 진단 값을 저장 값과 실행 전용 값으로 나눴다(odev-perf-6). 진단 키 기본값(끔)은 그대로다.
  - odev-perf-8 은 dtheme8 웹 열기비 회귀를 조사했다. 같은 조건 n=9 비는 d7 0.97, d8 1.03 이라 회귀는 기각에 가깝다. 코드는 바꾸지 않았다(안 A, C5-D-31).
  - 시험 도구 `poc/ui/test/desktop/d13_theme_text_color.py`(`--d16`)를 새로 만들었다.
- 동결: dtheme7 다음 최종 dtheme8(src sha256 `3394e03c…a415`). 기록은 세션 `work/dtheme8/` 에 있다.
- 판정 결과(goal E1~E7, 모두 dtheme8 기준 PASS)
  - E1 성능: 1차 k13d8 은 FAIL(웹 전환 p95 1840·웹 열기비 1.61)이었다. 2차 k13d8q(조용한 환경 재측정, C5-D-28)도 FAIL(1953·1.47)이었는데 Windows CPU 가 약 100% 였다.
  - 3차 k13d8r 은 시작 조건에 유휴 300초 이상·load1<50·Windows CPU<60% 를 넣어 PASS 했다. 웹 전환 p95 132, 웹 열기비 1.05, 데스크톱 전환 120.7, 데스크톱 열기비 0.43 이다. 데스크톱 2건은 정지 전, 8건은 재개 뒤 값이라 병합했다. FAIL 기록 2건은 지우지 않았다.
  - E2 D-13 사례 13건 불일치 0, E3 K8·K11 fails 0, E4 K9 11/11·K12 92/92, E5 K10(apk_md5 일치, ui_style=Mobile), E6 Fold5 펼침 474·접힘 425ms(≤500, n=10/10), E7 D-16 menuBar 2건 불일치 0.
  - otest_done 은 CRUD 게이트 rc=0 으로 PASS 했다. 배정표 v8 에 만들지 않은 파일 3건이 있어 처음엔 막혔고, v9 에서 지워 통과했다.
- 자율 결정: C5-D-25~C5-D-32(세션 `auto_decisions.jsonl`).
- 변경 문서
  - `plan/09_화면설계/17_테마_요소별_사용자지정_설계.md`: menuBar 행에 활성 창 제목줄 전체(캡션 단추 영역 포함)에 반영하고 비활성 창은 바탕 고정·글자색만 반영한다고 적었다. input 행에 복합 입력 안쪽 칸 적용을 적었다. 적용 방식에 디자인 테마 전환형 경로를 더했다.
  - `plan/09_화면설계/18_디자인테마_설계.md` §7: 전환 방식(표식 리소스 없음·사전 계산·TopLevel 클래스 전환·단일 경로 폴백)을 더했다.
  - `poc/ui/test/README.md`, HISTORY.md, LESSONS.md.
- 이월 후보
  - NarrowToolChrome 이 레이아웃 중 리소스를 써서 방송 45ms 가 생긴다(Pd.Ui.Views).
  - DesignEffectsApplier chrome·decor 표식이 Loaded 때만 붙는다(dtheme7 부터). 폴백 보고 문구도 정정이 필요하다.
  - ThemeApplier.cs:154 의 legacy·PaletteAlways·SkipPalette 진단 경로에서 FluentDynamicSync 가 돌지 않는다(D-13 재현, 진단 전용).
  - 펼친 폴드·태블릿(DOCK) 자세용 K10 대응 시험이 없다(설계 18 §4.6).
  - otest-m-7 마지막 정리에 'sweep remaining=12' 가 일부 미판정으로 남았다(harness_cleanup 확인).
  - Plan 규칙 캐시(전환 sync 약 10ms, 입력 키를 빠짐없이 잡아야 함), 예열 세분화(DocumentPrewarm 단계 쪼개기, 안 B — 근거 약함).
  - 편집형 ComboBox 는 제품 소스에 IsEditable ComboBox 가 없어 대상이 없다(N/A).
  - 팝업이 부모 창 디자인 규칙을 따르는지 미확인, 도킹 분리창 첫 프레임 미측정.
  - `k13_run13.py` 가 이어 쓰기를 지원하지 않고 lock owner 가 하드코딩돼 있다.
  - K13 p95(n=10 이라 최댓값)가 Windows 쪽 CPU 경합에 취약하다. 시작 조건 Windows CPU<60% 를 판정기 계약으로 고정할지 사용자 확인이 필요하다.
  - 문서 17 menuBar 행 반영은 이번에 처리했다. 선택 반전 색 반영 여부는 미확인이다.
  - pd-61 이관 목록(세션 coord_notes 2026-11-19 항목, 누적 52+9+11+8건)은 다음 사이클에 처리한다(C5-D-32).
  - 30b WBS 행(C4 디자인 테마 WBS)은 pd-61 [작업종료] 뒤 따로 push 한다. 그때까지 31b §17·이 항목이 가리키는 30b 행은 원격에 없다(로컬 브랜치 `pd18-30b-hold`).
  - 커밋 직전 gitlab/main(결정 142 재검토)이 문서 체계를 바꿔, C4·C5 문서 변경을 새 형식으로 다시 적었다(C5-D-33). docs-map 짧은 행, ADR 표 행, `plan/90_결정관리/09_번호_대장.md` 예약→사용, index.json 행이다. `plan/09_화면설계/18_개인화_빠른찾기_설계.md` 와 앞 번호 18 이 겹치지만 파일 이름이 달라 그대로 두었다.
  - C4 이월 중 D-373~D-384 사용자 확인, TODO-182~185, 지문·QR 앱 인계(ai-59)는 그대로 남는다.
- 한계
  - E1 통과는 시작 조건(Windows CPU<60%)을 3차 측정 전에 정한 뒤 얻은 값이다. 1·2차 FAIL 과 함께 읽어야 한다.
  - odev_simplify 는 변경 범위가 작고 리뷰 2회를 통과했으며 동결 직전이라 생략했다.

## 2026-11-17 — 디자인 테마 축·3D 창 효과·A16 웹 문서 열기 성능(REQ-104, 결정 134, ADR-118, goal G-DTHEME-C4, conv_179478964213, O4·/oto)

- 범위
  - 사용자 요구는 두 가지였다. 하나는 '색상 테마와 별개인 디자인 테마(아기자기·심플·화려·3D 효과 등)를 고객이 고르고, 여러 분야 앱을 참조한다' 이고, 다른 하나는 A16(Fold5 웹 문서 열기 p95 ≤500ms) 마무리다.
  - 문서(B1)
    - 레퍼런스 조사 정본 `plan/01_시장조사/19_디자인테마_앱레퍼런스_조사.md`, 설계 정본 `plan/09_화면설계/18_디자인테마_설계.md` 를 새로 만들었다.
    - REQ-104·결정 134·ADR-118(b40)·D-373~D-384(권장안 적용, 사용자 확인 대기)·TODO-182~185 를 등재하고, WBS(30b)·재산정(31b §17) 행을 더했다.
  - 구현(B2, `poc/ui`)
    - 디자인 테마 5 프리셋(ds-classic 포함)·효과 키·설정 계층(사용자 ?? 회사 ?? 테마 ?? 기본값)을 넣었다.
    - 전환 중에만 거는 3D 변환(depth.open/overview/page), 창 전체 보기(WindowOverview3D), NAV 페이지 넘김을 넣었다.
    - 움직임 수준(System/Full/Reduced/Off)과 웹 prefers-reduced-motion, 편집기 탭, 원격 명령(RemoteDesignThemeCommands), 다국어 키 theme.style.* 를 넣었다.
  - 한계 결정: 진짜 3D 공간 창 관리는 Avalonia 3D 장면 API 가 없어 불가로 판정했다. 대안 4종만 구현했다(ADR-118 재검토 조건).
  - 성능 개선(K13·K15)
    - perf1: 그림자 왕복 제거, 크롬 서명이 같으면 건너뜀, 저장 직렬화 유휴 예열(WindowChromeStyle.Design.cs 신규), PdCellChrome 의 TemplateBinding 7개를 구독 1개로 바꿨다.
    - perf2: depth.open=tilt 첫 기울기 렌더 비용(92ms)을 줄이는 기울기 예열 `Pd.Ui.Views/Depth/DepthPrewarm.cs` 를 넣었다(데스크톱만, 프로세스당 1회).
    - perf3: 표 셀 글자 직접 그리기 `PdCellChrome.Text.cs` 와 옵션 GridDrawnCellText(기본 켬, 설정 화면 '표 칸 글자 직접 그리기')를 넣었다. 정본 `plan/09_화면설계/01_화면설계_공통원칙.md` §5.4 에 반영했다.
    - perf4: K13 웹 전환 회귀를 진단만 했다. ABAB 교차 측정 결과 코드 회귀는 확인되지 않았다. 개선 후보 B·1·2 는 효과가 10ms 미만이라 코드를 바꾸지 않았다.
  - 진단 도구: `webperf_fold5.py --keep-diff`, `a16_trace.py`.
- 동결 이력(src_hash)
  - dtheme1 `995e0051…` 은 3축 기능 FAIL 이었다. dtheme2 `ce4d09e2…` 는 K5(300줄 초과) FAIL 이었다. dtheme3 `ad4c63e3…` 은 partial 분할로 K1~K12·K14·K16 PASS 였다.
  - dtheme4(perf1)·dtheme5(perf2 기울기 예열)를 거쳐 최종 동결판은 dtheme6 `c8722923…`(perf3 셀 직접 그리기, APK md5 `95d55273…`)이다. 기록은 세션 `work/dtheme/build_freeze.json` 이다.
- 판정 결과(K1~K17, 최종 dtheme6 기준)
  - K1~K4 문서 PASS, K5~K7 PASS, K14·K16 PASS.
  - dtheme6 회귀 재시험 결과는 K8 16/16·K11 5/5(데스크톱), K9 10케이스·K12 92/92(웹), K10 6/6(Android) 으로 모두 PASS 다.
  - K15(Fold5 A16) PASS: 펼침 p95 457·접힘 274(n=10×2, PC 비움 중). dtheme3 누적 557, dtheme5 508 FAIL 을 거쳐 통과했다.
  - **K13 FAIL(이월)**: 웹 전환 p95 1차 1816, 사용자 결정(D-11)으로 새 표본 재측정한 값 2043(기준 300). 첫 FAIL 기록은 지우지 않았다.
    - 데스크톱은 전환 p95 292·열기 비 PASS 다.
    - 재측정 때 load1 은 11~43 이었다. 같은 판이 밤에는 p50 163 이었고, ABAB 에서 부호가 일관되지 않아 다른 세션 부하 민감도로 판단했다(D-10·D-12).
    - 판정 규칙은 부하로 무효 처리하지 않으므로 FAIL 그대로 둔다. 다음 사이클에서 수단을 바꿔(측정 시작 부하 상한을 결과 전에 선언하는 등 — 측정 규칙 변경은 사용자 확인 필요) 다시 다룬다.
  - K17 은 이 항목으로 충족한다.
- 자율 결정: D-1~D-12(세션 auto_decisions.jsonl). D-7 은 모바일 시험 PC 비움 무관 지시를 다음 사이클 판정기부터 적용하기로 한 사용자 선택이다.
- 변경 문서
  - 신규 `plan/01_시장조사/19_…`·`plan/09_화면설계/18_…`·`d373_디자인테마_확인대기.md`·`b40_ADR-118.md`.
  - 수정 `01_사용자요구사항_원문.md`·`01a_…_2.md`·`01_화면설계_공통원칙.md`·`17_테마_요소별_사용자지정_설계.md`·`30b`·`31b`·질문목록·ADR·TODO·`plan/index.json`·docs-map.md·`poc/ui/test/README.md`·`testdata/README.md`.
  - 시험 자산: 신규 `design_theme_check.py`·`design_theme_cases.py`·`design_theme_cases_view.py`·`dtheme_legacy_settings.json`·`a16_trace.py`, 수정 `webperf_fold5.py`.
- 이월 후보
  - K13 웹 전환(측정 규칙 변경 사용자 확인 포함), child 방식 전환 개선(저부하 동기 −21ms, 안정 시점 불확실).
  - D-373~D-384 사용자 확인, TODO-182(OS 움직임 줄이기 연동)·183(아이콘 스타일 세트)·184(배경 흐림 유리 재검토)·185(디자인 테마 서버 저장).
  - 웹 표 셀 직접 그리기의 바탕·테두리·hover·선택행 시각 미검증, Android·영어 표 미실측.
  - 지문·QR 앱 구현 인계(ai-59, 결정 137 기준)는 다음 사이클에 착수한다.
- 한계
  - 성능·Fold5 실측은 사용자 PC 비움(away) 때만 해 시험이 늦어졌다. 기준(전환 p95 ≤300ms, 열기 비 ≤1.15, A16 ≤500ms)은 초기 제시값이며 결과를 본 뒤 바꾸지 않았다.
  - 3D 는 전환 중 RenderTransform 근사이며 진짜 3D 공간이 아니다.
  - perf2 측정이 otest-p-5 의 K15 측정과 일부 겹쳤다. 공식 실행기의 프로필이 남아 classic 시행이 depth 기동 뒤 전환됐다.

## 2026-11-17 — 결정 139 — D-418~D-467 50행 확인 결과·공통 기반 확대(ADR-123, conv_179488276384, O4·plan 문서만)

- 범위
  - D-418~D-467 50행의 사용자 확인 결과를 반영했다. 확정 43행·변경 확정 7행(D-420·D-433·D-436·D-450·D-454·D-459·D-465)이고 결정 139(139-1~139-8)·ADR-123(부록 `plan/90_결정관리/02_결정기록_부록/b45_ADR-123.md`)·원문 §3-0u(`plan/00_요구사항/01b_사용자요구사항_원문_3.md`)에 적었다. 새 REQ 는 없다.
  - 거래처 정본을 독립 공통 기반 모듈 `party` 로 옮겼다(`plan/08_데이터모델/party.md`·`plan/10_API설계/party.md` 신설, `prj.party` → `party.party`, `prj.PartyMerged` → `party.PartyMerged`). 결정 48 거래처 2층 구조는 그대로다. 국가 팩 `pack` 은 소유만 정하고 후보 등록부에 올렸으며, 근무 달력은 세분 공통 기반 `sch_cal` 이다. 공통 기반은 16개다.
  - 포트는 모듈 코드 안의 연결 규격이고 네트워크 포트를 새로 열지 않는다는 용어 문장을 48·50·52 와 골격 §8 에 넣었다. 교차 흐름의 실패·보상은 모두 `orc` 사가가 맡고(51·51a·46), 겹치는 모듈은 제품 고정가 + 겹침 할인율로 과금하며(20c·22b), 앱 잠금은 기본 켬이다(47·a01_IAM·14).
  - 골격 33~46 §8 의존 서술을 `plan/05_아키텍처/50_Pd_모듈_의존그래프.md` §3·§3.1 과 모듈별로 대조했다. 통합 대조에서 골격 쪽 어긋남 8건을 50 기준으로 고쳤다. 고친 곳은 38 의 `mal`→`crm` 담당자 조회, 40 의 `crm`→`iss` 조회, 43 의 `apr`→`col_booking` 포트, 45 의 `aip` 호출 모듈 목록, 35 의 `kms`→`col` 요약, 34·36·37 의 '공통 기반만 쓴다' 요약이다. 42 의 `prj` 가 `ctr.LegalMatterOpened` 를 구독하는 것과 43 의 `col.resource.asset_status`(`pur` 이벤트 사본)는 50 과 맞는다.
  - 사용자 확인 대상은 D-468~D-475 8행이다. 모두 권장안을 먼저 적용했다(`plan/90_결정관리/01_결정대기_부록/d468_결정139_확인대기.md`). 특히 D-473 은 `orc` 시스템 사가 런타임을 M4 에서 M2 로 앞당기는 일정 변경이라 사용자 답의 범위를 넘으므로 따로 확인을 받아야 한다.
  - 다음 번호는 결정 140·ADR-124·REQ-110·D-476·TODO-229 이다.
- 판정 결과
  - `check_dep_graph.py` 실측 결과는 모듈 41개·동기 간선 184·구독 간선 137·순환 0·알려진 위반 0·rc=0 이고, `--self-test` 는 8/8 이다. 50 §4.3 의 KV-01~40 은 'D 묶음별 해소 기록' 으로 바꿨다.
  - md_audit 는 rc=0(등재 213·고아 0·깨진 참조 0)이다. plan_audit 는 rc=0(errors 0·warnings 30, 기준선과 같음)이다. check_feature_detail 는 rc=0(238건, `--cite-check` 포함)이고 check_ai_usage 는 rc=0(238건·41행)이다.
  - check_m1_coverage 는 FAIL 7건으로 기준선과 같고 새 실패는 0 이다(모두 C4 `hrs`·`crm` 이벤트 목록 누락). 이 검사기의 스키마 목록에는 `party` 가 아직 없다.
- 변경 문서
  - 신규 4개는 `plan/08_데이터모델/party.md`·`plan/10_API설계/party.md`·`plan/90_결정관리/01_결정대기_부록/d468_결정139_확인대기.md`·`plan/90_결정관리/02_결정기록_부록/b45_ADR-123.md` 이다.
  - 수정한 문서는 결정 기록 7개(`plan/90_결정관리/01_결정대기_부록/d418·d436·d448`, `01_결정대기_질문목록.md`, `02_결정기록_ADR.md`, `03a_향후과제_TODO_2.md`, `01b` 원문)다. 아키텍처는 `plan/05_아키텍처/04`·`21f`·`22b`·`26`·`32`·`33`~`52` 를 고쳤다. 데이터·API·이벤트는 `plan/08_데이터모델/00_개요_ERD.md`·`90`·`adm`·`col`·`corp`·`crm`·`fin`·`hrs`·`kms`·`lic`·`pre`·`prj`·`sch`·`tim`, `plan/10_API설계/90`·`prj`, `plan/11_이벤트/01`·`01b` 를 고쳤다. 기능·요금·화면은 `plan/06_기능목록/20a`·`a01_IAM`·`a07_PRJ_1~3`·`a12_FIN_1·2`·`a14_CRM`, `plan/03_개발범위/20c`·`30`, `plan/09_화면설계/14` 를 고쳤다. 검사기는 `plan/_build/check_dep_graph.py`(self-test 1건 추가)·`fd_rules.py`(`party` 인용 스키마)를 고쳤고, 색인은 `plan/README.md`·`plan/index.json`·`.claude/rules/docs-map.md` 를 고쳤다.
- 이월 후보
  - 새 TODO 는 3건이다. TODO-226 은 `pack` 상세 이관(`fin.md` §3.13 요율 판·`adm.md` §3.6·`adm.RateTablePublished`·SCH-05 공휴일 시드)이고, TODO-227 은 `orc` 사가 M2 앞당김의 WBS 행과 일정 재산정이며, TODO-228 은 제품 고정가와 겹침 할인율 실제 값이다.
  - TODO-220 은 부분 처리다(⑤ 골격 §8 정합 완료, ①~④·⑥ 대기). `plan/03_개발범위/31a_Pd_일정표_M4_레인병렬_출시일정.md`·`31_Pd_일정표_마일스톤_세션배치.md` 의 gantt 와 일정 재계산은 TODO-227 로 넘겼고 TODO-179 도 남았다.

## 2026-11-16 — 결정 137 — 지문(생체) 인증·QR 스캔 도입(REQ-108·ADR-121, conv_179483081923, O3·plan 문서만)

- 범위
  - 사용자 요구 "지문인증을 pd 프로젝트에도 도입해야 한다."·"지문 및 QR도 pd프로젝트에 필요하니 완료후 인계하라" 를 REQ-108 로 등재하고 결정 137·ADR-121(부록 b43)·원문 §3-0t 에 반영했다. 구현은 pd-bb(front) 인계다.
  - 설계 정본 `plan/05_아키텍처/47_Pd_생체인증_QR_설계.md`(180줄)를 신설했다. 앱 잠금·보호 키·재인증·QR 표시·스캔·금지 3가지·설정 키·플랫폼 표를 담았고 값 근거는 ohoops 스파이크 0.45·0.48 실측(Android 만 확정, iOS·Windows·macOS 는 공식 문서 기준 후보)이다.
  - 새 기능·화면·메뉴·연계 번호는 없다. IAM-01·IAM-12·APR-08 블록을 확장했고 기능은 238건 그대로다. 앱 잠금 설정 자리는 M1~M2 셸 시스템 메뉴(SCR-PLT-01), M3 부터 SCR-IAM-05 안 영역이다.
  - 새 판단 D-436~D-447 은 권장안 적용(사용자 확인 대기)이다. 다음 D 번호는 D-448, 다음 TODO 번호는 TODO-219, 다음 REQ 번호는 REQ-109, 다음 ADR 번호는 ADR-122 이다.
- 판정 결과
  - 판정기 `c_fpqr_gate.py` 전체 실행은 CANARY(기대 FAIL)만 FAIL 이고 나머지 PASS 였다. 단 AC-1514(HISTORY 맨 위 결정 137 항목)는 이 항목을 쓰기 전 실행이라 FAIL 이었고 이 항목으로 닫는다.
  - 검사기 4종 rc=0 이다. check_feature_detail·check_ai_usage 는 a01_IAM·a01_IAM_2·a11_APR 단위와 전체, plan_audit 는 errors=0(기존 WARN 30), md_audit 는 고아 0·깨진 참조 0 이다.
- 변경 문서
  - 신규 `plan/05_아키텍처/47_Pd_생체인증_QR_설계.md`·`plan/90_결정관리/01_결정대기_부록/d436_결정137_확인대기.md`·`plan/90_결정관리/02_결정기록_부록/b43_ADR-121.md`.
  - 수정 `plan/00_요구사항/01_사용자요구사항_원문.md`·`plan/00_요구사항/01b_사용자요구사항_원문_3.md`·`plan/06_기능목록/21_기능요건상세_부록/a01_IAM.md`·`a01_IAM_2.md`·`a11_APR.md`·`plan/09_화면설계/06_모바일_화면설계.md`·`14_로그인_가입_통합진입_화면설계.md`·`15_외부참여_공유링크_화면설계.md`·`plan/90_결정관리/01_결정대기_질문목록.md`·`02_결정기록_ADR.md`·`03a_향후과제_TODO_2.md`·`plan/README.md`·`plan/index.json`·`.claude/rules/docs-map.md`·`HISTORY.md`·`LESSONS.md`.
- 이월 후보
  - D-436~D-447 사용자 확인, TODO-214~218(비 Android 플랫폼 실측·화면 잠금 제거 뒤 키 무효화·QR 스캔 실측 보강·WBS 증분 반영·pd-bb 구현 결과 반영).
  - 미실측으로 남긴 값은 백그라운드 중단 동작(설계 판단), 유효 시간 값 변경 시 키 재생성 필요 여부, 무GMS 스캔 촬영(카메라 처리 앱 조회만 참), 재인증 30초 외 값이다.

## 2026-11-16 — 테마 템플릿 선택 뒤 요소별 사용자 지정(REQ-101, 결정 131, ADR-115, goal G-THEME-C3, conv_179446127333, O4·/oto)

- 범위
  - 사용자 요구 "기본 테마(템플릿)을 선택후 각 요소마다 색상이나 폰트나 글자크기나 디자인항목들을 사용자가 커스터마이징 가능해야 한다." 를 PoC `poc/ui` 사용자 계층에 구현했다. 요소 10종(app·menuBar·docTitle·windowTab·button·input·gridHeader·gridCell·panel·statusBar)마다 색(기존 `PdBrush*` 매핑)·글꼴·글자 크기 오프셋·굵기·모서리·테두리·여백을 바꾸고, 속성·요소·전체 3단으로 되돌린다.
  - 편집 위치는 기존 테마 편집기 안 '요소별 디자인' 영역이다(새 메뉴 ID 없음, 1090 비워 둠). 내장 템플릿을 편집하면 사용자 사본을 자동으로 만든다(D-353). 저장은 바꾼 키만 희소 저장하고, 옛 저장본은 화면 불변이다.
  - 실효값은 사용자 ?? 회사(REQ-100, 접점만) ?? 템플릿 ?? 제품 기본값이다. 회사 잠금 공급자는 빈 값이다(TODO-172).
  - 적용 방식은 지정된 (요소, 속성)만 실행 중에 코드로 Style·Setter 를 만들어 Application.Styles 의 전용 묶음 1개를 교체하는 방식이다(D-4). 셀렉터·글자 크기 기준 토큰·제외 규칙은 데이터 표 `ThemeElements.Targets.cs` 에 둔다(REQ-99).
- 판정 결과(검증 루프 2회)
  - theme1 판: T1~T5·T12·T13·T14 PASS, T6~T11·T15 FAIL(정적 DynamicResource Setter 가 기존 렌더를 덮음, 글자 크기 기준 불일치, 웹 저장 복원·내보내기 경로 등). TEST→DEV 역라우팅(계획 `fix_c3_iter1.md`).
  - theme2 판: 웹 T7 17/18·Android T8 5/7·데스크톱 T6 에서 글자 크기 15 대 17(statusBar 기준 토큰 Caption), Android 숨은 메뉴 줄 재시작 유지 불일치. 다시 TEST→DEV.
  - theme3 판: T1~T15 전부 rc0(otest-6 데스크톱·정적, otest-7 웹 T7 18/18·T11·T14 74/74, otest-8 Android T8 7/7), goal CLI rc0, evidence/otest_done. 빌드 경고 데스크톱 8·웹 7·Android 7, 새 경고 0. src md5 7c724211…, APK md5 3ce3dc8f…, shell wasm md5 ca5b5e50….
- 변경 코드
  - 신규: `poc/ui/src/Pd.Poc.Shell/Theming/ThemeElements.cs`·`ThemeElements.Targets.cs`·`ThemeDesignApplier.cs`·`ThemeDesignStyles.cs`·`ThemeCatalog.Custom.cs`, `poc/ui/src/Pd.Poc.Shell/ViewModels/ThemeDesignViewModel.cs`·`ThemeDesignRow.cs`, `poc/ui/src/Pd.Ui.Views/ThemeDesignPanel.axaml(.cs)`, `poc/ui/src/Pd.Poc.Shell/Diagnostics/Remote/RemoteThemeDesignCommands.cs`.
  - 수정: ThemeApplier(같은 밝기 전환 창 Background)·ThemeApplierDiag·ThemeAutomation(웹 내보내기 JSON 반환)·ThemeCatalog·ThemeDefinition(Design·BaseTemplateId)·UiSettingsApplier(문서 래퍼 클래스)·ThemeEditorViewModel·ThemeTokenRow·RemoteCommandRouter·RemoteSettingsCommands(userThemesJson), theme.ko/en.json, PdApp·MainTitleBar·SdiTitleBar·ShellStatusBar(클래스 표식)·ThemeEditorView(Watermark→PlaceholderText, D-1).
  - 시험 자산: `poc/ui/test/common/measure/theme_custom_check.py`·`theme_custom_cases.py`·`theme_custom_web.py`, `poc/ui/test/common/testdata/theme_legacy_settings.json`, `poc/ui/test/mobile/measure/webperf_fold5.py`(세로 고정·방향 판독 수정, D-5), 각 README 색인 행.
- 변경 문서: `plan/09_화면설계/17_테마_요소별_사용자지정_설계.md`(신설, 마무리 때 코드 대조로 제외 클래스·글자 크기 토큰·숨은 컨트롤 재평가·대비 키 보강), `plan/90_결정관리/01_결정대기_부록/d350_테마사용자지정_확인대기.md`(신설), `plan/90_결정관리/02_결정기록_부록/b37_ADR-115.md`(신설, 131-2·131-4 개정 이력), 02 ADR(ADR-115 행·최신 결정 131), 01 공통원칙·01·01a 요구사항 원문(REQ-101·§3-0m)·질문목록·03 TODO(TODO-170~172)·plan/index.json·docs-map.md.
- 자율 발견(D-1~D-8): D-1 Watermark 경고 6곳 정리, D-2 판정기 fails 형식·웹 실행기 이관, D-3 글자 크기 기준을 요소 자기 기본 크기로, D-4 정적 Setter 폐기 → 코드 Style 생성, D-5 Fold5 측정 도구 세로 전제·방향 판독 결함 수정, D-6 제목줄 제외를 반사 대신 클래스로, D-7 판정기 대상 목록을 실제 파일 구성으로(검사 대상 1개 증가), D-8 Android 굵기 강등은 범위 밖(실효값 표시만).
- 정리: 웹 스모크 스크린샷 3장(`poc/ui/test/web/measure/web_smoke_theme1~3.png`)은 1회성 결과라 세션 보관소로 옮겼다. 동결본(r12f·theme1~3_freeze_*)은 보존했다.
- 이월 후보(번호 미배정)
  - 디자인 테마 요구(다음 사이클, 번호는 다른 세션 pd-8c 종료 뒤 확인).
  - A16 Fold5 재측정 — 결과가 오면 반영한다(직전 r12 값 유지 중).
  - Android 굵은 글꼴 번들 — 번들 글꼴이 Regular 하나라 Bold·SemiBold 가 Normal 로 강등된다(D-8).
  - input.fontSize 측정 대상 — 원격 측정이 문서 안 TextBox 를 잡아 설계상 되돌린 값을 읽는다. 셸 입력칸으로 대상을 옮길지 검토한다.

## 2026-11-15 — A15 해소·끌기 시작 멈춤 A29 신설과 정지 창 찾기 수정, 진단 계측 frameDiag·놓은 뒤 복원 분산 옵션 P2, r12e·r12f 동결(conv_179446127333, O4·oto, 검증 루프 2회차)

- 범위
  - 진단 계측 P1 `chrome.frameDiag`(PD_DIAGNOSTICS 빌드 한정, rAF 마다 GC·할당·레이아웃·커밋·단계 시간 기록)를 넣었다(`MdiDragFrameDiag.cs`·`MdiDragFrameDiag.Stages.cs`·`MdiSnapDrag.Timing.cs`·`MdiTabJoinDrag.Timing.cs` 신설, `PerfCommitLog.cs`·`PerfLayoutLog.cs`·`MdiTabJoinDiag.cs`·`RemoteWindowsCommands.MoveLight.cs` 수정). 제품판에 계측 문자열이 없음을 obuild 가 확인했다.
  - 옵션 P2 `MdiMoveLightRestorePerFrame`(놓은 뒤 정지 창 복원을 프레임마다 N개씩, 기본 0)를 넣었다(`MdiMoveLight.RestoreSpread.cs`·`MdiStaticLight.Restore.cs` 신설, `MdiStaticLight.cs`·`MdiMoveLightener.cs`·설정 DTO·`UiSystemSettings.Spread.cs` 수정). 비교 규칙 G5 는 측정 전에 01a 에 적었다.
  - r12e 진단 측정 108회(공식값 아님, 무효 0): doc5 초과 프레임은 커밋 뒤 합성·그리기 구간이고, 놓은 뒤 복원은 판정 창 밖이라 G5 는 실행하지 않았다(D-51). 앞선 r12d 진단의 '놓기 안에서 정지 창 해제' 추정은 코드와 달랐다.
  - 같은 진단에서 문서 창 1개 끌기 시작 멈춤(139~223ms)을 찾아 새 수용 항목 A29(시작 구간 p95 ≤100ms, 초기 제시값, 출처 web.dev RAIL)를 만들었다(D-50). 분석 모듈 `poc/ui/test/web/measure/drag_start_gap.py` 를 신설했고 `w10_web.py` 에 연결했다. `MdiStaticCache.cs` 는 정지 창 찾기를 MDI 배치 패널에서 멈추고 정지 창이 0개면 건너뛰게 고쳤다. 동작이 달라지는 경우는 1건이다(끄는 창 패널에 그 창뿐이고 다른 영역에 창이 있으면 다른 영역 창에 가벼운 크롬을 걸지 않음).
  - r12e(0a8a79e099a3997803c8e9775cdc74f6)·r12f(1d0b0d9a153bfa9c677987aa6765d288)를 동결했다.
  - C: 드라이브가 가득 차 odone 정본 수정이 I/O 오류로 막혔다. 리드 결정으로 r12e 동결 폴더 5개와 Desktop `bin/--help`(빌드 출력 오입력 폴더)만 지웠다. 가용 공간은 약 1.1GB 다.
- 판정(r12f 공식, 설정 키 없는 새 프로필, 적용값 확인, 무효 0)
  - A15 PASS: doc1 21.0·doc3 21.7·doc5 22.5ms(원래 p95 21.1·22.0·33.9, 제외 1·1·22). 직전 r12d 는 25.2ms FAIL 이었다. doc5 개선이 수정 효과인지 회차 흔들림인지는 가르지 않았다.
  - A29 PASS: 38.2·57.3·54.6ms. 수정 전(r12d 원자료 재분석)은 222.6·54.0·49.2ms 였다.
  - A19 44/44, A20 78/78, 비침 0.0, A17·A18 PASS. load1 5.5~28.3 은 참고만 했다.
  - A16(Fold5 웹 문서 열기)은 기기 분리로 blocked 다(직전 r12 펼침 540.2ms FAIL 기록 유지).
- 변경 문서: `plan/09_화면설계/01a_화면설계_공통원칙_끌기경량크롬.md`(P2·P1·G5 규칙, r12e 진단 요약, A29 기준, r12f 공식 결과, K7 ① 기본 적용 표기), `plan/04_기술스택/04_판정보고서_부록/a10_측정보류_목록.md`(H-02 해소(PASS)·r12f 공식), `poc/ui/README.md`, `poc/ui/test/README.md`.
- 이월 후보
  1. A16 Fold5 r12f 재측정(기기 재연결 뒤).
  2. 놓은 뒤 약 0.3초 복원 몰림 미측정(P2 옵션은 기본 끔).
  3. 설정 저장이 새 기본값 전파를 막는 문제(D-47).
  4. `poc/ui/test/web/measure/w10_web.py` import 시 main() 재실행·888줄, `poc/ui/test/mobile/measure/webperf_fold5.py` 863줄, `poc/ui/src/Pd.Ui.Views/MdiMoveLightener.cs` 304줄(300줄 규칙 초과).
  5. Windows 127.0.0.1:9229 bind 실패 원인 미확인.
  6. 이번 사이클 checkpoint.jsonl 미기록으로 재기동 자동 복원 실패.
  7. 팀원 메시지 전달 지연.
  8. 비움 감시기 주기·부하(사용자 선택 대기).
  9. 큐 대기 요구 2건: 테마 요소별 커스터마이징, TODO-149 GPU 폴백 PoC.
  10. C: 드라이브 가득 참(2026-11-15 12:1x, 1TB 중 100%) — 이 worktree 산출물 약 38GB, WSL ext4.vhdx 약 194GiB(안쪽 사용 125GB), 나머지 약 700GB 미확인, 정리 후보(옛 판 bin/run 약 17GB·obj 3.7GB·logs/pcaway 5.3GB 등)는 사용자 결정.
  11. `poc/ui/test/desktop/measure/user_away_guard.py` 의 `pd_away_ps_*` 임시 폴더 27개 잔여(11-07~08, 강제 종료 때 정리 안 됨).

## 2026-11-14~15 — 끌기 합류 아이콘 근접 표시(옵션 A) 기본 적용·옵션 B·G3/G4 비교·r12d 동결, Fold5 r12 측정·측정 도구 좌표 수정(conv_179446127333, O4·oto, 반복 1회차 중간 커밋)

- 범위
  - 옵션 A `MdiTabJoinArmNear`(포인터 근처 창에만 합류 아이콘 표시, 반지름 96)를 넣었다(`MdiTabJoinDrag.Near.cs`·`MdiMoveLight.Spread.cs`·`UiSystemSettings.Spread.cs` 신설, 진단 `MdiTabJoinDiag.cs`).
  - 옵션 B `MdiMoveLightSiblingsPerFrame`(끌기 중 다른 창 갱신 분산)를 넣고 기본 0 으로 두었다.
  - G3 비교(측정 전 01a 에 규칙 확정): 끔 doc5 합친 p95 30.1, A 23.2, B 27.2ms. A·B 모두 조건 충족, 기본값은 A 만 켰다(D-46). A+B 는 측정한 적이 없어 채택하지 않았다.
  - r12d 를 동결했다(소스 해시 6aa4e8d5cd40f9e7641fc7cb225a28da). 저장된 설정이 새 기본값보다 우선하므로 공식 측정은 설정 키가 없는 프로필로 했다(D-47).
  - G4 비교(A 대 A+B spf=1, 18쌍, 무효 2건 재측정): 조건 ①② 미충족(doc5 중앙 A 20.85 / AB 21.9, AB 낮은 쌍 6/18) → A+B 미채택, 기본값 A 만 유지(D-49).
  - Fold5 r12 측정(PC 사용 중 측정, 사용자 지시)을 했다(D-43·D-45).
  - 측정 도구 `poc/ui/test/mobile/measure/webperf_fold5.py` 의 dy 계산이 보이지 않는 첫 navigationBars 항목(높이 0)을 읽어 펼침에서 다른 문서(간트)를 열던 결함을 고쳤다(D-44). 순수 함수 `poc/ui/test/mobile/measure/webperf_insets.py` 를 신설했고 self-test 3/3 PASS 다.
  - 진단 원격 `chrome.moveLight` 에 옵션 A·B 인자를 더했다(`RemoteWindowsCommands.MoveLight.cs`). `MdiStaticLight.cs`·`MdiMoveLight.cs`·`MdiTabJoinDrag.cs`·설정 DTO 를 함께 고쳤다.
  - TODO-124 B분: 01a·a10 의 임시 경로 16곳을 `<세션 기록>/` 표기로 바꾸고 "저장소 미수록(세션 원본)"을 적었다(저장소에 있는 자료 0건).
- 판정
  - A15 FAIL: r12d 공식 doc1 21.5·doc3 21.7·doc5 25.2ms(D-39 정의), 기준 ≤25ms. doc5 가 0.2ms 넘는다(D-48). G4 의 A 조건 doc5 22.6 은 비교용 측정이라 공식값으로 쓰지 않았다. 검증 루프는 계속한다(반복 1/5).
  - A19 PASS 44/44, A20 PASS 78/78, 비침 0.0.
  - A16 FAIL(Fold5 r12, r12d 아님): 웹 문서 열기 펼침 p95 540.2ms, 접힘 329.3ms. r8 은 544.8·341.3ms 였다. A21 Fold5 회귀 70/70 PASS.
  - otest-2: gate A15 rc=1·A19 rc=0·A20 rc=0, self-test rc=0, md_audit rc=0, plan_audit rc=0.
- 변경 문서: `plan/09_화면설계/01a_화면설계_공통원칙_끌기경량크롬.md`(옵션 A·B, G3·G4 규칙과 결과, r12d 공식, 진단, 다음 수단, 탭 막대 위치 D-254), `plan/04_기술스택/04_판정보고서_부록/a10_측정보류_목록.md`(H-02 결과, H-04 Fold5 r12), `poc/ui/README.md`(chrome.moveLight 인자·경로 정정), `poc/ui/test/README.md`(webperf_fold5 대응·webperf_insets 행·안전 규칙 7·8).
- 이월 후보
  1. A15 다음 수단: 끌기 중간 긴 렌더 원인 계측(P1), 놓기 작업 분산(P2), Avalonia 12.1.6.
  2. A16 r12d 판으로 Fold5 재측정.
  3. D-47 저장된 설정에 새 기본값이 전파되지 않는 문제(설정 저장 구조 설계 사안).
  4. 비움 감시기 PowerShell 프로브 부하(1회 0.649초, 10초 주기).
  5. `poc/ui/test/mobile/measure/webperf_fold5.py` 863줄·`poc/ui/test/web/measure/w10_web.py` 887줄 300줄 규칙 초과 부채.
  6. 이번 사이클 checkpoint 를 기록하지 않아 재기동 뒤 자동 복원에 실패했다.

## 2026-11-14 — r9~r11 동결 루프·A15 웹 끌기 판정 정의 변경(conv_179446127333, O4·oto, goal G-057ACFC1)

- 범위
  - K1~K3 은 기본으로 켰다. 그랩존 캐시가 배치 항목(ContentPresenter)에서 우회되던 문제를 고쳤고(`MdiGrabZoneCache`), 끌기 중 접기 판정을 건너뛰며(`MdiStripFold`), 끌기 중 창 내용 히트 테스트를 끈다(`Pd.Ui.Views/MdiMoveHitTest.cs` 신설).
  - K5 포인터 합치기(`MdiMovePointerCoalesce.cs` 신설)·K6 놓기 프레임 반영·K7 정지 창 미리 계산은 옵션으로 넣고 기본 끔으로 두었다. G2 비교를 넘지 못했다. K4 비교와 웹 RenderDirtyRegions 9쌍도 채택하지 않았다. K8 진단에서는 Pd 코드로 줄일 몫을 찾지 못했다(D-41).
  - 진단 원격 `chrome.moveLight` 를 `RemoteWindowsCommands.MoveLight.cs` 로 나눴다(300줄 규칙).
  - 사용자 결정 D-39 로 A15 판정을 '화면 변화 없는 간격 제외 p95'로 바꾸고 원래 p95 를 함께 적는다(c12 개정, 정본 `plan/09_화면설계/04_웹_화면설계.md` §4). 판정 도구 `poc/ui/test/web/measure/drag_static_gaps.py` 를 새로 만들었고 `w10_web.py` 가 이를 부른다.
  - A19 h15 실행기 `poc/ui/test/desktop/measure/h15_binding_check.py` 를 저장소 안에 다시 만들었다(옛 `h15b_run` 소실).
  - r9·r10·r11 을 동결했다. r11 소스 해시는 7a579ddf275749770923e55e96ad4e10 이다(otest-2 확인).
- 판정
  - A19 PASS: r11 데스크톱 회귀 44/0, h15 PASS.
  - A15 FAIL: D-39 정의 판정값 문서1 24.0·문서3 22.4·문서5 26.8ms(원래 p95 25.7·30.1·38.5ms), 기준 ≤25ms. 문서5 가 넘는다. 다음 사이클로 넘기며 PASS·면제로 쓰지 않는다.
  - A16 blocked: Fold5 측정은 이 사이클에서 분리했다.
- 정본 문서: `plan/04_기술스택/04_판정보고서_부록/a10_측정보류_목록.md`(A15 미달 이월·문서5 한계), `plan/09_화면설계/01a_화면설계_공통원칙_끌기경량크롬.md`(K1~K8 옵션 판정 정리), `plan/09_화면설계/04_웹_화면설계.md`(D-39 판정 정의). `poc/ui/test/README.md` 에 새 도구 2개를 등재했다.
- 이월 후보
  1. A15 문서5 26.8ms 원인 처리(시작·놓기 프레임의 캡처 해제 IsPointerOver 연쇄·흩어진 5창 다시 그리기, D-41).
  2. A16 Fold5 펼침 재측정.
  3. `w10_web.py` 887줄 300줄 규칙 초과 부채 정리.


## 2026-11-14 — GPU 모드 끄기 3경로·개인 플래너 진입점·Pd 전환 깔때기 정본 반영(결정 125, conv_179462673836, O4)

- 범위
  - 문서 전용 사이클이며 제품 코드(poc 포함) 변경은 없다.
  - GPU 모드 끄기 3경로(기동 파라미터 `--gpu off`·시스템 메뉴·전역 단축키 Ctrl+Alt+Shift+G 초기 제시값), 원격/GPU 부재 자동 감지, 실패 폴백을 정했다(05 §3.5.1, a05 PLT-11, 03_PoC G1~G6).
  - 개인 플래너 진입점(가칭 Pd 플래너, 같은 코드·데이터 구조, Free+Personal)과 플래너 → Pd Free → Professional 깔때기를 정했다(05 §1.3, 08 §3.10, 20c §6.2.2, 화면 16 신설, 업종 팩 개인 팩 PER-01, 30a/30b/31a §13).
  - 등재는 REQ-93·94, ADR-109(b31), D-318~D-328(d318), TODO-149~151, I-49 지도 연동 후보이며 ADR-108 대안 ① 을 개정했다. 새 기능 ID 는 없고 기능 236건은 불변이다.
- 판정 결과: otest PASS(FAIL 0), 계약 AC-314·317·318 은 `c125_gate.py` 로 확인한다.
- 변경 문서: 변경 27건(수정 23 + 신설 4). 신설은 `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-36/plan/00_요구사항/01a_사용자요구사항_원문_2.md`, `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-36/plan/09_화면설계/16_개인플래너_전환유도_화면설계.md`, d318 확인대기 부록, b31 ADR-109 부록이다.
- 이월 후보
  1. D-318~D-328 11행 사용자 확인 대기(권장안 적용 중).
  2. 지도 공급자(I-49)·플래너 정식 이름은 후보 상태이다.

## 2026-11-14 — 외부 참여 3경로·공유 링크·게스트 계정 수·개인 모드·연구개발 팩·전면 옵션화 정본 반영(결정 124, conv_179462673836, O4)

- 범위
  - 문서 전용 사이클이며 제품 코드 변경은 없다. 결정 123 의 D-302~D-305 4행을 확정 1건·변경 확정 3건(D-302 자동 회수, D-304 유예 7일, D-305 외부 참여 3경로)으로 종결했다.
  - 외부 참여 3경로(교차 테넌트·게스트 계정 또는 정식 좌석·공유 링크)를 정했고, 공유 링크는 APR-08 을 확장했다. 게스트 계정 포함 수는 LIC-05 에 넣었고, 개인 모드 Personal 은 PLT-09 규모 모드 5단계(0단계)로 넣었다. 연구개발 팩을 정식 승격하고 7블록을 확장했으며, 전면 옵션화(REQ-92)를 적용했다.
  - 등재는 REQ-89~92, ADR-108, D-306~D-317, TODO-146~148, WBS-M2-52, WBS-M4-47·48 이다. 새 기능 ID 는 없고 기능 236건은 불변이다.
  - 신설 3파일은 `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-36/plan/09_화면설계/15_외부참여_공유링크_화면설계.md`, `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-36/plan/90_결정관리/01_결정대기_부록/d306_결정124_확인대기.md`, `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-36/plan/90_결정관리/02_결정기록_부록/b30_ADR-108.md` 이다.
- 판정 결과: goal_autoscript_run odone 전 14/14 PASS, 검수 AC-201~226 PASS(CANARY 는 설계상 FAIL), check_feature_detail 4모드·check_ai_usage·plan_audit·md_audit rc=0, 300줄 초과 md 0. otest_done 마커 PARTIAL 은 CRUD 검증기의 문서 사이클 오탐으로 unneeded 판정했다.
- 변경 문서: 변경 47건(수정 44 + 신설 3). `.claude/rules/docs-map.md`, `plan/README.md`, `plan/index.json`, `plan/00_요구사항/`, `plan/03_개발범위/`, `plan/06_기능목록/`(부록·생성물 csv·xlsx), `plan/08_데이터모델/`·`plan/10_API설계/`(apr·iam·lic), `plan/09_화면설계/`, `plan/11_이벤트/`, `plan/90_결정관리/`, `plan/_build/features.py`·`features_data.py`.
- 이월 후보
  1. 31 일정표 gantt 막대 재작도.
  2. GPU 모드 3경로(기동 파라미터·본체 on/off·단축키) 요구는 결정 125 사이클에 큐잉.
  3. D-306~D-317 12행 사용자 확인 대기(권장안 적용 중).

## 2026-11-13 — 이월 전부 처리·카드 뒤 글자 비침 판정·.NET 11 전환(conv_179446127333, O4, Phase Batch 2/2)

- 범위
  - 이월1 비활성 창 호버 대비. 비활성 전용 토큰 `PdBrushChromeHoverInactive`(별칭 `PdChromeHoverInactive`)를 내장 테마 7개에 신설했다. 비활성 캡션 줄 Opacity 0.75 를 반영한 실효 대비가 3.25~3.29 이다(이전 라이브 1.20~2.07). 비활성 호버 규칙은 항상 로드되는 `ChromeButtonStyles.axaml` 에 둔다.
  - 이월2 `chrome_button_states.py` 에 `INACTIVE_HOVER` 줄과 `on_glyph_vs_off_glyph` 규칙을 넣었다(켜짐·꺼짐 기호 화소 RGB 거리 ≥60, 켜짐 기호 대 바탕 대비 ≥3.0). 낡은 '켜짐 바탕=기본 바탕' 규칙을 없앴다.
  - 강조 띠 위 기호 색(odev-b2·b3). 누름 `#2E1D00`(pd-contrast 만 흰색, ember 표면 줄 `#FFE9D2`), 호버는 밝은 바탕 4개 테마 `#FFF4CC`·ember/forest/contrast `#00007A`, 켜짐(`PdBrushChromeGlyphCheckedOnAccent`)은 pd-dark `#FFFFFF`·pd-contrast `#5A0A3E`·그 밖 `#FFE066` 이다. 호버·누름 대비 3.31~5.03, 전 상태 최저 3.07, 켜짐·꺼짐 거리 최저 88. 합치기 아이콘 바탕은 활성 제목줄 불투명색(`PdChromeTitleActive`)이다.
  - 제품 코드 분할(300줄 규칙). `SnapAssistLayer`(260/211/213줄)·`SnapAssistSession`(243/286/124줄)은 partial 로, `main.js` 는 ES 모듈(`host_bridge.js`)로 나눴다.
  - H-15 빈 패널 머리줄 Binding 오류. 원래 두 경로(`ActiveDockable.Title`·`Owner.Factory.CloseDockable()`)는 구판에서도 재현되지 않아 수정 효과는 입증하지 못했다. 새로 `DockAdornerHost`→`$visualParent[MdiDocumentControl]` 6건이 구판 b6 에도 있어(b6 소스 해시 미확인) 다음 사이클로 이월한다(D-2, Dock.Avalonia 기본 템플릿 쪽 추정).
  - 시험 도구. `snap_assist_fg.py` 에 A9(`space_enter`·`pressed_capture`·`topmost_live`·`dockjoin_live`·`focus_ring_2px`)·A10(`H-09`·`H-11`·`H-13`·`H-14`), `retest_pcaway_extra2.py` 에 `B2S`(H-07), `retest_mdi_drag_web.py` 에 `--snap off|on|keep`, `c5_splash_judge.py` 에 `ff_aux_ms` 대체 판정, `snap_assist_remote.py` 에 `case_backdrop`·`--cases`·`--themes`, `w24_snap_assist.py` 에 `--steps bleed`·`--cdp`·`--ui-mode`·`--viewport`·`--dpr`, `webperf_fold5.py` 에 RemoteDisconnected 폴백, `retest_android_pcaway_panel.py` 에 `--diag-release`, `perf_ui.py restore()` 의 배율 1.0·테마·그림자 복원, `retest_android.py` 의 `ADB` 환경변수·`PD_KEEP_APP`, `check_assembly_closure.py` 의 net11 어셈블리 스토어(XAZS/Zstandard) 지원을 넣었다.
  - 카드 뒤 글자 비침. 대조군 e2fc424 의 비침 화소 비율은 데스크톱 0.2805(light)·0.4926(dark)·0.3044(contrast), 웹 0.2804·0.4937·0.3055 였고, 현행 판은 세 테마 모두 0.0 이라 재현되지 않아 소스를 고치지 않았다. 원인은 남은 칸 판 반투명(대조군 ZoneOpacity 0.6)이며 adcaabe 에서 1.0 으로 이미 해소됐다. cause 는 '현행 판 재현 안 됨, 사용자 앱 구판 추정' 이고 사용자 앱 판은 확인하지 못했다. r4 동결판 `bleed.json`(A17)은 9칸(3폼팩터×3테마) 0.0, 대조군 0.492597 이다(Fold5 는 PC형 강제 장면).
  - .NET 11 전환. csproj 8개를 `net11.0`·`net11.0-browser`·`net11.0-android` 로 바꿨고 `tools/*.sh` 출력 경로는 `net11.0` 이다. `poc/ui/global.json` 은 SDK 11.0.100 이상 중 가장 높은 것을 쓰는 `rollForward: latestMajor`, `Directory.Build.props` 가 `Pd.Poc.Desktop` 에만 `RollForward=LatestMajor` 를 준다. Android SDK·JDK 경로는 환경변수 `PD_ANDROID_SDK`·`PD_JAVA_SDK` 로 넘긴다(이 PC 는 `%USERPROFILE%\pd-android\{jdk,sdk}`). SDK 11.0.100·런타임 11.0.0, 워크로드 android 37.2.31·wasm-tools 11.0.100(obuild-2 보고). 웹은 Emscripten 6.0.2 와 SkiaSharp 3.119 불일치(`saveSetjmp`)로 `Pd.Poc.Browser.csproj` 에만 SkiaSharp 4.158.0-preview·HarfBuzzSharp 15.0.0-preview 를 넣고 AOT 에서 뺐다.
  - D-18 SDI 창 포커스 진입 규칙(Tab 은 첫 항목, Shift+Tab 은 마지막 항목, 자동 포커스 없음)을 01 §7 에 보강했다. D-20 Fold5 비침 측정은 PC형 강제 MDI 장면에서 한다(11 §3.9). D-21 `pc_away_monitor.py` 가 부하로 3.7초 걸린 끌기 주입을 사용자 입력으로 오판해, 끝나지 않은 `*_pre` 주입은 pre 시각부터 최대 10초까지 주입 창으로 보게 고쳤다(self-test 15/15, 데몬 PID 733223 재시작).
  - D-22 판본 3개 A/B 17회 측정에서 b2→b3 코드 회귀 근거는 없었고 34표본 p95 는 느린 프레임 1~2개에 좌우된다. D-23 기준은 유지하고 표본 ≥150 판정 회차를 미리 정한다(회차 고르기 금지, 웹 H-02 는 문서마다 12회 합친 p95). D-26 Docking 9개 파일 성능 수정을 r4 에 넣었고 회귀(A19)는 PASS, H-01 개선 효과는 측정되지 않았다. D-27 은 perf_ui 배치 그룹(문서 20개, `docs.arrange` 반복 구간, r3 새 앱 `--frame-pump off`) 실행 중 앱이 예외 로그 없이 종료된 건이다(앱·데이터 로그·stdio 에 예외 없음, Windows 이벤트 로그 기록 없음, 같은 조합의 다른 회차는 정상 종료, 원인 미확인, 재현 시도 안 함, 근거 `work/c179446/diag24/probe/crash_note.json`). 처리 route 를 처음에 'blocked' 로 잘못 적었고 'carryover(다음 사이클 이월)'로 정정했다. D-28 H-04 공식 도구를 헤드리스 참고 도구 `a7_docopen_web.py` 에서 Fold5 실기 `webperf_fold5.py`(지표 doc 행 `settle_ms` p95)로 정정했다. H-13·H-09J 는 도구 원인(로그 파일 오독, 같은 위치 SDI 창 혼동, SDI 에서 나올 수 없는 판정 조건)이었고 고친 뒤 PASS 다.
  - 정본 문서. 01 §4.2·§4.4·§7, 11 §3.9, a10 §1.5·H-04, 05 창크롬 기준, 시험 README, `poc/ui/README.md` 를 고쳤다.
- 판정은 최종 게이트(r4)로 PASS 22건이고 FAIL 3건은 다음 사이클로 이월한다(아래). H-16 기준 판정은 PASS(중앙 225·최대 298ms, 기준 중앙 ≤250·최대 ≤400)다. 동결 소스 해시는 r4 3347f5205e6688e58656d6a069de2617 이며 데스크톱·Android·웹 3종이 같다(근거 `work/c179446/build_freeze_r4.json`). r3 는 4c7ece99166eaa09547fdbef79ecdc7c 이고 b3r2 와는 `TabFocusScope.cs` 하나가 다르다(HEAD 판으로 치환한 대체 해시가 b3r2 와 일치, 처음 보고된 e7732a99… 는 다른 사본 계산 추정).
- 이월 후보
  1. A11 H-01 B2 20창 커밋 p95 FAIL(perf_ui 뒤 43.87·새 앱 17.84ms). A15 H-02 W4 웹 문서 끌기 p95 FAIL(문서1 28.3·문서3 29.7·문서5 37.7ms). A16 H-04 A7 Fold5 문서 열기 settle p95 FAIL(펼침 722.3·접힘 488.2ms, 기준 ≤500). 모두 다음 사이클에서 원인을 확정해 고친다.
  2. D-2 `DockAdornerHost` Binding 오류 6건(Dock.Avalonia 기본 `MdiDocumentWindow` 템플릿 추정, 소스 확인 필요).
  3. D-3 비활성 창 켜짐 기호 대비 기준 부재(pd-ocean 1.88·pd-forest 1.81, 정적 모델 값, 라이브 미측정). D-27 perf_ui 배치 그룹 중 앱 무로그 종료(원인 미확인, 재현 조건은 crash_note.json).
  4. H-12 이월분: 데스크톱 Snap 켬 시나리오 정렬, `real_resize_*` 4건 원인, `redock_left` TypeError. 이월 8 커서 원위치 복원(가드 결정). `snap_assist_fg.py`(약 1174줄) 300줄 규칙 분할.
  5. Avalonia 12.2(PR #22196)가 NuGet 에 나오면 웹 SkiaSharp 4.x 프리뷰 우회를 뺀다(Avalonia 12.1.4 와 SkiaSharp 4.x 프리뷰는 보증된 조합이 아니다).
- 커밋 제외는 INFRA.md, `poc/ui/device_lease/`, `poc/ui/test/desktop/measure/pd_away_ps_*`, 로그·생성물·bin/obj 다.
## 2026-11-14 — 게스트 단일 모델·게스트 공간·초대 종료 조건 정본 반영(결정 123, conv_179461404263, O4)

- 범위
  - 문서 전용 사이클이며 제품 코드 변경은 없다. 결정 122 이월이던 D-298·D-299 를 확정하고 D-301 을 변경 확정했다.
  - 게스트와 외부인을 kind=guest 하나로 합치고 '외부인 공간' 을 '게스트 공간' 으로 바꿨다(external 후보 폐기). 마지막 super 는 거부하고 후임 지정을 강제한다(무결성 예외). 조직 상하 권한 세부 옵션 4종은 유지하고 원천은 부서장 명단이다.
  - REQ-88(게스트 초대 종료 조건 필수 — 기간 또는 마일스톤, 자동 회수, 연장 승인, 알림 7일은 초기 제시값, 보안 예외), ADR-107(`plan/90_결정관리/02_결정기록_부록/b29_ADR-107.md` 신설), 신규 D-302~D-305(부록 `plan/90_결정관리/01_결정대기_부록/d302_결정123_확인대기.md` 신설), TODO-145 를 등재했다. 새 기능 ID·화면·메뉴 ID 는 없다.
  - 이월 3건을 정리했다(20c D-243 확정 표기, 00 원문 162행·b27 85행 토큰 중복 제거, 31 §6 gantt 를 M2·M4 재산정 값으로 재작성).
- 판정 결과: goal AC-101~114 odone 전 11항목 PASS, 검수 AC-115~123 PASS(CANARY 는 설계상 FAIL), check_feature_detail 4모드·check_ai_usage·plan_audit·md_audit rc=0, 변경 md 300줄 이하, 자격증명 없음. AC-109·111·112 는 odone 뒤 판정.
- 변경 문서: `.claude/rules/docs-map.md`, `plan/README.md`, `plan/index.json`, `plan/00_요구사항/` 원문, `plan/03_개발범위/`(20c·30a·31·32), `plan/06_기능목록/`(a01_IAM·a01_IAM_2·20a·20d·21·01 상세·생성물 csv·xlsx), `plan/08_데이터모델/`·`plan/10_API설계/`(iam·org), `plan/09_화면설계/14`, `plan/11_이벤트/`(01·01a), `plan/_build/features_data.py`, `plan/90_결정관리/`(질문목록·ADR·TODO·b27·b29·d293·d302).
- 이월 후보
  1. 다음 확인 대기 D-302~D-305 4건(권장안 적용 중).
  2. 원본 폴더 .git/index.lock 0B 잔류 재발 여부 점검.

## 2026-11-13 — AI 할당량 배분·조직 상하 권한·롤 지정 정본 반영(결정 121, conv_179457159311)

- 범위
  - 사용자 요청(AI 할당량 총량 = 요금제 + 추가구매를 부서별·개인별로 절대값(토큰)·비율 두 방식으로 배당, 권한은 조직도 상하관계 + 롤 생성·관리 + 권한 설정에서 롤 지정)을 plan/ 정본에 반영했다. 제품 코드 변경은 없다.
  - 묶음 처리 사실. 같은 사이클에 D-277~D-280 확정(모두 권장안) 반영을 함께 처리했다. 메인 판단으로 묶었다.
  - 결정 121(121-1~121-7), ADR-105(`plan/90_결정관리/02_결정기록_부록/b27_ADR-105.md`), REQ-84·REQ-85, 기능 IAM-12·IAM-13·AIP-22 신설(기능 233→236, M2 75·M4 62), 신규 D-281~D-292(확인 대기, `plan/90_결정관리/01_결정대기_부록/d281_결정121_확인대기.md`), TODO-140·141, WBS-M2-50·51, WBS-M4-46 을 등재했다.
  - 31a §10 재산정: 출시까지 504~859.5일(추정).
- 판정 결과: otest AC-001~023 PASS(CANARY 는 설계상 FAIL), 검사기(check_feature_detail·--wbs·--cite-check·--self-test, check_ai_usage) rc=0, plan_audit·md_audit rc=0, 300줄 초과 0, 보호 파일 변경 0.
- 변경 문서: `.claude/rules/docs-map.md`, `plan/README.md`, `plan/index.json`, `plan/00_요구사항/01_사용자요구사항_원문.md`, `plan/03_개발범위/`·`plan/05_아키텍처/`·`plan/06_기능목록/`(신규 `a01_IAM_2.md`·`a15_AIP_3.md` 포함)·`plan/08_데이터모델/`·`plan/09_화면설계/`·`plan/10_API설계/` 관련 문서, `plan/_build/menu_data.py`, `plan/90_결정관리/` 질문목록·ADR·TODO·부록 b27·d281.
- 이월 후보
  1. D-281~D-292 사용자 확인 대기 12건.
  2. features.mvp 167 대 169 정의 미확정.
  3. goal.json G-1204D277 이 locked 로 잔존해 새 goal 은 만들지 않았다.

## 2026-11-13 — 확인 대기 D-238~D-267 사용자 답 정본 반영(결정 120, conv_179454591582, O4)

- 범위
  - 확인 대기였던 D-238~D-267 30건에 대한 사용자 답을 plan/ 정본에 반영했다. 제품 코드 변경은 없다.
  - 결정 120, ADR-104(`plan/90_결정관리/02_결정기록_부록/b26_ADR-104.md`), REQ-80~83, 신규 D-277~280(확인 대기, 부록 `plan/90_결정관리/01_결정대기_부록/d277_결정120_확인대기.md`), TODO-137~139 를 등재했다.
  - 기능 PLT-11·PLT-12 를 신설하고 WBS-M4-43~45(예약 페이지·게스트 계정 포함)를 추가했다. 업종 팩 확대는 `plan/03_개발범위/09_업종팩_확대_기본템플릿.md` 에 정리했다(업종 25·템플릿 75).
  - 저장 공간 합산 한도, 전역 단축키(D-278 1안은 확인 대기), 프롬프트 4단계, 게스트 예약 무료 범위 등을 기능 부록·데이터 모델·API·화면 문서에 맞췄다.
  - 기능 233건(M4 61), 출시까지 일정 재산정 499.5~843.5일(추정). 다음 번호는 결정 121·ADR-105·REQ-84·D-281·TODO-140 이다.
- 판정 결과: otest AC 22/22 PASS(역라우팅 2회), 검사기 5종 rc=0, md_audit rc=0(고아 0·깨진 참조 0), plan_audit rc=0(경고 27, 기록만).
- 변경 문서: `.claude/rules/docs-map.md`, `plan/README.md`, `plan/index.json`, `plan/00_요구사항/01_사용자요구사항_원문.md`, `plan/03_개발범위/`·`plan/05_아키텍처/`·`plan/06_기능목록/`·`plan/08_데이터모델/`·`plan/09_화면설계/`·`plan/10_API설계/` 의 관련 문서, `plan/90_결정관리/` 질문목록·ADR·TODO·부록 b23·b24·b26·d237·d242·d257·d263·d277.
- 이월 후보
  1. TODO-137~139(예약 API·프롬프트 엔터티와 API·보고 주기 기준 데이터 설계 선행 등).
  2. core.md 게스트 열 요청(DM-CORE-01) 미반영.
  3. `plan/index.json` 의 D-237·REQ-75 옛 '확인 대기' 표기와 질문목록 261·262행은 범위 밖이라 그대로다.
  4. 업종 법령 근거는 2차 자료뿐이고 통신·물류·항공 업종은 미포함이다(TODO-138).

## 2026-11-13 — 통합 로그인 D-268~D-276 확정 반영(결정 119 후속, conv_179453113304, O3)

- 범위
  - 사용자 답(Apple 이메일도 연결 후보에 포함, 비밀번호 없는 6자리 일회용 코드가 기본이며 비밀번호 방식도 유지, 같은 기기에서는 패스키 지원, 나머지는 권장안)에 따라 D-268~D-276 을 모두 확정하고 plan/ 정본에 반영했다. 제품 코드 변경은 없다.
  - D-269 는 변경 확정이다(6자리 코드 기본, 비밀번호 유지·신규 가입자도 선택 가능, 같은 기기는 패스키). D-275 도 변경 확정이다(Apple 릴레이 이메일을 연결 후보에 포함). 나머지는 권장안대로 확정이다. ADR-103 은 확정이며 새 번호는 없다.
- 판정 결과: AC 14/14 PASS, 검사기 5종 rc=0, plan_audit 경고 27(기록만).
- 변경 문서: `.claude/rules/docs-map.md`, `plan/README.md`, `plan/index.json`, `plan/00_요구사항/01_사용자요구사항_원문.md`, `plan/05_아키텍처/21i_Pd_연계명세_외부계정로그인.md`, `plan/05_아키텍처/21l_Pd_연계명세_통합로그인_이메일코드_SSO.md`, `plan/06_기능목록/21_기능요건상세_부록/a01_IAM.md`, `plan/08_데이터모델/iam.md`, `plan/10_API설계/iam.md`, `plan/09_화면설계/14_로그인_가입_통합진입_화면설계.md`, `plan/90_결정관리/` 질문목록·ADR·TODO·부록 b25·d268.
- 이월 후보: 없음(TODO-132~136 은 기존 이월 그대로).

## 2026-11-13 — 통합 로그인 진입점·이메일 코드·SSO 정본 반영(결정 119, conv_179452826158, O3)

- 범위
  - 사용자 요청("구글·이메일·Apple·SSO 로 계속하기" 단일 진입 화면, 외부 계정 활용 시 이메일과 SSO 도 가능하게)을 plan/ 정본 설계에 반영했다. 제품 코드 변경은 없다.
  - 결정 119, ADR-103(`plan/90_결정관리/02_결정기록_부록/b25_ADR-103.md`), REQ-79 를 등재했다. 연계 명세 `plan/05_아키텍처/21l_Pd_연계명세_통합로그인_이메일코드_SSO.md` 와 화면 설계 `plan/09_화면설계/14_로그인_가입_통합진입_화면설계.md` 를 신설했다.
  - IAM 기능 부록(a01)·데이터 모델·API(iam)·이벤트(01·01a)·웹·모바일 화면·WBS(30a·30b)·연계 총괄(21·21c·21i)을 맞춰 고쳤다.
- 판정 결과: otest-1 AC 10/10 PASS(역라우팅 1회 — AC-9 파일명 표기 보정). 검사기 5종 rc=0, plan_audit 경고 27(기록만).
- 변경 문서: `.claude/rules/docs-map.md`, `plan/README.md`, `plan/index.json`, `plan/00_요구사항/01_사용자요구사항_원문.md`, 위 신설·수정 문서 전부, `plan/90_결정관리/` 질문목록·ADR·TODO·부록 d268.
- 확인 대기: D-268~D-276(부록 `plan/90_결정관리/01_결정대기_부록/d268_통합로그인_확인대기.md`).
- 이월 후보
  1. TODO-132~136(통합 로그인 후속 설계·시험).
  2. 외부 사실(Apple·SSO 공급자 정책 등)은 '확인 필요'로 표기돼 있어 구현 전 공식 문서 확인이 필요하다.

## 2026-11-12 — 결정 118 분야별 최고 솔루션 흡수·REA 레인·정본 반영(conv_179447184241, O4, Phase Batch 1~5)

- 범위
  - batch 1~4 는 분야별 최고 솔루션 조사(18a~d)·AI 단계 전략(29)·ADR-102·UX 패턴 카탈로그(13·13a, UX-29~113, CV-40·41)·기능요건 부록 AI 활용 재판정·KMS-04·COL-17 정식 등재·결정 118·REQ-77·78·WBS·메뉴 1086·1087·옛 문구 정리를 했다. batch 5 는 REA 레인 마무리(`ref/` 분석·manifest·`tools/fetch_ref.py`)와 정본 색인·docs-map 반영, 사이클 마무리다.
  - 앱 소스 변경은 없다. 문서(plan/·ref/·docs-map)와 문서 도구(`tools/fetch_ref.py`)만 바꿨다.
- 판정 결과: otest-5 최종 PASS. AC-001~025 전부 rc=0, CANARY-001 rc=1(기대 FAIL, 정상). check_feature_detail(기본·cite·wbs 227·self-test), check_ai_usage, fetch_ref audit(52, 위반 0)·self-test(86), plan_audit(경고 28), md_audit 모두 rc=0. REA 44행(done 33, blocked 11, queued 0), 약관 근거 0.
- 변경 문서: `.claude/rules/docs-map.md`, `plan/README.md`, `plan/index.json`, `plan/03_개발범위/` 01·08·20·20b·30·30a·31·31a, `plan/05_아키텍처/` 22·26, `plan/06_기능목록/20a·21`, `plan/08_데이터모델/corp.md`, `plan/09_화면설계/08f`, `plan/99_발표자료/한장요약.txt`, `ref/README.md`, `ref/analysis/rea_artifacts_d.md·rea_artifacts_d2.md`, `ref/collect_log.json`, `ref/manifest.json`, `tools/fetch_ref.py`, 이 HISTORY·LESSONS, `plan/90_결정관리/03_향후과제_TODO.md`.
- 커밋: batch 1~4 커밋(9c6048e … 36d2675) 12건과 이번 batch 5 본체·문서 커밋(해시는 worktree 로그 참조).
- 이월 후보
  1. `tools/fetch_ref.py` 에 열람만 하고 지운 항목을 analyzed_removed 로 등재하는 명령이 없다. 이번엔 승인 아래 manifest 를 직접 편집했고, 40자 해시를 못 구한 항목은 audit 처리했다(TODO-125).
  2. `plan/06_기능목록/21_Pd_기능요건상세_공통_색인.md` 20행 '다음 번호 I-48' 이 낡았다. 현행은 I-49(f3f044d 부터)이다(TODO-126).
  3. 수정 금지 파일 `plan/09_화면설계/01_화면설계_공통원칙.md` 270행 '215건' 문구(TODO-124).
  4. 실행 비트 불일치 경고(plan 문서 일부 100755 대 커밋 100644).
  5. REA 정적 분석 한계. 원격 웹앱 업무 기능 미확인, BCJ2 내부 미확인, Trello Windows 설치본 없음, uipath 일부만 해제, thunderbird 데스크톱판 미분석.

## 2026-11-12 — 결정 117 범용화·에이전트형 PMS 정본 반영(conv_179446121256, O4, 웨이브 1·2)

- 범위
  - 결정 117 범용화·에이전트형 PMS 를 정본에 반영했다. 요구 REQ-66~76, 기능 9건 신설·4건 확장으로 확정 224건 + 후보 1건, 차트뷰 CV-35~39, 연계 I-48(STT), ADR-101, 확인 대기 D-237~241, 새 화면 7개(메뉴 1079~1085)이다.
  - 앱 소스 변경은 없다. 문서(plan/·ref/·docs-map)와 생성 스크립트(plan/_build)만 바꿨다.
- 판정 결과: otest-1 PASS, 판정기 AC-001~025 전부 PASS, CANARY-001 FAIL(정상). md_audit·plan_audit 점검은 아래 보고 참조.
- 변경 문서
  - 신규: `plan/03_개발범위/08_범용화_에이전트PMS_요구.md`, `plan/05_아키텍처/28_Pd_에이전트_자동화_설계.md`, `plan/05_아키텍처/21k_Pd_연계명세_STT.md`, `plan/09_화면설계/08f_차트뷰_추가_PERT_할일.md`, `plan/09_화면설계/12_간편입력_규모적응_화면설계.md`, `plan/01_시장조사/16·16a·17·17a`, `plan/90_결정관리/01_결정대기_부록/d237_범용화_확인대기.md`, `plan/90_결정관리/02_결정기록_부록/b23_ADR-101.md`, `ref/analysis/openproject.md·plane.md·taiga.md·rea_artifacts_c.md`.
  - 갱신: 기능 목록·상세 부록(a05·a07·a08·a10·a13·a16·a17·a18)·01·01b·21 색인·20c·02 메뉴구조·09 메뉴 ID·08 차트뷰·WBS 30a·30b·32·21 연계 총괄·27·결정 문서·README·index.json·docs-map·`plan/_build` 6개·ref/README·manifest.
- 이월 후보
  1. C 드라이브 여유 부족으로 Plane·OpenProject·Taiga 원본을 받지 못했다(분석 메모만 있음).
  2. 상용 REA 0건(약관 사유).
  3. TODO-121 일정 재산정.
  4. ADR 문서의 낡은 '최신 ADR-099' 문구.
  5. 개인 단위 이상 징후의 법령 고지 의무 미확인.
  6. STT 공급자·법령 조문 미확인.

## 2026-11-12 — Snap Assist 남은 영역·크롬 단추 대비·포커스·SDI 유예 옵션·SCR-FIN-06(conv_179444169058, O4, Phase Batch 2/2)

- 범위
  - 대비(이월2). `BuiltinThemes.cs` 를 고쳤다. Mars 기호 꺼짐 대비 3.56~3.68, pd-contrast 켜짐 기호 11.32/10.53, 호버 바탕 3.15/3.13, 누름 바탕 3.69/3.67(누름 알파 0.5), DockJoinTarget 3.98/3.72 이다. pd-contrast '항상 위' 제목줄 색을 마젠타 #FF00FF 에서 시안 #00E5FF 로 바꿨다(같은 색 위 호버 대비 1.00 문제).
  - 포커스(이월3). 크롬 단추를 Focusable 로 만들고 2px 포커스 링을 붙였다. `TabFocusScope.cs` U21 확장으로 메뉴 → 활성 문서 크롬 단추 7개 → 메뉴 순으로 돈다(문서가 없으면 건너뜀). 실키 (a)·(b) 와 웹 chrometab 은 PASS 다.
  - 이월6 기준선을 맞췄다(차이 1px 이하). 이월9 SDI 는 옵션 `SnapAssistSdiGraceMs`(초기 400, 범위 100~2000, 설정 ▸ 창)를 추가했고 저장·재기동 유지·범위 보정이 PASS 다. 이월9 웹은 visibilitychange hidden 이면 Snap Assist 를 종료한다(w24 S12).
  - Snap Assist 남은 영역 뒤판은 0.96 에서 Fold5 다크 글자 약 4% 잔상 FAIL 이 나와 ZoneOpacity 를 1.0 으로 바꿨다. 데스크톱·웹·Fold5 PASS 다.
  - 이월7 `retest_android_pcaway_splash.py` 에 ff_aux_ms 를 추가했다. Fold5 접힘 스플래시 9회 중앙 1972ms PASS, 펼침은 판정 불가다. `pc_away_monitor.py` 는 주입 입력 오탐(GRACE_MS 제외)을 고쳤고 `snap_assist_fg.py` 에 T5·T6·T6D·T6S 를 추가했다.
  - 이월10 pd-admin 화면 SCR-FIN-06(a1011)을 추가했다(화면 89, 후보 17). `menu_data.py`·생성물·09·01·20c·README·docs-map 수치를 맞췄다. a10 에 AC-024 결과·해소·이월을 기록했고 01 §4.2·§7, 11 §3.9 를 갱신했다.
- 판정은 데스크톱·웹·모바일 축 시험 완료이며 실입력이 필요한 일부 항목은 이월한다. 동결 소스 해시는 3e519328204694a8faeeeb7384c0b64d 다.
- 이월 후보
  1. 비활성 회색 제목줄 호버 대비가 2.72 다(비활성 전용 토큰 없음).
  2. 판정기(chrome_button_states)의 '켜짐 바탕' 규칙이 낡았다(설계는 기호 색으로 구분).
  3. PC 비움 때 남은 측정은 SDI 실키 Tab, Space/Enter 실키, :pressed 실누름 캡처, '항상 위' 시안·DockJoinTarget 라이브 캡처, 포커스 링 2px 측정이다. Fold5 펼침 스플래시는 판정 불가다.
  4. `SnapAssistLayer.cs`(637줄)·`SnapAssistSession.cs`(626줄)·`main.js`(321줄)의 300줄 분할이 남았다.
  5. collab_board scope-check 가 phases 형식을 읽지 못한다. 팀원이 유휴가 되면 감시 폴링이 멈춘다.
  6. 데스크톱 커서를 원래 위치로 되돌릴 수단이 없다.
- 커밋 제외는 INFRA.md, `poc/ui/device_lease/`, `poc/ui/test/desktop/measure/pd_away_ps_*`, 로그·생성물·bin/obj 다.

## 2026-11-12 — Snap Assist 후보 카드 미리보기·위치 고정 해제 끌기·제목 탭 × 표시 옵션(conv_179444169058, O4, Phase Batch 1/2)

- 범위
  - A. Snap Assist 후보 카드 미리보기를 고쳤다. 원인은 축소가 두 번 적용된 것이다. 원래 크기로 캡처한 뒤 비율 축소하도록 바꿨고, 카드는 200x120 썸네일 아래에 이름표를 두며 대체 카드를 추가했다(`SnapAssistThumbnail`·`SnapAssistThumbnail.Diag.cs`·`SnapAssistPicker`).
  - B. 위치 고정을 풀어도 문서 창이 끌리지 않던 문제를 고쳤다. 원인은 `MdiDocumentWindowTheme.axaml` 의 `DockProperties.IsDragEnabled` 바인딩이 TwoWay 였던 것이고 OneWay 로 바꿨다.
  - C. 제목 탭 × 표시 옵션 `TitleTabCloseMode`(Auto/Always/Hover/ActiveAndHover)를 추가했다(`MainView.TabClose.cs`·설정 ▸ 모양). × 를 겹쳐 그리면서 아래 글자를 흐리게 하고, 가운데 클릭으로도 탭을 닫는다.
  - 문서는 01 을 01a 로 분할했고(01 §2-1·11 §3.9 개정, docs-map 갱신) 시험 도구 `poc/ui/test/desktop/measure/pc_away_monitor.py`·`snap_assist_fg.py` 를 새로 만들었다(README 색인 등재).
- 판정은 데스크톱·웹 모두 PASS 다. PC 비움 실입력(Shift+Tab, H-16 중앙값 225ms, H-17, B 실마우스, 탭 가운데 클릭)이 PASS 이고 Fold5 AC-207(APK a3dd2c68)도 PASS 다.
- 변경 문서는 `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-18/plan/09_화면설계/01_화면설계_공통원칙.md`, `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-18/plan/09_화면설계/01a_화면설계_공통원칙_끌기경량크롬.md`(신규), `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-18/plan/09_화면설계/11_창배치_스냅레이아웃.md`, `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-18/.claude/rules/docs-map.md`, `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-18/poc/ui/test/README.md` 다.
- 커밋 제외는 INFRA.md, `poc/ui/device_lease/`, `poc/ui/test/desktop/measure/pd_away_ps_*`, 로그·생성물·bin/obj 다.
- 이월 후보(Phase Batch 2/2 가 이어진다)
  1. `SnapAssistLayer.cs`(636줄)·`SnapAssistSession.cs`(626줄)는 이미 300줄을 넘은 상태였다. 분할이 필요하다.
  2. collab_board scope-check 가 phases 형식의 배정표를 읽지 못한다.
  3. `pc_away_monitor.py` 가 자기 SendInput 을 사용자 입력으로 보고 present 오탐을 낸다.
  4. 시험 뒤 데스크톱 커서를 원래 위치로 돌려놓을 수단이 없다(SetCursorPos 차단).
  5. 관찰 — Snap Assist 남은 영역에서 카드 사이로 뒤 창 글자가 비쳐 보인다. batch 2 에서 검토한다.

## 2026-11-12 — Snap Assist·제목줄 단추 켜짐/꺼짐·hover 바탕만·미뤄 둔 성능 측정(conv_179435695241, O4)

- 범위
  - Snap Assist 를 넣었다. 끌기 막대·최대화 단추 팝업으로 칸에 놓은 뒤 남은 칸마다 다른 창 후보를 고르고, Esc 는 고른 것만 남긴다. 파일은 `SnapAssistHost`·`SnapAssistSession`·`SnapAssistCandidates`(`poc/ui/src/Pd.Poc.Shell/Docking/`), `SnapAssistLayer`·`SnapAssistPicker`·`SnapAssistThumbnail`(`poc/ui/src/Pd.Ui.Views/Snap/`), `SdiSnapAssistHost`(데스크톱), 원격 `RemoteSnapAssistCommands`, 옵션 3종(`UiSettings.Snap`·설정 ▸ 창)이다. 시험 도구 `poc/ui/test/desktop/snap_assist_remote.py`·`poc/ui/test/web/w24_snap_assist.py` 를 새로 만들었다.
  - 문서 창 기능 단추·메인 제목줄 단추의 켜짐/꺼짐을 바탕 변경 없이 구분했다(켜짐 노랑 #FFE066 + 1.15배, 꺼짐 불투명도 0.85). hover·누름은 테두리 없이 메뉴 막대와 같은 바탕만 바꾼다. UI 형 스위치 자동 판정 칸 점(`PdUiAutoDotSize`=3)과 2px 키보드 포커스 링을 넣었다. Snap Assist 중 Tab 을 앱 전역 `TabFocusScope` 가 가로채던 것을 고쳤다.
  - 사용자 PC 비움 중 미뤄 둔 성능 측정을 했다(H-01·H-03·H-07·H-12). 결과는 a10 §1.1 이다.
- 판정은 TEST 통과다(조건부 PARTIAL 2건 수용). 데스크톱 otest-1 4차는 AC-024 외 PASS 이고 원격 회귀 19/19 다. 웹 otest-w-1 3차는 w24 14/14·w23 15/15 이고 Tab·방향키·Shift+Tab·Enter 가 PASS 다. Fold5 는 AC-013 PASS 다. AC-020·023 의 0.85 수치 불일치는 정본 개정으로 수용했다. 역라우팅은 3회(AC-023·020·009·014 → AC-015 Tab·AC-025 링 → AC-015 Tab 원인)였다.
- 성능(실제 ms 판정, 부하는 참고)은 다음과 같다. H-01 perf_ui 뒤 커밋 p95 104.41·89.99ms FAIL 재현, 새 앱 67.10ms FAIL·12.83ms PASS, B3 첫 프레임 1회차 FAIL·2회차 PASS. H-03 접힘 중앙 1890ms PASS, 펼침은 도구 문제로 측정불가. H-12 FAIL 13 + TypeError. H-07 은 도구 인자가 없어 측정불가. H-02·H-04 는 H-05 결정 전이라 미실행.
- 변경 문서는 `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-18/plan/09_화면설계/11_창배치_스냅레이아웃.md`(§3.9 실측 확인), `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-18/plan/09_화면설계/01_화면설계_공통원칙.md`(§4.2 0.85·노랑·점·포커스 링·대비), `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-18/plan/90_결정관리/02_결정기록_부록/b21_ADR-099.md`(개정 2 실측·수치), `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-18/plan/04_기술스택/04_판정보고서_부록/a10_측정보류_목록.md`(H-16·H-17 추가, §1.1 측정 결과), `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-18/poc/ui/test/README.md` 다. 지시의 'H-13' 은 이미 다른 항목이라 Snap Assist 시간 보류는 H-16 으로 적었다.
- 커밋 제외는 INFRA.md, `poc/ui/device_lease/`, `poc/ui/test/desktop/measure/pd_away_ps_*`, 로그·생성물·bin/obj 다.
- 이월 후보
  1. AC-024 변경 전 캡처 없음(PARTIAL).
  2. Mars 스타일 기호 대비 2.73~3.30:1, pd-contrast 켜짐 노랑 대비 1.10:1.
  3. 문서 창 크롬 단추 `Focusable=False` 라 Tab 이 오지 않는다.
  4. `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-18/plan/09_화면설계/01_화면설계_공통원칙.md` 300줄 초과 분할.
  5. 데스크톱 실 Shift+Tab 미판정(웹 PASS)·SDI 앱 모드 호스트 표시(a10 H-17), Snap Assist 시간(H-16).
  6. 캡션 기호가 스위치보다 2px 낮다(관찰).
  7. 도구 문제 3건 — `retest_android_pcaway_splash.py` 펼침 first_sample_late, `retest_mdi_drag.py` redock_left TypeError, `--work` Linux 경로 시 기동 실패. 보조 캡처 스크립트 기대값 2건.
  8. Fold5 APK 는 역라우팅 전 소스라 재빌드가 필요하다.
  9. 웹 visibilitychange 종료 미구현, SDI 재활성화 400ms 는 초기값.
  10. front 이관 — pd-admin 단가표 초안 화면·회사 관리자 메뉴 분리와 메뉴·화면 ID(pd-38 결정 115, `/home/rioky/.claude/session-env/5a01cad7-2b06-412f-a040-38c16799d4ad/work/front_handoff_pd38.md`).

## 2026-11-11 — 창 배치(Snap Layouts)·최대화 단추 호버 팝업·이월 작업 진행(conv_179435695241, O4)

- 범위(시간 측정 보류)
  - batch1 은 문서와 시험 도구다. 창 배치 정본 `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-18/plan/09_화면설계/11_창배치_스냅레이아웃.md`, ADR-099(`/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-18/plan/90_결정관리/02_결정기록_부록/b21_ADR-099.md`), 측정 보류 목록 `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-18/plan/04_기술스택/04_판정보고서_부록/a10_측정보류_목록.md` 을 새로 만들고 01·03·04·06 화면 문서에 포인터를 넣었다. 시험 도구는 `w23_tab_join_web.py`(⑦)·`retest_r2_titlebar.py`(SDI 캡처 hwnd)·`retest_real_input.py`(`--dry-run-endpoints` 끝점 사전 판정)를 고쳤다.
  - batch2 는 Snap 구현이다. 칸 카탈로그·판정(`SnapLayoutCatalog`·`SnapZoneResolver`·`UiSettings.Snap`), 막대·미리보기(`Pd.Ui.Views/Snap/`), MDI·관리형 창 끌기(`MdiSnapDrag`·`ManagedSnapDrag`), SDI 훅(`SdiSnapHook`·`SdiSnapBarHost`·`OsSnapProbe`), 창 메뉴 '배치'·Ctrl+Alt+방향키(`WindowMenuOps.Snap`·`SnapKeyboardCommands`), 설정 옵션·문구 ko/en, 원격 명령(`RemoteSnapCommands`)이다.
  - 사용자 추가 요구(16:58)로 최대화·이전 크기 단추 1초 호버 뒤 칸 팝업(`SnapFlyoutState`, 옵션 SnapFlyoutEnabled·SnapFlyoutDelayMs 1000)을 넣었다. Windows 메인 창은 OS 메뉴에 맡겨 표시하지 않는다.
  - 사용자 실사용 결함(17:02, 상단 막대 칸을 골라도 배치 안 됨)을 고쳤다. 캡처 유실 처리기가 판정을 지우던 것을 500ms 유예로 바꾸고, 놓기 때 판정이 없으면 놓은 자리로 다시 판정한다. MDI 와 웹 관리형 창에 각각 고쳤다. 웹 실호버 미감지는 단추 PointerEntered 대신 TopLevel 포인터 이동 판정으로 바꿨다.
  - batch3 B5 는 Fold5 패널 기능 끔에서 문서 정확히 1개 패널 머리줄 끌기의 Fill 허용(`PanelQuery`)과 놓기 진단 로그(`PanelDropService`, category=panel-drop)다. 처음 판정이 여러 문서 패널까지 허용해 교차 시험에서 잡혔고 1개로 좁혔다.
- 판정은 실행한 항목 PASS 다. 데스크톱(otest-1) AC-006·007·009·010·011·012·015·017, 웹(otest-w-1) AC-014·017·025, Fold5(otest-m-1) AC-008(Android)·AC-016·AC-025(모바일)이다. 실입력 일부(AC-013 SDI 끌기·AC-019 T3 캡처·호버 팝업 칸 실클릭·기본값 머무름)와 ms 항목은 a10 보류(H-07·H-09~H-15)다. 실입력 재개는 주간 한도 게이트 승인(19:51) 뒤 사용자 복귀 감지로 멈췄다.
- 변경 문서는 위 3개 신규와 `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-18/plan/09_화면설계/01_화면설계_공통원칙.md`(279행 기능 수를 rebase 뒤 정본 값 215건 확정 + 후보 1건 = 216건으로 정정), `03_데스크톱_화면설계.md`, `04_웹_화면설계.md`, `06_모바일_화면설계.md`(같은 폴더), `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-18/plan/90_결정관리/02_결정기록_ADR.md`, `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-18/plan/04_기술스택/04_PoC_UI_판정보고서.md`(부록 색인 a10), `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-18/plan/README.md`, `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-18/plan/index.json`, `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-18/.claude/rules/docs-map.md`, `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-18/poc/ui/test/README.md` 다.
- 커밋 제외는 INFRA.md, `poc/ui/device_lease/`, `poc/ui/test/desktop/measure/pd_away_ps_*`, 로그·생성물·bin/obj 다.
- 이월 후보
  1. a10 보류 실입력(AC-013·AC-019·호버 팝업 칸 실클릭·기본값 머무름 지연)과 Snap 끌기 ms 측정.
  2. MDI 끌기 회귀 도구가 기본값(스냅 켬)에서 가장자리 놓기 시나리오와 충돌(실패 13건, 의도된 변경). real_resize_* 4건은 원인 미분석.
  3. 세로 모니터 그림 방향·SDI 복원 없음·메인 창 OS 의존 한계(11 §12).
  4. 직전 사이클 이월 1~5(A2 B2·W4·A9·⑦ v4·pcaway_gate 규칙)는 시간 측정 보류로 그대로다.
## 2026-11-11 — 사용자 답 31건 정본 반영·결정 114·ADR-100·9모듈 데이터모델·API(conv_179437577100, O4)

- 범위
  - 정본 전수 점검 권장안 61건(D-176~D-236)에 대한 사용자 묶음 답 A-1~A-14·B-1~B-17(31건)을 정본에 반영했다. 권장안 53건 확정, 7건 변경 확정(D-201·204·207·208·215·222·232), D-194 보류다. 결정 114-1~11 로 묶고 ADR-100 상세 부록(`/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-36/plan/90_결정관리/02_결정기록_부록/b22_ADR-100.md`)을 새로 썼다.
  - 주요 결정은 ADM-09 사용 한도 관리 보강(REQ-64), EVA-06 평가 정책 템플릿·EVA-07 다중 참여 개인 평가 신설(215건 확정 + 후보 1 = 216, M4 54), 대체 언어 2단, 환율 외부 API 기본, AI 예측 통계·LLM 선택, 서버 설정 권한 위임, 포함 AI 토큰 기업용만, 참조 분석 대상 라이선스 확인 불요다.
  - 9모듈(ain·aip·crm·doc·eva·fin·hrs·kms·pre) 데이터모델·API 문서 18개를 `plan/08_데이터모델`·`plan/10_API설계` 에 새로 만들었다.
  - 환율 API 조사(`plan/01_시장조사/15_환율API_조사.md`)와 Mars 평가식 차이표(`plan/05_아키텍처/25i_Pd_Mars_평가식_차이표.md`)를 새로 만들었다.
  - 이월 7건(C-31·C-F7021·C-F3018·C-F6027·C-MOD·C-L27·C-T98)을 반영하고 WBS·일정표(30·30a·30b·31·31a)·생성기(`plan/_build/features*.py`·`menu_*.py`)·색인(`plan/index.json`·`plan/README.md`·`.claude/rules/docs-map.md`)을 갱신했다.
- 판정은 시험 PASS 다. goal A1~A7 PASS, 검사기 7종 rc=0 이다. DONE 전이 게이트가 고부하로 판정을 생략해 odone 첫 단계에서 다시 판정했다(A4·A5 UNDET → run-long 뒤 PASS). 역라우팅 2회(B-10 옛 표기 4곳, 옛 기능 수 213/214 표기 6곳)는 모두 해소했다.
- 변경 문서는 plan/ 아래 정본 99경로와 `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-36/assets/README.md`, `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-36/.claude/rules/docs-map.md` 다(커밋 8852e41).
- 남은 일과 이월 후보
  1. `plan/09_화면설계/01_화면설계_공통원칙.md:279` 등 수정 금지 문서의 옛 기능 수 표기는 front 세션 몫이다.
  2. Mars 표본 숫자 대사는 WBS-M2-26 에서 한다.
  3. 환율 API·오픈소스 라이선스의 '미확인' 값은 구현 전에 공식 페이지에서 다시 확인한다(이번에는 WebSearch 요약 기준).
  4. D-194 Dock 이슈는 보류다.

## 2026-11-11 — 사용자 UI 피드백 6건·활성 탭 분리 소실 수정·측정 도구 새 사양·오후 재측정(conv_179435695241, O3)

- 범위
  - 사용자 UI 피드백 6건(oi-fix-1)이다. 합치기 아이콘, 분리 탭 Z 최상, 툴바 아이콘 하이라이트, 드래그 중 라운드 유지, 탭 줄 높이 기본 38(19~48)·탭 32, 시스템 폰트·글자 크기 설정(`UiSettings.Shell.cs` 신규, `AppearancePage`·`SettingsInventory`·`shell.ko/en.json`)을 넣었다.
  - 역라우팅 수정이다. SDI 끄는 중 합치기 아이콘·반투명 공용 키(`DockJoinTarget`·`SdiTitleBar`·`SdiTitleBar.TabJoin`·`MdiDocumentWindowTheme`), 활성 탭 끌어내기 시 묶음 문서 소실 수정·빈 패널 정리 경로(`PocDockFactory`·`PocDockFactory.TabGroups`)를 고쳤다.
  - 시험 도구는 `retest_pcaway_extra2.py`(B2 직전 20창 visible 확인, 실패 시 '측정 무효(절차)'), `retest_r2_titlebar.py`·`retest_r2_web.py`·`retest_r2_android.py`(T3 새 사양 `--strip-setting`), `w10_web.py`(탭 줄 높이 좌표 보정)이다. 색인은 `poc/ui/test/README.md` 에 반영했다.
- 판정은 시험 PARTIAL 이다. PASS 는 UI 6건 3축, SDI 아이콘, 활성 탭 분리, 회귀(MDI 끌기 36/36·탭 합류 15/15·웹 41/41·Fold5), r2 T1·T3·T4 새 사양, A7 Fold5 웹 문서 열기(펼침 p95 374·접힘 383ms)다. FAIL 은 A2 B2(단독 52.78ms, perf_ui 뒤 상태에서만, 새 앱 대조 13.33ms PASS), W4 문서5 25.4ms(100ms 넘는 긴 프레임 반복), A9 펼침 2069ms 다.
- 변경 문서는 `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-18/plan/09_화면설계/01_화면설계_공통원칙.md`, `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-18/plan/09_화면설계/03_데스크톱_화면설계.md`, `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-18/plan/09_화면설계/04_웹_화면설계.md`, `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-18/plan/04_기술스택/04_판정보고서_부록/a9_성능_웹_모바일_4_5_6.x.md`(반복 3 절), `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-18/poc/ui/test/README.md` 다.
- 커밋 제외는 INFRA.md, `poc/ui/device_lease/`, `poc/ui/test/desktop/measure/pd_away_ps_*`, 로그·생성물·bin/obj 다.
- 이월 후보
  1. A2 B2 perf_ui 뒤 상태 차이 진단(docs.zoom·settings.get·GC 비교).
  2. W4 문서5 긴 프레임(100ms 초과) 원인 분해.
  3. A9 Fold5 펼침 first_frame 2069ms 재측정·개선.
  4. ⑦ v4 는 도구 한계로 BLOCKED, F08 OS 커서 경로는 도구 한계(L2 는 PASS), T3 SDI 캡처가 메인 창을 찍는 도구 문제.
  5. pcaway_gate 의 P1 low_load ≥10 규칙이 '부하로 시행을 무효 처리하지 않음' 사용자 결정과 충돌한다.
  6. Fold5 문서 1개일 때 패널 머리줄 끌기 동작 관측(확인 필요).

## 2026-11-11 — A2 B2 지표 교정(대안 c)·A12 잔존 문구 정정(conv_179435695241, O3)

- 범위(사용자 2026-11-11 09:15 지시 1·5번)
  - A2 B2 지표를 '실제로 커밋된 프레임 간격만 집계(p95 ≤ 25ms) + 입력 뒤 25ms 안 커밋 없는 누락 공백 0건' 으로 교정했다. 유휴 틱(커밋 없는 간격)은 집계에서 빼고 개수·p50 을 함께 보고한다.
  - 앱 계측 `PerfCommitLog.cs` 신규(진단 빌드에서만 Compositor `AfterCommit` 리플렉션 구독, Avalonia 12.1.4 고정, 못 찾으면 `commit_source=unavailable` 미측정 FAIL)와 `PerfFrameLog`·`PerfProbe`·`PerfSampler`·`RemoteWindowsCommands` 연동. 판정기 `poc/ui/test/desktop/judge/b2_commit_frames.py` 신규, `retest_pcaway_extra*.py` 연결.
  - A12 옛 잔존 고정 문구(잡을 곳 50 DIP·왼쪽 잔존 159 DIP)를 2026-11-10 잔존 비율 규칙으로 정정 기록했다(F08).
- 판정: 시험 PASS(AC 12/12, 판정기 self-test 16/16, CANARY heavy_render pass=False, 옛 기록 재판정 시 원자료 없음 rc=2). B3 판정은 무변경이다. 새 측정은 하지 않았다(사용자 지시).
- 변경 문서: `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-18/plan/04_기술스택/04_판정보고서_부록/a9_성능_웹_모바일_4_5_6.x.md`(반복 3 절), `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-18/plan/09_화면설계/03_데스크톱_화면설계.md`(B2 bullet·F08 A12 정정만), `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-18/poc/ui/test/README.md`.
- 커밋 제외: INFRA.md, `poc/ui/device_lease/`, `pd_away_ps_*`, 03·01 의 oi-fix-1 UI 변경분(별도 커밋 몫).
- 이월 후보
  1. 새 지표로 A2 재측정. min_samples=30 경계값(이동 30회와 같은 수)에서 커밋 간격 표본 부족으로 미측정이 되는지 관찰한다.
  2. `AfterCommit` 실동작(앱 실행 중 커밋 로그 수집) 확인.
  3. A7 Fold5 공식 재측정(펼침 doc 500ms 이하), A9 C5 first_frame 재측정.

## 2026-11-11 — plan 정본 전수 점검·수정, M4 부록 47건 전체 템플릿 승격, 일정 재산정(TODO-115~119, conv_179429225635, O4)

- 범위는 문서·생성기·검사기뿐이다. 앱 코드·poc/ui·서버·INFRA.md 와 수정 금지 문서(`plan/09_화면설계` 01·03·04·06, `plan/04_기술스택` 02·05, `plan/07_구축_배포/05`)는 건드리지 않았다.
- 요청: TODO-115~119 진행과 지금까지의 계획(plan/ 정본) 전수 점검·수정.
- 한 일:
  1. 정본 전수 점검 — 발견 386건을 조치·권장안 적용·이월·보류로 분류해 `plan/90_결정관리/04_정본_전수점검_결과.md` 와 발견 목록 04a~04c 에 남겼다. 이월은 3건(F-7-021·F-3-018·F-6-027)이다.
  2. 확인 대기 번호 D-176~D-236(61건)을 `plan/90_결정관리/01_결정대기_부록/d176_정본점검_확인대기.md` 에 등록했다. 권장안을 먼저 적용했고 사용자 확인이 남아 있다. 다음 번호는 D-237 이다.
  3. TODO-115 일정 재산정 — 신규 `plan/03_개발범위/31a_Pd_일정표_M4_레인병렬_출시일정.md`. WBS 행 WBS-M2-42(SCH-03)·WBS-M2-43·WBS-M4-37·WBS-CV-28~34 를 추가하고 오케스트레이터(WBS-ORC)를 M4 일정에 편입했다(D-234~236). 레인 병렬 기준 출시까지 450~763근무일이며 수치는 전부 추정이다.
  4. TODO-116 공휴일 데이터 조사 — 신규 `plan/01_시장조사/14_공휴일_데이터_오픈소스_조사.md`.
  5. TODO-117 다국어·로캘 설계 — 신규 `plan/05_아키텍처/27_Pd_다국어_로캘_설계.md`.
  6. TODO-118 M4 기능 부록 47건을 전체 템플릿으로 승격했다. a17 을 모듈 경계로 나눠 `a17_IMP_AIN_2.md` 를 만들었다(부록 26개). 검사기는 M4 를 전체 판정, X 를 축약 판정으로 바꿨다.
  7. TODO-119 구 R2·R3·209건 표기를 M4·X·213건 기준으로 정리했다.
  8. 300줄 규칙 분할 — 문서 4건(`plan/05_아키텍처` 08a·20d·20e·21j), 검사기(`check_feature_detail.py` + `fd_rules.py`·`fd_files.py`·`fd_selftest.py`), 생성기(`menu_tree.py` + `menu_data.py`, 메뉴 ID 불변 검사 추가).
- 수치(검사기 실측): 부록 26개·기능 214건(확정 213 + 후보 1), 전체 템플릿 209·축약 2·포인터 3, 화면 88개, md_audit 등재 108·고아 0·깨진 참조 0.
- 판정: 검사기 10종(check_feature_detail 4모드, check_ai_usage 2모드, check_m1_coverage, menu_tree 재생성 비교, md_audit, plan_audit) rc=0, AC 27건 PASS(AC-303 은 1차 FAIL 뒤 수정·재시험 PASS, 역라우팅 1회), CANARY-001 은 기대대로 rc=1, goal A1~A16 PASS. UI 시험·성능 측정·외부 리뷰는 실행하지 않았다(대상 없음·범위 제외·스킬 미제공).
- 변경 규모: 218개 파일(신규 17 — md 13·py 4, 수정 201). 등록 3종(`.claude/rules/docs-map.md`·`plan/index.json`·`plan/README.md`)에 신규 문서를 등재했다. 정본 원격 main 의 2026-11-11 변경(성능 부록 a9 '반복 3' 절 등) 위로 rebase 했고 충돌은 없었다.
- 이월 후보:
  1. D-176~D-236 사용자 확인(권장안이 바뀌면 해당 문서와 31a 일정을 다시 본다).
  2. 발견 목록 이월 3건(F-7-021·F-3-018·F-6-027)과 TODO 문서 이월 절.
  3. 수정 금지 문서에 남은 낡은 표기(09/06 의 'PLT-02, R2', 09/01 의 '화면 79개·기능 203건', 옛 세션 경로 17곳) — 개정 허용 여부를 사용자에게 묻는다.
  4. `plan/01_시장조사/08` 의 '미확인' 1건, PLT-03 대체 언어 규칙 D-207 대 D-232 병기, PRJ-15 보존 기간(법률 확인 필요).

## 2026-11-11 — 첫 이동 지연·그리드 셀 지연 생성·탭 합류 규칙·화면밖 이동 비율·제목줄 순서(conv_179419201221, O4, goal G-7C3A91E4 A1~A18)

- 범위
  - 성능: A2 첫 이동 지연(`MdiStaticLight`·`MdiMoveLightener`), A7 그리드 셀 템플릿 지연 생성(`PdCellTemplate.cs`)·K5 `GridColumnScaler.FitStarIfClear`.
  - UI 요청 4건: MDI 화면 밖 이동 잔존 비율(`MdiDragLimit` 기본 25%), 간트 스크롤바 얇게, SDI·MDI 탭 합류는 창 아이콘·탭 줄에 올렸을 때만(`TabJoinRules`·`MdiTabGroup`·`LayoutSnapshot` v5·`WindowTabJoinService`(+DockGuard)·`SdiTitleBar.TabJoin`, 끄는 창 반투명 0.55), 제목줄 [테마][모드][캡션] 순서.
  - 시험 도구: `test/web/measure/w23_tab_join_web.py`·`a7_docopen_web.py`, `retest_tab_join.py` 신규, `retest_mdi_drag*.py` effective_min_grab, `test/mobile/measure/webperf_inject.py`, `poc/ui/test/README.md` 색인. 은퇴 도구는 `poc/ui/test/archive/retired` 로 옮겼다.
- 판정(장부 `/home/rioky/.claude/session-env/5a01cad7-2b06-412f-a040-38c16799d4ad/oto_verdicts/A1~A18.json`)

| 항목 | 결과 |
|---|---|
| A1, A3~A6, A8, A10, A11, A15~A18 | PASS |
| A12 | PASS(주의 — AC-001·AC-006 은 문자 그대로는 FAIL 이나 나중 사용자 요청을 우선하는 해석) |
| A2 | 사용자 승인 PASS(2026-11-11 07:05 "성능이 통과하지 못한것은 일단 모두 pass하고 넘어가자"). 실측 B2 p95 31.49ms > 25. 지표가 렌더 비용이 아니라 Avalonia 16ms SetTimer 틱을 잰다. 대안 (b) `--frame-pump on`, (c) 커밋 없는 틱 제외 |
| A7 | 사용자 승인 PASS. 펼침 doc 633ms, headless 첫 열기 568.5→459ms 개선 |
| A9 | 사용자 승인 PASS. 미측정 |

- 포그라운드 미실측: OS SDI 창끼리 겹치기, SDI 실제 제목줄 끌기 합류, 탭 실제 끌어내기, 끄는 동안 강조 캡처, L2 250ms 머무름, F08 OS 커서 경로. 웹은 ⑦ v4 읽기·W4 웹 끌기 회귀·가운데 드롭 미실측, '모두 독립 창 분리'는 부분 확인.
- 변경 문서: `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-18/plan/09_화면설계/01_화면설계_공통원칙.md`(§5.4 셀 지연 생성, §5.4·§9 화면 88개(후보 16)·기능 214건 수치를 정본 `02_메뉴구조.md` 에 맞춤), `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-18/plan/09_화면설계/03_데스크톱_화면설계.md`(§3.1 끄는 창 반투명 0.55 등), `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-18/plan/09_화면설계/04_웹_화면설계.md`, `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-18/plan/09_화면설계/06_모바일_화면설계.md`, `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-18/poc/ui/test/README.md`.
- 커밋 제외: INFRA.md, `poc/ui/device_lease/`, `pd_away_ps_*`.
- 이월 후보
  1. B2 지표 교정(대안 b·c) 뒤 A2 재판정.
  2. A7 펼침 doc 열기 500ms 이하, A9 측정.
  3. 위 포그라운드·웹 미실측 항목.
## 2026-11-10 — D-175 미결 5건 종결(결정 113, ADR-098)·KMS 모듈 신설·ISS-19 전용 화면(conv_179423311738, O4)

- 범위는 문서·생성기·검사기뿐이다. 앱 코드·poc/ui·서버·INFRA.md 는 건드리지 않았다.
- 반영: D-175 미결 5건 사용자 답을 결정 113(113-1~5)·ADR-098 로 기록하고 D-175 를 종결했다. ADR-097 은 고치지 않고 종결 포인터만 달았다.
  1. Jira 읽기 전용 동기화는 M4 정식 행 WBS-M4-18 로 두었다(113-1). WBS-X-14 는 결번이다.
  2. 연봉 수신 2단계(표준 API 풀링 + 유명 솔루션 전용 커넥터)는 모두 M4 정식 행 WBS-M4-27 로 두었다(113-2). WBS-X-15 는 결번이다.
  3. M1~M3 의 한국 전용 기능은 국가 공통 뼈대(연차 규칙 엔진·소셜 로그인·알림 공급자 목록·결제 어댑터) + 한국팩 구조로 표기했다(113-3). 해외 팩은 출시 뒤 X-13 이다.
  4. KMS 모듈을 신설하고 KMS-01~03 을 M4 로 정식 등재했다(113-4). 부록 a19_KMS.md 를 신설했고 WBS-M4-35 에 매핑했다. TODO-120 은 종결했다.
  5. ISS-19 는 전용 화면 SCR-ISS-08(메뉴 ID 1077)로 옮기고 PMO 묶음에는 바로가기만 두었다(113-5).
- 수치(생성기 실측): 기능 213건 확정 + 후보 1건 = 214(M1 61·M2 67(후보 FIN-19 포함)·M3 29·M4 52·X 2·흡수 2·제외 1), 모듈 22개, 부록 25개, 화면 88개(후보 표시 16개), pd 본체 메뉴 ID 1001~1078. 01_상세기능목록 분할 수는 2개 그대로다.
- 판정: 검사기 rc 는 check_feature_detail(기본·`--wbs`·`--cite-check`)·check_ai_usage·md_audit·plan_audit 전부 0 이다. 근거는 `/home/rioky/.claude/session-env/7be609e4-9a9c-436e-b162-7d5962ec0521/work/odev-10_result.json` 이다.
- 변경 문서: 신규 `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-36/plan/90_결정관리/02_결정기록_부록/b20_ADR-098.md`, `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-36/plan/06_기능목록/21_기능요건상세_부록/a19_KMS.md`. 수정은 결정대기 질문목록·ADR 본문·b19·TODO, WBS 30·30a·30b, 일정표 31, 경계표 32·32a, 로드맵 22, 생성기(`features.py`·`features_data.py`·`menu_tree.py`)와 검사기(`check_ai_usage.py`·`check_feature_detail.py`), 21 공통 색인과 부록 a01·a02·a09·a10, 20c 화면총괄, 09 메뉴ID 규칙, 생성물(기능목록 csv·xlsx·01·01b, 02_메뉴구조·메뉴구조.csv), `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-36/plan/README.md`, `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-36/plan/index.json`, `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-36/.claude/rules/docs-map.md` 이다.
- 이월 후보:
  1. KMS 화면 SCR-KMS-01 배치·메뉴 ID 1078·화면명은 후보다(결정 113-4 는 기능 등재만 정함).
  2. KMS-01~03 요금제 등급(개인 Professional / 기업 Basic)과 요구 ID 연결은 후보다.
  3. SCR-ISS-08 화면명·열림 방식·스마트폰 노출 값은 후보다.
  4. TODO-115 M4 레인 병렬과 출시 총 일정 재산정, TODO-92 연봉 전용 커넥터 대상별 공식 API 확인.

## 2026-11-10 — 출시 범위 질문 1~18 반영(결정 112, ADR-097)·M4 신설·R2·R3 해체(conv_179423311738, O4)

- 범위는 문서·생성기·검사기뿐이다. 앱 코드·poc/ui·서버·INFRA.md 는 건드리지 않았다.
- 반영: 출시 범위 질문 1~18 사용자 답을 결정 112·ADR-097 로 기록했다. 대원칙 3가지를 세웠다. 첫째, PMS 본연과 PMS 에 밀접한 기능(ERP·그룹웨어·KMS 영역 포함)은 출시 전에 모두 만든다. 둘째, 모든 국가에 모든 언어로 출시한다. 셋째, 국가별 제도와 외부 기관 연동은 출시 뒤(X)로 미루되 한국팩은 예외로 출시 전에 둔다.
- 단계 재편: M4(본연 완성)를 신설하고 R2·R3 를 해체했다. 출시 뒤 X 를 정리했다. 게이트를 9개로 정했다(G-BETA 통과 = 출시, G-R2 폐지, G-R3 흡수). 기능 210건의 릴리스를 다시 매겼다(M4 49건, X 2건). ISS-19 를 M2 로 정식 등재했다. SCH-05 공휴일은 회사가 국가를 고르면 내장 데이터를 쓰도록 했다. D-174 는 종결했고 남은 미결은 D-175 로 모았다.
- 판정: 시험 AC-001~031 31/31 PASS, CANARY-001 은 기대대로 FAIL. 검사기 rc 는 check_feature_detail(기본·`--wbs`·`--self-test`·`--cite-check`)·check_ai_usage·check_m1_coverage·md_audit·plan_audit 전부 0 이다(plan_audit 경고 30). 생성물 재생성 결과는 텍스트가 동일했고 xlsx 는 생성 시각만 달랐다. 근거는 `/home/rioky/.claude/session-env/7be609e4-9a9c-436e-b162-7d5962ec0521/work/otest-1_result.json` 이다.
- 변경 문서(50건): 신규 `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-36/plan/03_개발범위/30b_Pd_WBS_M4_출시뒤X_게이트.md`, `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-36/plan/03_개발범위/32a_Pd_출시범위_행이동_기능릴리스표.md`, `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-36/plan/90_결정관리/02_결정기록_부록/b19_ADR-097.md`. 수정은 WBS 30·30a, 일정표 31, 경계표 32, 13 영상분석, 20b 요금·로드맵, 확장설계 22·22a, 기능목록(`features.py`·`features_data.py`·csv·xlsx·01·01b), 기능요건 공통 색인 21 과 부록 20개, 메뉴구조(`menu_tree.py`·02·csv), 검사기 `check_ai_usage.py`·`check_feature_detail.py`, 결정대기 질문목록·ADR 본문·b18·TODO, `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-36/plan/README.md`, `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-36/plan/index.json`, `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-36/.claude/rules/docs-map.md` 이다.
- 이월 후보:
  1. D-175 미결 5건(사용자 확인 대기): ① Jira 읽기 전용 동기화 M4·X 여부, ② 연봉 수신 2단계 분리, ③ M1~M3 안의 국가별 제도·국내 연동 기능을 한국팩으로 둘지, ④ KMS 프로젝트 지식 기능 ID 등재 위치, ⑤ ISS-19 화면 배치.
  2. TODO-115 M4 레인 병렬과 출시 총 일정 재산정, TODO-116 공휴일 내장 데이터 후보 조사, TODO-117 전 언어 다국어 정합, TODO-118 M4 부록 전체 템플릿 승격, TODO-120 KMS 기능 ID 등재.
  3. TODO-119: 이번 범위 밖 문서(20·20a·20c·06_AI_활용·25a·25b·26·08d 등) 본문에 남은 구 R2·R3·209건 표기를 결정 112 기준으로 맞춘다. 그전까지는 32a 대응표로 읽는다. 30 의 제안-P86-72 행에 남은 옛 게이트 이름은 과거 기록이라 그대로 둔다.

## 2026-11-09 — MDI 잡을 곳 옵션화(MdiMinGrabDip)·SDI 접힘 진단·PC 비움 이월 측정 마감(conv_179419201221, O4)

- 범위: MDI 창 끌기 잡을 곳 최소값을 설정 키 `MdiMinGrabDip`(기본 50, 범위 50~120 DIP)로 옵션화했다. `UiSystemSettings.cs` 를 300줄 이하로 줄이려 `UiSystemSettings.Timing.cs` 로 분리했다. `placement.state` 에 SDI 문서 grab·stripFolded 진단을 노출하고 selftest 는 `MdiDragLimit.UseFixedMinGrab(50)` 으로 고정했다. 판정기·측정 도구 결함(K5·C5·F08 v2·A07)은 제품 결함이 아니라 도구를 고쳤다(역라우팅 2회). Chrome 임시 프로필 종료 정리를 추가했다.
- 판정(최종 검수 `/home/rioky/.claude/session-env/5a01cad7-2b06-412f-a040-38c16799d4ad/work/otest-e_result.json` + 이후 `otest-r3_result.json`):

| 항목 | 결과 |
|---|---|
| AC-001~008·011·014 | PASS |
| AC-009 C5 touch 원자료 재판정·K7 | PASS (C5 first_frame 은 PENDING) |
| AC-012 A07 #7 | PASS (도구가 기동 모니터를 DISPLAY3 으로 보정한 뒤 재측정) |
| 180초 요청 | PASS (190.1s, ping 19건 응답) |
| 넓은 창 순환 | NO_CYCLE (진단 전용, 기준 없음) |
| Fold5 P1 펼침 웹 | PASS (open_grid p90 400ms) |
| AC-015 B2 | FAIL p95 31.21ms (이전부터 이월, 회귀 아님) |
| AC-015 W4 doc5 | FAIL 중앙값 33.4ms (publish/wwwroot 번들, 이전부터 이월) |
| AC-010 F08 v2 | UNDET (도구 안전 제약으로 끝점 도달 불가, 제품 판정 아님) |
| AC-013 K5 | 원자료 재판정 PASS, 공식 pcaway_gate 는 build.sha256 불일치 FAIL |
| AC-017 무변경 | 불충족 (승인된 stale device_lease 정리로 파일 변동, 팀원 금지 명령 pgrep -f 2회·pkill -f 1회 자진 보고) |

- 변경 문서: `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-18/plan/09_화면설계/01_화면설계_공통원칙.md`(설정 키 표기를 코드 저장 키 `MdiMinGrabDip` 로 정정), `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-18/plan/09_화면설계/03_데스크톱_화면설계.md`(같은 정정), `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-18/plan/09_화면설계/06_모바일_화면설계.md`(§6 before_window 정의 확인), `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-18/poc/ui/test/README.md`(새 도구 등재).
- 커밋 제외: `poc/ui/device_lease/`, `poc/ui/test/desktop/measure/pd_away_ps_*`, INFRA.md, 로그·bin 산출물.
- 이월 후보:
  1. B2·W4 성능 개선과 측정 분할(분석 `/home/rioky/.claude/session-env/5a01cad7-2b06-412f-a040-38c16799d4ad/work/odebug-perf_result.json`).
  2. PENDING 측정: P1 커버(closed) 웹, P4 첫 프레임, C5 first_frame(사용자 PC 복귀로 중단).
  3. F08 v2 실입력 잔존 수치 도달 불가(도구 안전 제약).
  4. UI-20 웹 펼침 분할 검사 b·c·f FAIL(범위 밖, 원인 미조사).
  5. K5 공식 pcaway_gate 가 빌드 역할(otestd_b1 대 desktop_work) 불일치로 FAIL.
  6. 기존 도구 `layout_cycle_per_op`·`retest_r2_titlebar_crop` 와 새 도구(넓은 창 순환·NOACTIVATE·C1) 중복 점검.
  7. NOACTIVATE 관찰 기준 없음, C1 가로 스크롤 캡처 판독 대기.

## 2026-11-09 — Q-YT-01~08 반영·출시 범위 대원칙(결정 110)·32 경계표 신설(conv_179422750914, O3)

- 범위는 문서뿐이다. 앱 코드·poc/ui·서버·INFRA.md 는 건드리지 않았다.
- 반영: 유튜브 영상 분석 질문 Q-YT-01~08 사용자 답을 결정 111 로 기록했다. 출시 범위 대원칙(PMS 본연 기능 전부 구현 후 출시, ERP·그룹웨어·KMS 제외)을 결정 110·ADR-095 로 등록했다. 경계표 32 를 신설했다. WBS 포인터와 MS-REL 후보를 추가했고 PMO 메뉴 묶음과 ISS-19 후보를 추가했다. SCH-03·04·달력, DSH-03, IMP-06, DOC-03, PRJ-07 부록도 갱신했다.
- 판정: 시험 AC-1~9 전부 PASS. 역라우팅 1회(메뉴구조.csv 재생성 누락 → 재생성 뒤 AC-5 재시험 PASS).
- 변경 문서(26건): `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-36/plan/03_개발범위/32_Pd_출시범위_PMS본연_경계표.md`(신규), `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-36/plan/90_결정관리/02_결정기록_부록/b18_ADR-095_096.md`(신규), 13 영상분석 적용계획, WBS 30·30a·일정표 31, 확장설계 22·22a, 기능목록(`features_data.py`·csv·xlsx·01_상세기능목록), 부록 a07_PRJ_2·a08_SCH·a09_ISS_1·a13_EVA_DSH·a17_IMP_AIN·a18_DOC, 메뉴구조(`menu_tree.py`·02_메뉴구조·csv), 결정대기 질문목록·ADR 본문, `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-36/plan/README.md`, `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-36/plan/index.json`, `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-36/.claude/rules/docs-map.md`.
- 이월 후보(사용자 확인 미결): (1) 경계 모호 A~J 분류, (2) 출시 전 R2 본연 단계명, (3) 자체 메일서버 출시 뒤 이전 여부(기존 'R2 무조건' 결정과 충돌), (4) G-R2·G-RELEASE 통합, (5) IAM-05 PMO 이동(화면 분할 필요), (6) ISS-19 정식 등재.

## 2026-11-09 — Q-PE-01~10 사용자 결정 반영·정책 템플릿 25h 신설·저해상도 폼 대응 문서 신설(conv_179422002337, O3)

- 범위는 문서뿐이다. 앱 코드·poc/ui·INFRA.md 는 건드리지 않았다.
- 결정: Q-PE-01~10 과 '평가 기준을 회사·부서·프로젝트·인원별로 지정' 요구를 D-172 / 결정 109 로 등록했다(ADR 은 신설하지 않음, 25h 가 확정 단계에 가면 ADR 후보). 바닥 60 단일안은 PoC 결과 반영 정책 템플릿으로 대체했고 25g 예시를 재계산했다(76.55, 가상 수치).
- 신설: 정책 템플릿 6종·회사>부서>프로젝트>인원 재정의 계층·충돌·감사·공정성·PMO 투입 시점 계상 원칙 문서 25h, 저해상도·소형 화면 폼 대응 문서 09_화면설계/10(Mars 문제는 사용자 진술, 수치는 후보).
- 변경 문서: `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-36/plan/05_아키텍처/25h_Pd_평가정책템플릿_범위별기준지정.md`(신규), `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-36/plan/09_화면설계/10_저해상도_소형화면_폼대응.md`(신규), 25f·25g·25 설계 문서, `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-36/plan/index.json`, `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-36/plan/90_결정관리/01_결정대기_질문목록.md`, `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-36/.claude/rules/docs-map.md`
- 이월 후보: 사용자 결정 Q-PE-11~14·Q-LR-01~04, 확정 뒤 EVA-02 부록·데이터 모델·API 반영.

## 2026-11-09 — 프로젝트 유형별 평가기준·다중 참여 개인 평가 후보 문서 신설(conv_179421010130, O3)

- 범위는 문서뿐이다. 앱 코드·poc/ui·서버·INFRA.md 는 건드리지 않았다. 모든 결론은 후보이고 사용자 결정은 확정하지 않았다.
- 내용: 유형 마스터(사업·유지보수·PoC·내부·R&D 예시), 유형별 ROI·KPI·가중치 프로필 초기 제시값, 가중치 방법론 비교와 권장안, 개인 종합점수 산식 Σ(w×S)/Σw, 가상 계산 예시, 권한·감사, 데이터 영향 후보, 사용자 결정 질문 Q-PE-01~10.
- 변경 문서: `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-36/plan/05_아키텍처/25f_Pd_프로젝트유형별_평가기준_다중참여평가.md`(신규), `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-36/plan/05_아키텍처/25g_Pd_다중참여_개인평가_산식.md`(신규), `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-36/plan/05_아키텍처/25_Pd_프로젝트평가_설계.md`(포인터), `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-36/plan/index.json`, `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-36/.claude/rules/docs-map.md`, `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-36/HISTORY.md`.
- 이월 후보: Q-PE-01~10 사용자 답변, 답변 뒤 EVA-02 부록·데이터 모델·API 문서 반영.

## 2026-11-09 — OCR·외부 AI 문서 추출 결과 형식 분석 문서 신설(conv_179420711646, O3)

- 범위는 문서뿐이다. 앱 코드·poc/ui·서버·INFRA.md 는 건드리지 않았다. 모든 결론은 후보이고 스키마·도입은 확정하지 않았다.
- 판정: 공유 대화 주장 17건을 공식 문서 검색으로 검증했다. 맞음 8, 부분 6(PaddleOCR 는 요약 근거), 확인불가 3. 확인불가는 Textract 의 Markdown·HTML 직접 출력, Claude API 의 좌표 필드, Claude API 의 신뢰도 필드다. Claude 의 이미지 위치 서술 능력과 API 보증 필드를 구분해 적었다.
- 한계: 하위 에이전트 spawn 이 hook 으로 차단되어 team-lead 가 대신 web-fetch 를 받았다. Azure·Textract·Claude 는 본문 대조, PaddleOCR 는 페이지 끝 약 26,000자 미확인(요약 근거)이다.
- 변경 문서: `/mnt/c/DATA/Project/Pd/plan/04_기술스택/25_Pd_OCR_외부AI_문서추출_출력형식_분석.md`(신규), `/mnt/c/DATA/Project/Pd/plan/index.json`, `/mnt/c/DATA/Project/Pd/.claude/rules/docs-map.md`, `/mnt/c/DATA/Project/Pd/HISTORY.md`.
- 이월 후보: 사용자 결정 Q-OCR-01~08 답변, P0~P1 표본 세트 선정과 엔진별 출력 실측(별도 승인), PaddleOCR·Textract 필드 실측.
- 시험 뒤 정정 3건: PaddleOCR 출처를 paddleocr.ai 실제 확인 주소로 교체(옛 주소는 리다이렉트), Textract 출처에 APIReference 의 API_Block·API_Geometry 추가, C-10 의 쪽 비율 정규화 서술은 근거가 없어 미확인으로 표기했다(같은 서술이 있던 본문 한 줄도 함께 고침).
- 막힘·해결: oplan 이 배정표에 원본 폴더 경로를 써서 F-CRUD-1 이 worktree 신규 문서를 판독하지 못했다. 배정표를 worktree 경로로 고쳐(version+1) 해결했다. 상세는 LESSONS.md 참조.

## 2026-11-09 — 측정 사이클(F08·T3·K4·K5·A01·웹 F08·Fold5 C5/K7 이월) 판정 반영(conv_179418274562)

- 범위는 측정과 문서 반영이다(코드 변경 없음). 기준 빌드는 HEAD 2ae2870, Desktop.dll sha256 `45af231e…`, 웹 `c179417d_web_diag`(재빌드 없음). 증거 `/home/rioky/.claude/session-env/5a01cad7-2b06-412f-a040-38c16799d4ad/evidence/otest_d_results.md`·`otest_w_results.md`·`otest_m_results.md`.
- 판정: 웹 F08 PASS(12건 중 실패 0). T3 공식 final PASS. K4 PASS. K5 열 폭 끌기 PASS(행 줄무늬·선택 행 줄은 미측정). 데스크톱 F08 은 L2 경로 36건 실패 0(잔존 159 DIP·잡을 곳 51 DIP)이지만 **공식 게이트는 FAIL**(잔존 421 > 158+2)이다. FAIL 원인은 도구 끝점 구조 한계로 계산했으며 확정이 아니고 기준은 완화하지 않았다. A01 은 SDI 9건·다중 SDI 3건 PASS, 메인 창 9건은 `MainBgPinHook` 이 이동을 되돌려 미측정(원인 확정)이다. SDI 분리 뒤 줄 접힘 상태는 `placement.state.grab` 이 MDI 패널만 읽고, `tree.get`/`ui.find` 로 SDI 창 트리는 읽히나 접힘 상태 노출은 라이브 미확인이라 미검증 유지다. Fold5 C5·K7 은 APK 가 구판이고 접힘에 사용자가 필요해 재측정하지 않았다.
- 변경 문서: `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-18/plan/09_화면설계/03_데스크톱_화면설계.md`·`04_웹_화면설계.md`·`06_모바일_화면설계.md`, `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-18/poc/ui/test/README.md`(`retest_maximize_monitor_fg.py` 등재), `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-18/LESSONS.md`.
- 새 도구: `poc/ui/test/desktop/measure/retest_maximize_monitor_fg.py`(등재). `poc/ui/test/desktop/measure/retest_pcaway_extra2_f08far.py` 는 목적 미달로 삭제했다(창 안 끝점 변형 시도는 창 미이동, 원인 미확정). README 에도 등재하지 않았다.
- 이월 후보(사유): (1) F08 실입력 공식 게이트 — 제품 한계 -481 에 닿을 수 있는 끝점 구성 필요. (2) K5 행 줄무늬·선택 행 줄 — 도구가 항목 이름을 만들어야 측정된다. (3) A01 메인 창 9건 — `--bg-pos` 없이 화면에 기동하는 새 판(터미널 모니터 노출 위험). (4) SDI 분리 뒤 줄 접힘 상태 읽기 경로. (5) Fold5 C5·K7 — 새 Release APK 빌드·설치, 사용자의 접힘·펼침 배치, before_window 수용 판단. (6) 이전 사이클 이월 중 B2 프레임 p95, A07 HOLD 4건, W4 doc5, P4, P1·웹 dpr 실기, 180초·C1, 넓은 창 레이아웃 순환 고리 특정은 이번 사이클에서 다루지 않아 그대로 남는다.

## 2026-11-09 — F08 MDI 창 끌기 잡을 곳 50 DIP 보장(줄 접기)·C5/K7 판정기 보정·진단 좌표 정정(conv_179417057011, O3)

- 범위는 다섯 가지다. (1) F08: MDI 창 끌기 잡을 곳을 50 DIP 이상 보이게 하고 밀려나면 기능 버튼 줄을 접으며 다시 펼 때 8 DIP 여유를 둔다(신규 `poc/ui/src/Pd.Poc.Shell/Docking/MdiStripFold.cs`, `MdiDragLimit.cs`·`MdiGrabZoneReader.cs`·`MagnetMdiLayoutManager.cs`·`SdiScreenPlacement.cs`·`WindowActivation.cs`). 10% 규칙 폐지는 사용자 결정(2026-11-08), 줄 접기 C안은 자율 판단이다. 왼쪽 잔존은 353 DIP 에서 159 DIP 로 줄었다(실측). (2) C5·K7 판정기가 화면이 뜨기 전(before_window) 탭을 판정에서 뺀다(boundary·cls=other 는 계속 판정). (3) 진단 AreaJson 이 입력 좌표계 기준 x·y·w·h 를 보고하고 winX·winY 를 추가했으며 DocPanel 헬퍼로 문서 담긴 패널을 고른다(`RemotePlacementCommands.cs`). (4) 최대화 뒤 복구 시 줄이 다시 접히게 했다. 원인은 TitleGrid 재배치 전에 RightInset=-32 를 읽은 것이며 낡은 구간이면 판정을 건너뛰고 InvalidateArrange 를 다시 요청한다. (5) 측정 도구(`retest_mdi_drag.py`·`retest_mdi_drag_web.py`·`retest_pcaway_extra2.py`·`pcaway_report_map.py`)를 새 규칙에 맞췄다.
- 역라우팅 4회: AreaJson 좌표, 복구 재접힘, DocPanel, 근본원인 재현 후 MdiStripFold 수정.
- 판정 결과: 데스크톱 otest_done=PARTIAL(AC-001~005·008·009·011·016 PASS). AC-010 은 r5 재판정에서 C5·K7 FAIL 을 그대로 기록했다(숨기지 않는다). 웹 PASS(AC-003 웹, AC-006 12/12). 최종 빌드 desktop c179417d, web c179417d_web_diag.
- 변경 문서: `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-18/plan/09_화면설계/01_화면설계_공통원칙.md`·`03_데스크톱_화면설계.md`·`06_모바일_화면설계.md`(159 DIP 실측 반영).
- 이월 후보(사유): F08·T3 공식 final, K4·K5 live, A01 화면 최대화 계열은 포그라운드가 필요해 PC 를 비울 때 진행한다(A01 은 승인 후 실행했으나 시험 앱이 기동 직후 전경을 잡아 게이트가 중단, NOACTIVATE 에도 전경을 잡은 원인은 다음 사이클 확인). SDI 분리 뒤 줄 접힘 상태는 `placement.state.grab` 이 MDI 패널만 읽고 SDI 창 트리(`tree.get`/`ui.find`)의 접힘 상태 노출은 라이브 미확인이라 미검증. C5·K7 은 after_window·boundary 유실이 남아 PC 를 비우고 Fold5 로 재측정. B2 프레임 p95, A07 HOLD 4건, W4 doc5(헤드리스 규칙 우선), P4, P1·웹 dpr 실기(Fold5 Chrome 사용 허락 대기), 180초·C1(계획서 §3-5 정의만). 잡을 곳 50 값의 설정 파일 연결은 `UiSystemSettings.cs` 분리가 선행. 상세는 `/home/rioky/.claude/session-env/5a01cad7-2b06-412f-a040-38c16799d4ad/evidence/otest_deferred.md` 참조.

## 2026-11-08 — 패널 문서 끌어 빼기 복구·패널 자동 기본값·기기 정보 기반 UI 형 판별(conv_179413554826, O4)

- 범위는 여섯 가지다. PC형에서 패널에 넣은 문서를 다시 끌어 뺄 수 없던 결함 수정(`PanelQuery.cs`·`PanelCornerService.cs`), 패널 기능이 끔이어도 원하는 위치로 끌어 빼기 허용(분할 안내만 숨김, `PanelFeatureRules.cs`), 앱·웹 및 PC형·태블릿형 동일 동작, 패널 자동 기본값(PC형 켬·태블릿/폰형 끔, `UiStyleRules.cs`·`UiSettings.Parts.cs`·`UiSettingsDto.cs`·`UiSystemSettings.cs`), 기기 정보 기반 UI 형 판별(신규 `DeviceTraits.cs`·`AndroidDeviceInfo.cs`, `InputFormFactor.cs`, 폴드·태블릿은 자동이면 태블릿형·접은 폴드는 모바일형), 설정 화면 반영(`WindowPage` VM·axaml, `SettingsInventory.cs`, `shell.ko/en.json`, `UiStyleViewModel.cs`, `RemoteUiModeCommands.cs`, `MainActivity.cs`, 웹 `Program.cs`)이다.
- 판정 결과: 검수 AC 26건 FAIL 0, PARTIAL 4건(대체 측정). 역라우팅 2회(리뷰로 selftest 26→27, Fold5 AC-023 은 `Configuration.SmallestScreenWidthDp` 가 멀티윈도우에서 창 기준이라 생긴 결함이어서 `MaximumWindowMetrics` 로 수정).
- 자율 판단 결정(정본에 표기): 분할 화면 폭 600dp 미만은 모바일형(옵션 `compactWindowKeepsTablet` 로 덮어쓰기), 정밀 포인터가 있는 터치 노트북은 PC형, 웹 접힘은 폭 폴백, 끔에서도 다른 패널 탭 영역 놓기 허용, 옵션은 태블릿 기기에만 적용.
- 변경 문서: `plan/05_아키텍처/08_지원_폼팩터_용어.md`, `plan/09_화면설계/01_화면설계_공통원칙.md`·`03_데스크톱_화면설계.md`·`04_웹_화면설계.md`·`06_모바일_화면설계.md`, `poc/ui/test/README.md`(신규 `desktop/judge/device_class_table.py`·`mobile/measure/m1_device_class_android.py` 색인).
- 이월 후보(PARTIAL 대체 측정 4건): `/home/rioky/.claude/session-env/5a01cad7-2b06-412f-a040-38c16799d4ad/evidence/otest_deferred.md` 참조. 헤드리스 Chromium dpr 에뮬레이션 폭 절반 현상의 실기 확인.
- 참고 기록(이월 아님): 켬 패널 끌기 연속 실행에서 났던 FAIL 2건은 같은 판정본으로 전체 연속을 다시 돌리자 100 PASS·FAIL 0 이었다(11-05 기준선도 100 PASS). 재현되지 않는 일시 FAIL 이며 코드 회귀 증거는 없다. 증거는 `/home/rioky/.claude/session-env/5a01cad7-2b06-412f-a040-38c16799d4ad/evidence/otest_axes/desktop2_drag_work3` 이다.

## 2026-11-08 — oroof 반복 1 — 레이아웃 순환 수정(ZoomDecorator Hold)·첫 cascade 잘림 C1·진단 보정·Fold5 r5 재측정(conv_179371711303, O4, auto)

- 범위는 다섯 가지다. ZoomDecorator Hold 규칙(`poc/ui/src/Pd.Ui.Views/ZoomDecorator.cs`), 진단 쪽 `perf.reset` 이 프레임 표본도 초기화(`PerfFrameLog.cs`·`PerfProbe.cs`·`PerfProbe.Product.cs`)와 원격 응답 쓰기 보정(`RemoteCommandRouter.cs`·`RemoteControlServer.cs`), 첫 cascade 잘림 C1(`poc/ui/src/Pd.Poc.Shell/Docking/DocumentLifecycle.cs`), 시험 도구(`perf_repeat.py`, 신규 `layout_cycle_per_op.py`·`retest_r2_titlebar_crop.py`, README 색인), 그리고 위 수정의 재측정이다.
- 판정 결과(원자료 `/home/rioky/.claude/session-env/5a01cad7-2b06-412f-a040-38c16799d4ad/work/` 아래 결과 JSON, 미통과를 숨기지 않는다)
  - 순환 수정 PASS(otest-21): 순환 줄 수 Hold on 0·off 15441·r3 15421, 화면 안 n1 정렬 p95 62.45ms·스크롤 59.17ms 로 n20 수준. 회귀 없음, K4·K5 48/48, retest_mdi_drag 35건 실패 0. 원격 perf-ui 는 167.6초까지 정상 응답(180초 초과 미재현, 조건부 PASS).
  - C1(otest-22, 화면 밖 r5): Hold on 순환 0·off 19줄 로 Hold 효과는 확인했으나 C1 단독 효과는 분리하지 못했고 가로 스크롤 막대는 UNDET 이다.
  - 웹: W4 1차 프레임 0건 원인은 도구 Chrome 기본 위치 x=-2500(단일 모니터 밖)이다. 화면 안 재측정 grid 열기 p50 335.6·p95 536.9ms(기준 500 초과 유지), doc5 끌기는 사용자 복귀로 미측정. perf05 중앙 3799.2ms.
  - Fold5 r5 새 빌드(otest-m-23): C5 FAIL(전부 before_window, 첫 프레임 뒤 1건 0 노드), K7 판정기 rc=1(bad 1건은 앱 창 전 입력이 20초 뒤 도달한 사례, 재전송 오배달 0), T4 UNDET(컨트롤 탐색 실패), P1·P4 PC 사용 중이라 보류.
  - 정정: 03_데스크톱_화면설계.md §7 의 K5 'FAIL·새 판 재측정 남음' 문구를 r3 새 판 도구 PASS 로 바로잡았다. goal 미통과 24건 중 이번 반복에서 PC·Fold5·사용자 결정이 필요한 항목은 남는다.
- 변경 문서: `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-18/plan/04_기술스택/04_판정보고서_부록/a9_성능_웹_모바일_4_5_6.x.md`(반복 1 재측정), `plan/09_화면설계/03_데스크톱_화면설계.md`(K5 정정·순환·C1), `04_웹_화면설계.md`(W4·perf05), `06_모바일_화면설계.md`(Fold5 r5).
- 이월 후보: 화면 안 첫 cascade 재확인(C1 단독 효과)·W4 doc5 끌기·perf-ui 180초 초과 최종 확인·T3 공식 판정(--before-strip/--before-tab)·T4 DOCK 형 제목 탭 상태 확인·Fold5 C5 첫 프레임·P1·P4·K7 diag 판본.

## 2026-11-08 — PC 비움 재시험·A10/K5/G1 수정·Fold5 원인 확정·성능 재측정(conv_179371711303, O4, auto)

- 범위는 다섯 가지다. 데스크톱 r3 최종 빌드 재판정, A10 2차 수정(`CrossRootLayoutFlush.Track`·`DocumentViewLocator`)·K5 수정안 B(`FitStar`)·G1 그리드 경량 텍스트 열(`PdTextColumn`)·perfreps(`--reps`·`p95_interp`) 구현, 웹 재시험, Fold5 스플래시 탭 유실 원인 분석, 성능 재측정이다. 사용자 결정은 성능 판정을 실제 동작시간으로 하는 것(부하는 참고)이다.
- 판정 결과(미통과 항목을 숨기지 않는다. done_gate 판정은 고부하로 생략됐고 goal.json acceptance 중 아래 FAIL 이 미통과로 남는다)
  - 데스크톱 r3 재판정은 22 PASS 이고 A10 은 PASS(6/6, FATAL 0)다. G1 은 r3 대 a10_fix 38단계 셀 값 전부 일치로 동작 동등 PASS 다.
  - K5 는 동결 판 도구가 FAIL(기대값 90 대 실측 88, 도구 기대값 결함)이었으나 새 판 도구(43d67f52)로 r3 게이트 PASS 이다.
  - 데스크톱 FAIL 은 E5(수정 전 추정 복원판에서도 결함이 재현되지 않아 before_fix_reproduced FAIL, 기능 3건은 PASS)·B1~B3·B5·F08·F15 등 남은 항목이다. B2·B3 는 성능 결함으로 확정된 값이 있다.
  - 웹은 PARTIAL 이다. D1·E6·C3 PASS, F14 는 근거가 약하다(결과 파일 `pcaway/F14.json` 부재). W4 와 perf05 는 FAIL 이고 W4 재측정은 Chrome 가림으로 blocked 다.
  - Fold5 결론은 제품 결함이 아니라 플랫폼 동작과 측정기 분류 오류다. 앱 창의 첫 프레임이 뜨기 전에 주입된 탭을 Android InputDispatcher 가 앱에 전달하지 않는다(logcat 대조로 유실 156/156 이 앱 창 밖 처리). 측정기 `classify_loss` 의 분류 오류를 고쳐 새 판 도구(`retest_android_pcaway_splash.py` 60d766dd, `retest_android_pcaway_replay.py` 40266ed1)를 만들었다(`work/odebug-fold_result.json`).
- 성능 실측(otest-20, 자기 앱을 화면 밖·임시 프로필로 띄운 조건이라 이전 B2·B3(화면 안)와 조건이 다르다)
  - G1 효과는 미미하다. 웜 measure1 p50 90→77ms, 콜드 266→283ms, 열기 elapsed 웜 150→146ms 이다. 원인 가설 X1(그리드 셀 실현 비용)이 거의 틀린 것으로 보며 재분석을 이월한다. 이전 '30~50% 감소 예상'은 틀렸다.
  - R2R 게시본 첫 프레임은 883~1732ms 이다. 웹 perf05 W1 중앙은 4.0~4.2초(이전 공식값 7.25초)이나 서버 조건이 달라 참고값이고 화면 가림 영향이 있을 수 있다. 최대 구간은 avalonia_setup 약 2.1초이다. 공식 B1·B2·B3·W4 emit 은 없다.
  - 최종 빌드 판본표 desktop_work 를 r2f→r3 로 바꿔 r2f 로 받은 결과가 빌드 불일치로 게이트 FAIL 이 됐고, PASS 20개 재실행과 K4·K5 재실행으로 보정했다.
- 변경 문서: `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-18/plan/04_기술스택/04_판정보고서_부록/a9_성능_웹_모바일_4_5_6.x.md`(G1 효과·R2R·웹 재측정·Fold5 원인), `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-18/plan/09_화면설계/01_화면설계_공통원칙.md`(§5.4 G1 효과 정정), `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-18/plan/09_화면설계/03_데스크톱_화면설계.md`, `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-18/poc/ui/LAYERS.md`, `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-18/poc/ui/README.md`, `/mnt/c/DATA/Project/Pd/.claude/worktrees/pd-18/poc/ui/test/README.md`(도구 색인), 이 HISTORY·LESSONS 이다.
- 변경 소스·도구 신규 파일은 `PdTextColumn.cs`·`CrossRootLayoutFlush.cs`·`RootSafeRecyclingDataTemplate.cs`·`DocumentTabKeepAlive.cs`·Android `Startup*.cs`·`poc/ui/test/**` 시험 도구(`perf_repeat.py`, `dense_wheel_check.py` 새 판, 웹 `e6_web.py`·`d1_web.py`·`perf05_web.py`·`w10_web.py` 등)·`poc/ui/tools/serve_browser.py` 이다. 같은 작업 트리에 앞 사이클(11-06) 미커밋분이 섞여 있어 아래 11-06 항목을 함께 둔다.
- 이월 후보
  - G1 효과 재분석, W4 재측정(Chrome 을 가리지 않게), 화면 안 perf-ui, `perf.reset` 이 프레임 표본을 비우지 않는 문제(move20 은 1회차만 유효), 원격 perf-ui 180초 끊김.
  - T3 확대 좌표, K5 넓은 창 레이아웃 순환(기존 결함), B2 F2·F3, 웹 avalonia_setup, perf05 공식 조건 결정, F14 결과 파일 확인.
  - Fold5 C5·P4·K7 재측정(새 판 도구), P1 재측정(재연결 뒤), Fold5 기동~첫 프레임 단축 결정.
  - 사용자 판단 대기는 `before_window` 플랫폼 한계 수용 여부다.

## 2026-11-06 — GitLab→GitHub 단방향 push 미러 유지(D-163 종결, 결정 100)·ops 실측(pd-71) plan 반영(conv_179392430001, O3)

- 범위는 문서뿐이다. 앱 코드·poc/ui·서버·INFRA.md·하네스는 건드리지 않았다.
- 결정 100: GitHub(비공개)는 GitLab 의 단방향 push 미러로 유지한다. 정본은 GitLab 이고 GitHub 에는 직접 push 하지 않는다. D-163 은 종결했고 결정 98 의 "미러 미설정" 서술은 현행화했다(이력 서술은 보존).
- ops 실측(pd-71) 반영: 계정·SSH 키·push 미러·백업(매일 03:00, 14일 보관)·서버 복제본·임시 브랜치 정리를 `plan/04_기술스택/24_Pd_저장소_정본_내부서버_전환_조사.md` §9.10.2 에 기록했다. 서버 작업 복제본 현행 경로는 `/data/pd/Project/Pd` 이다(사용자 지시로 이전 완료).
- 변경 문서: 24 문서, `plan/00_요구사항/01_사용자요구사항_원문.md`(결정 100·원문 6), `plan/index.json`(D-163 확정), `plan/90_결정관리/01_결정대기_질문목록.md`·`03_향후과제_TODO.md`·`02_결정기록_ADR.md`·`b12_ADR-085_087.md`·`b4_ADR-024_017.md`, `plan/05_아키텍처/20·20a`, `plan/07_구축_배포/02·03`, `plan/README.md`, `.claude/rules/docs-map.md`.
- 역라우팅 1회: 시험(otest)에서 D-163 질문 칸·24 문서 U-11·TODO-111 표기와 AC-011·012 판정 시간(timeout_sec+cache_inputs)을 보완했고 재시험 PASS 였다. AC 기준표 두 사본은 리드 승인으로 동기화했다.
- 이월 후보: TODO-109 서버 재부팅 뒤 자동 기동 시험, TODO-110 헬스체크 허용 목록, TODO-112 서버 밖 GitLab 비저장소 데이터(이슈·설정 등) 백업, TODO-113 Windows git SSH 인증. TODO-111(설정·비밀 백업)은 서버 안 백업으로 완료했고 같은 서버 보관이라는 한계가 남는다.

## 2026-11-06 — GitLab 정본 전환·Gitea 제거·pd 그룹 변경 plan 반영(결정 97~99, conv_179392284915, O3)

- 범위는 문서뿐이다. 앱 코드·poc/ui·서버·INFRA.md·하네스는 건드리지 않았다. 수정 금지 경로 변경 0건.
- 결정 97: 저장소 서버는 GitLab CE 단일, Gitea 는 쓰지 않고 제거 완료. 결정 98: 모든 산출물의 정본을 GitLab 으로 정하고 전환 완료(2026-11-06, `git_canonical=gitlab`, 프로젝트 pd/Pd). GitHub 는 비정본이며 미러는 설정하지 않았다(D-163). 결정 99: pd 기본 그룹 gid 1007, 결정 89(cs1 유지) 개정.
- 정본 전환 실측 반영: `plan/04_기술스택/24_Pd_저장소_정본_내부서버_전환_조사.md` §9.10·§9.10.1(ops pd-71 실측 13건, U-2/3/4/6 해소). ADR-086 확정, ADR-017 에 Gitea 이력 주석. 구축·아키텍처·로드맵 문서의 Gitea 현행 서술은 GitLab CE 로 교체(이력 문서 본문은 보존). 요구사항 원문 97~99 추가.
- 변경 문서: 24·`plan/90_결정관리/01_결정대기_질문목록.md`(D-112~115 갱신·D-163 신설)·`03_향후과제_TODO.md`(TODO-89·90·80 갱신, TODO-109~111 신설)·`02_결정기록_ADR.md`·b1·b4·b12·p88·p89, `plan/07_구축_배포/01~04·06`, `plan/05_아키텍처/20·20a·21g·23`, `plan/04_기술스택/21`, `plan/03_개발범위/20b`, `plan/README.md`, `plan/index.json`, `.claude/rules/docs-map.md`.
- 이월 후보: D-163(GitHub 미러 여부, 사용자 결정), TODO-109 실제 서버 재부팅 뒤 자동 기동 시험(이번엔 rootless docker 데몬 재시작만 시험), TODO-110 헬스체크 허용 목록, TODO-111 설정·비밀 백업 절차(셋 다 ops 인계).

## 2026-11-06 — GitLab 시험 설치 승인(2026-11-06)·차트 Waterfall front 인계·plan 잔여 처리(2026-11-06) 반영(conv_179389172420, O3)

- 범위는 문서·생성기뿐이다. 앱 코드·poc/ui·서버·INFRA.md·하네스는 건드리지 않았다. 수정 금지 경로(09_화면설계/01·03·04·06, 04_기술스택/02·05, 07_구축_배포/05, poc/ui) 변경 0건.
- GitLab 시험 설치 승인(2026-11-06): 사용자 원문 "gitlab 시험설치하라". 실제 설치는 ops 인계 대기(ops·all 세션 없음, front pd-49 [인계거절]). 절차서는 `plan/04_기술스택/24_Pd_저장소_정본_내부서버_전환_조사.md` §9. D-112~114·TODO-90·ADR-086 동기.
- 차트 Waterfall: front 세션 pd-49 [인계결과] 반영 — 4종(ScottPlot·LiveCharts2·OxyPlot·ProCharts) 렌더 성공·전부 MIT, ProCharts 사용 불가, LiveCharts2/ScottPlot 조합 후보. D-131 후보 판정(예외 아님, WASM·Android·음수 미실측이라 확정 보류).
- 잔여 처리(2026-11-06): Business 5기능(PRJ-04·DSH-02·IAM-09·IAM-07·AIP-12) 기업용 열 반영(`features.py` `CORP_BUSINESS`, 부록·생성물 재생성, D-148 초기 제시값 채택), ADR-077·079 확정(자율 판단 결정 86), 메뉴 ID 확정(`MENU_ID_CONFIRMED=True`, R-3 불변 적용), 설정 키 3개 잠정 등재, CV-12·13 M2 데이터 출처, 공식 문서 2차 조사(D-155 Google usageMetadata 확인, D-161·162 law.go.kr 원문 확인 0건).
- 변경 문서: `plan/90_결정관리/03_향후과제_TODO.md`(103~108 처리 결과·잔여 전수 분류 절)·`01_결정대기_질문목록.md`·`02_결정기록_ADR.md`·`b9`·`b17`, `03_개발범위/20c`, `01_시장조사/10·11`, `09_화면설계/02·08·08a·08b·09`, `06_기능목록/20`·부록 5곳·생성물, `plan/index.json`, `.claude/rules/docs-map.md`.
- 이월(사용자 조치): 법무·세무 원문 확인(TODO-107), 협회·금감원 문의(D-147·TODO-101), 성능 측정 PC 미사용 시간 지시(TODO-53·77), REA MCP 재연결(TODO-89), 상표·회계·외부 보안 검토. 시점 대기: `09_화면설계/01` 165행 ADR-077 후보 표기(수정 금지 파일, 소유 세션 갱신 때 동기), 내부 LLM 원가 실측(서빙 도입 때). 사용자 확인 대상: Business 초기 제시값. front 잔여: WASM·Android·음수 값 실측.

## 2026-11-06 — 사용자 결정 90~96 plan 정본 반영(conv_179389172420, O4): 요금 5등급·AI 원가 요율·운영 디버그 제거·법령 조사

- 범위는 문서뿐이다. 앱 코드·poc/ui·서버는 건드리지 않았다. 수정 금지 경로(09_화면설계/01·03·04·06, 04_기술스택/02·05, 07_구축_배포/05, poc/ui) 변경 0건.
- 결정: 90 운영 디버그 제거·서버 전송 오류 로그 전 채널(보조로 오류 로그 보존 90일·마스킹), 연 2,088시간, 요금제 5등급(Business 는 후보)·애드온은 기업용 한정, AI 요금은 원가×1.2/1.5, 기준 환율 고정. 자율 판단 18건과 조사 6건(법령·공급자 API·차트 Waterfall) 반영. 신규 ADR-094(`plan/90_결정관리/02_결정기록_부록/b17_ADR-094.md`).
- 신규 문서: `plan/01_시장조사/10_법령_조사_거래처_연봉_선불.md`, `plan/01_시장조사/11_공급자API_차트_조사.md`, `b17_ADR-094.md`. 그 외 요구사항 원문·03(07·20b·20c)·05(05·21e·21h·25·25b·25c·25e·26)·06 부록(a02·a03·a04·a13·a15_AIP_1·2)·09(08·08a·09)·90(질문목록·ADR·b11·b13·b16·TODO)·`plan/README.md`·`plan/index.json`·`.claude/rules/docs-map.md` 를 수정했다.
- 결정 대기 D-128~157 종결, D-158~162 신규, TODO-104~108 신규.
- 판정(otest-1): TEST PASS(AC-001~017, 대체 판정 AC-003·005·008·010), 역라우팅 1회(README b17 등재).
- 이월 후보
  - 사용자 행동: D-147 협회 문의, TODO-101 금감원 문의.
  - D-158~162 출시 전 법무 확인, TODO-103 Business 확정, TODO-104~108.
  - GitLab 시험 설치는 별도 사이클 승인 대상이다.

## 2026-11-05 — 사용자 결정 49~89 plan 정본 반영(conv_179388590709, O4): 옵션화 원칙·메뉴 ID·요금 애드온·AI 과금

- 범위는 문서와 `plan/_build/` 생성·검사 스크립트뿐이다. 앱 코드·poc/ui·서버는 건드리지 않았다. 수정 금지 경로(09_화면설계/01·03·04·06, 04_기술스택/02·05, 07_구축_배포/05, poc/ui) 변경 0건.
- 결정: 옵션화 대원칙 ADR-089, 빌드 채널 ADR-090, 차트·칸반 공통 ADR-091, 영업 차트 ADR-092, 요금 애드온·AI 과금 ADR-093. ADR-084·085·086·088 개정. 결정 89 는 그룹명을 cs1 로 유지한다(pd 그룹 없음, 사용자 확인).
- 수치: 기능 209건 확정 + 후보 1건(FIN-19, 합계 210), 화면 86개(메뉴구조.csv 생성물 기준), 결정 대기 D-128~157, TODO-101~103.
- 판정(otest-1): AC 31/31 PASS(대체 판정 AC-016·017·022·029 적용). check_feature_detail(기본·--wbs·--cite-check)·check_ai_usage·check_m1_coverage·md_audit·plan_audit·check_secrets 전부 rc=0. 생성기 재실행 전후 생성물 md5 동일. 빌드·배포 대상이 없어 phase1 은 N/A 로 기록했다.
- 신규 문서(9): `plan/03_개발범위/20c_Pd_솔루션기획안_요금_패키징_AI과금.md`, `plan/05_아키텍처/21g_Pd_연계명세_운영횡단.md`, `plan/05_아키텍처/21h_Pd_연계명세_AI과금_한도집행.md`, `plan/05_아키텍처/21i_Pd_연계명세_외부계정로그인.md`, `plan/05_아키텍처/25e_Pd_프로젝트평가_결정59_85_반영.md`, `plan/90_결정관리/02_결정기록_부록/b13_ADR-089_090.md`·`b14_ADR-091_092.md`·`b15_ADR-088.md`·`b16_ADR-093.md`.
- 수정 문서: 요구사항 원문, 01 시장조사 09, 03_개발범위(01·06·07·20b·30·30a·31), 04/24, 05(05·21·21c·21e·21f·22·22a·24·25·25a·25b·25c·25d·26·26a), 06 기능목록(01·20·20a·20c·21 색인, 부록 a02·a03·a04·a07_PRJ_2·3·a08·a09_ISS_1·a12_FIN_1·2·a13·a14·a15_AIP_1·2·a17, csv·xlsx), 07(03·06), 08 corp, 09(02·08·08a·08c·08d·08e·09·메뉴구조.csv), 90(질문목록·ADR·b0·b11·b12·TODO), `plan/README.md`, `plan/index.json`, `.claude/rules/docs-map.md`, `plan/_build/` 스크립트 4개. 총 81개 경로(수정 72 — HISTORY·LESSONS 포함, 신규 9, git status 기준).
- 이월 후보
  - D-128~157 사용자 확인(결정 53·55·57·58·60·61·65·66·78·82·83·88 의 부분 반영분).
  - 사용자 행동: D-147 협회 문의, TODO-101 금감원 문의.
  - GitLab 시험 설치는 별도 사이클 승인 대상이다.
  - plan_audit 경고 26건(IPV4 22·LONG 3·GENERATED 1)은 실서버 IP 가 아니다(패키지 버전·커널 버전·RFC 절 번호 오탐). 전부 이번 변경 밖 기존 문서다.

## 2026-11-05 — 결정 48 거래처 2층 구조·법인 마스터 상세 설계(conv_179387241368, O4): 전부 후보

- 범위는 문서·검사 스크립트뿐이다. 코드·poc/ui·서버는 건드리지 않았다. 사용자 결정 대행 없이 모두 후보로 표기했다.
- 판정(otest-1): AC 20/20 PASS(AC-001~020), 검사기(check_ai_usage·check_feature_detail·check_m1_coverage·menu_tree) rc=0.
- 신규 문서(5): `plan/05_아키텍처/26_Pd_거래처_법인마스터_설계.md`, `plan/05_아키텍처/26a_Pd_법인마스터_출처_조사.md`, `plan/05_아키텍처/21f_Pd_연계명세_법인마스터갱신.md`, `plan/06_기능목록/21_기능요건상세_부록/a07_PRJ_3.md`, `plan/08_데이터모델/corp.md`.
- 결정 후보: REQ-63, ADR-088, 결정 대기 D-120~127, TODO-95~100. 주민등록번호 등 고유식별번호는 저장하지 않는 설계로 적었다(AC-020).
- 수정 문서: 요구사항 원문, 03_개발범위(01·06·20b·30·30a·31), 05(21·22·22a·24), 06 기능목록(상세 설계 20·20a·20c, 21 색인, 부록 a04·a07_PRJ_1·2·a12_FIN_1·2·a14, csv·xlsx), 08(00_개요_ERD·prj), 09(02 메뉴구조·08d·csv), 10 prj, 11 outbox, 90 결정관리(질문 목록·ADR·b0·b11·TODO), `plan/README.md`, `plan/index.json`, `.claude/rules/docs-map.md`, 검사·생성 스크립트(`plan/_build/`). 총 53개 경로(수정 47 — HISTORY·LESSONS 포함, 신규 6, git status 기준).
- 기능 총수는 206건 유지, 거래처 선택 규칙은 CRM-01·04, FIN-02·07·14 에 후보 문장으로 추가했다. 화면·메뉴 파생물은 생성기에서 재생성했다.
- 이월 후보
  - D-120~127 사용자 질문(특히 D-123 은 PRJ-17·ADM-10 M1 후보와 PRJ-18 M2 후보를 나눠 묻는다).
  - TODO-95~100(발송된 견적과 병합 재지정 충돌, FIN-11 세금계산서 party 참조 설계 포함).
  - `plan/README.md` 35행의 '기능 206건'은 결정 당시 값이라 유지한다. `plan/index.json` 에는 ADR 항목이 없는 기존 구조다.

## 2026-11-05 — 사용자 결정 46건 plan 정본 반영(conv_179386398389, O4): 기능 206건·화면 81개(후보 10), ADR-085~087, 서버 uidmap·SSH 키 로그인

- 범위는 문서·스크립트가 중심이다. 사용자 결정 1~46 에 47(칸반·영업 차트)·48(거래처 마스터 후보)을 더해 plan 정본에 반영했다. poc/ui·앱 코드는 건드리지 않았다(poc diff 0).
- 수치(otest-1 실측): 기능 206건(EVA-05·PRE-08·HRS-06 신설), 화면 81개(후보 10개), 변경·신규 66개 파일(수정 59, 신규 7, git status 기준).
- 신규 문서: `plan/04_기술스택/24_Pd_저장소_정본_내부서버_전환_조사.md`, `plan/05_아키텍처/21e_Pd_연계명세_연봉수신.md`, `plan/05_아키텍처/25c_Pd_내부프로젝트_가치산정.md`, `plan/05_아키텍처/25d_Pd_프로젝트평가_점수방법_진행매출.md`, `plan/09_화면설계/08e_차트뷰_영업단계_파이프라인.md`, `plan/09_화면설계/09_메뉴ID_규칙.md`, `plan/90_결정관리/02_결정기록_부록/b12_ADR-085_087.md`.
- 결정 후보: ADR-085~087, 결정 대기 D-101~119, TODO-89~94. 결정 48(거래처 마스터)은 REQ-63 후보·TODO-93·D-117 로만 존재하고 기능 ID 는 부여하지 않았다(총수 206 유지).
- 서버(odev-9): uidmap 1:4.8.1-2ubuntu2.2 설치(신규 1), pd 계정 SSH 키 로그인(~pd/.ssh 700·authorized_keys 600). INFRA.md(커밋 제외) 갱신. TODO-80 그룹명 cs1 대 사용자 답 pd 는 사용자 확인 대기(D-115)다.
- 판정(otest-1): AC 15개 중 14개 PASS, AC-H1 은 PARTIAL(TODO-80 사용자 확인 대기, 종결 아님). check_ai_usage·check_feature_detail(전수·--wbs·--cite-check)·check_m1_coverage 와 각 self-test rc=0. 시험 중 역라우팅 1회(08c 단서 보강). 빌드·배포 대상이 없어 phase1 은 N/A 로 정직 기록했다.
- 변경 문서: 위 신규 문서와 요구사항 원문, 03_개발범위(01·03·06·07·20b·30·30a·31), 04/22 비밀관리 비교, 05(21·21c·22·22a·23e·23f·25·25a·25b), 06 기능목록(상세 21 부록 a02·a06·a08·a12·a13 포함, csv·xlsx), 07(03·06), 09(02·08·08a·08c·08d·메뉴구조.csv), 90(질문목록·ADR·부록 b0~b11·TODO), `plan/_build` 스크립트 5개, `plan/README.md`·`plan/index.json`·docs-map, `ref/analysis/dify.md`, `tools/README.md`, 이 HISTORY·LESSONS.
- 이월 후보
  - (a) 결정 48 거래처 마스터 상세 설계(법인 공통 관리·사업자등록번호·주기 갱신·개인·프리랜서는 회사별 관리, 다음 사이클).
  - (b) 21·21c 300줄 초과 분할(21 은 329줄, 21c 는 385줄. 기존 초과 문서에 이번에 각각 +1줄).
  - (c) 07 문서 R-PE-1~5 의 §Q1~Q5 깨진 포인터 정리.
  - (d) 일정 미산정(TODO-81·82·94).
  - (e) pd-d9 인계: `plan/09_화면설계/01_화면설계_공통원칙.md` 의 '203건'을 206건으로, '79개·후보 8개'를 81개·후보 10개로 고치고, 메뉴 ID 규칙·06_모바일 터치 포인터를 추가한다.
  - (f) F-PLAN-REF 교착 결함은 AI 프로젝트 세션에 보고한다(역라우팅 DEV 전환이 막힌 사례 재발).
  - 경미: 08c:185 '총 34장 추적 단위 승격' 문구가 후보 CV-28~34 를 포함하므로 표현 점검 권고.

## 2026-11-05 — 프로젝트 평가 3종·투자비용 산정 방식 계획(conv_179385281667, O4): 사전 VRB·사후 재심의·연말 정기 평가, REQ-60·61·ADR-082~084 후보

- 범위는 문서뿐이다. 코드·poc/ui 는 건드리지 않았다. 기능 203건·화면 79개·산출물 전수표 37행은 불변이다.
- 신규 설계: 프로젝트 평가 요구 `plan/03_개발범위/07_프로젝트평가_요구.md`, 사전 VRB·사후 재심의·연말 정기 평가 설계 `plan/05_아키텍처/25_Pd_프로젝트평가_설계.md`·`25a_Pd_프로젝트평가_항목_영향.md`, 투자비용 산정 방식 `plan/05_아키텍처/25b_Pd_투자비용_산정방식.md`.
- 결정 후보: REQ-60·61 후보, ADR-082~084 후보(`plan/90_결정관리/02_결정기록_부록/b11_ADR-082_084.md`), 결정 대기 질문 D-85~100 후보, TODO-82~88. 모두 후보이며 확정이 아니다.
- 구조 변경: `plan/90_결정관리/01_결정대기_질문목록.md` 를 절 경계로 분할해 471줄에서 부록 4개(`plan/90_결정관리/01_결정대기_부록/`)로 옮겼다. 본문 문구는 불변, 포인터 1줄. 분할 뒤 본문은 실측 180줄이다(D-85~100 추가분 포함).
- 기능 부록 반영: a13_EVA_DSH·a06_PRE·a12_FIN_1·a12_FIN_2·a07_PRJ_2·a02_ORG_HRS, 수주전 요구 03, 요구사항 원문, `ref/analysis/commercial_docs.md`(SaaS 대체 사유).
- 판정: AC-001~025 PASS(재시험 포함, AC-017·018 색인 누락 보강 뒤 통과), CANARY-001 은 기대한 FAIL(정상), md_audit·plan_audit rc=0.
- 변경 문서: 위 신규·수정 문서와 ADR·TODO·`plan/README.md`·`plan/index.json`·docs-map, 이 HISTORY·LESSONS.

## 2026-11-05 — msp.docx 근거 차트뷰 보강과 잔여 TODO 처리(conv_179383364007, O4): 08d 보강 문서·ADR-080·081 후보

- 범위는 문서·조사뿐이다. msp.docx 를 근거로 차트뷰 보강 문서 `plan/09_화면설계/08d_차트뷰_의존관계_일정분석_보강.md` 를 새로 만들었고 새 뷰·새 기능·새 화면은 0건이다. 원문 msp.docx 는 `plan/09_화면설계/근거자료/` 에 보관하고 README 에 등재했다. poc/ui 와 코드는 건드리지 않았다.
- 결정 후보: 사용자 결정 대기 U-CV-12~18, 결정 대기 질문 D-78~84, ADR-080·081 후보(`plan/90_결정관리/02_결정기록_부록/b10_ADR-080_081.md`). 모두 후보이며 확정이 아니다.
- 신규 문서: `plan/01_시장조사/09_요금_외부인_공공단가_조사.md`, `plan/04_기술스택/23_Pd_문서변환_라이브러리_후보.md`, `plan/06_기능목록/20c_Pd_상세기능설계_화면총괄.md`.
- TODO 상태(03_향후과제_TODO.md 대조)
  - 처리: 16, 17, 37, 58, 62.
  - 일부 처리: 05, 10, 18, 39, 40, 59, 63, 78, 80.
  - 조사 완료, 사용자 결정 대기: 79.
  - 대기: 67(상태 점검만 했고 상태는 대기), 81(신규 이월).
- 이월: TODO-81 은 08d 반영 후보를 sch 데이터 모델·API·WBS·일정에 옮기는 일이며 U-CV-12~18 답을 받은 뒤에 한다. 미확인으로 남은 것은 네이버 공식 도움말(10), 더존·이카운트 공식 API(39), 다우오피스 일정 API·더존 그룹웨어(40), KIPRIS·UKIPO·USPTO 상표(05), 문서변환 라이선스 원문 대조(63)다.
- 판정: AC-001~018 모두 PASS, CANARY-001 은 기대한 FAIL(정상), md_audit·plan_audit rc=0.
- 변경 문서: 위 신규 문서와 `plan/09_화면설계/08_차트뷰_설계.md`·`08a`·`08b`, `plan/06_기능목록/21_기능요건상세_부록/a08_SCH.md`, `plan/06_기능목록/20_Pd_상세기능설계_공통_권한_화면.md`, 20b·05/20·구성프로파일·rootless 판정, ADR·결정 대기 질문, 사용자 요구사항 원문, `plan/README.md`·`plan/index.json`·docs-map, `ref/analysis/commercial_docs.md`, `tools/README.md`, 이 HISTORY·LESSONS.

## 2026-11-06 — 웹 속도 W1~W3·final6/final7 빌드·같은 컨트롤 판정·Waterfall PoC·Fold5 비교(conv_179371711303, auto, tier 는 앞 사이클과 같은 O4 로 추정)

- 범위는 다섯 가지다. 웹 속도 개선 W1~W3, 전 축 final6 빌드와 Android final7 재빌드, Android 시작 직후 터치의 같은 컨트롤 판정(odev-sametarget), 차트 라이브러리 Waterfall PoC(D-131·TODO-106, CV-20), Fold5 앱 대 웹 final5·final6 비교다. W4(PC 웹·앱 속도 실측)는 측정 중이다.
- 웹 속도(odev-webperf, `work/odev-webperf_result.json`)
  - W1: `poc/ui/src/Pd.Poc.Shell/Docking/ManagedLayerFirst.cs` 의 진단 조건(`#if PD_DIAGNOSTICS`·AppContext 스위치)을 없애 제품 기본으로 바꿨다. `MainView` 가 데스크톱·Android·웹 공통으로 호출한다. 레이어는 자식 ZIndex 를 원래 순번으로 매긴 뒤 첫 자식으로 옮기며, 0 이 아닌 ZIndex 가 있으면 옮기지 않는다. 판정은 W1 PASS·check_layers rc=0 이다. 관리형 창 클릭·겹침·스피너·투어 위아래 실기 회귀는 미실측이다.
  - W2: `Pd.Poc.Browser.csproj` Release 에 `RunAOTCompilation=true` 를 넣었다. Debug 는 AOT 를 하지 않는다. `WasmStripILAfterAOT` 는 넣지 않았다(크기 변화 없음 실측, IL 제거 위험은 추정). final6 웹 제품 빌드 뒤 W2 게이트 PASS(`work/obuild8_gate_W2.out`).
  - W3: `poc/ui/tools/serve_browser.py` 를 새로 만들었다. 기능은 br 협상·`Vary`·지문 파일 `immutable`·그 밖 `no-cache`+304 이고, `serve_browser.sh` 가 이 스크립트를 쓴다. 스모크는 5291 포트에서 확인했다.
- 빌드(obuild-8, `work/obuild-8_result.json`·`work/obuild-8_final7_result.json`)
  - final6 7종 전부 rc=0·오류 0이다. 경고는 웹 8·8, 데스크톱 9·9, Android 8·8·8 이다. 웹 제품 `pcaway_final6_browser_product`(Pd.Ui.Views.0ylx3igswb.wasm a8a75f62…), 웹 진단 `pcaway_final6_browser`(701b6ab5…), 데스크톱 `pcaway_final6`(83343a5c…)·`pcaway_final6_product`(a35fabc1…), Android prod f8ccb811…(채널 prod, check_product_web rc=0)·release 진단 2dfe327d…·debug 36d67c7c… 이다. 빌드 중 소스 변경 0건이다.
  - PC 재기동으로 Android prod 빌드(01:46:42 시작)가 중단됐다. `Pd.Poc.Android/obj/Release/net10.0-android` 에 크기 0 AOT 임시물이 있어 그 폴더만 지우고 05:44 에 다시 빌드했다.
  - final7: odev-sametarget 의 Android 3파일 변경만 반영해 Android 3종을 다시 빌드했다. 전부 rc=0, 경고 8 이다. prod `pcaway_final7_android_release` d852ec79…(채널 prod, check_product_web rc=0), release 진단 df87f536…, debug d82299b9… 이다. 웹·데스크톱은 final6 을 유지한다.
  - 판본표 `plans/pcaway_final_build.json` 은 키 구조를 그대로 두고 값만 바꿨다(백업 `before_webperf`·`before_final7`).
- 같은 컨트롤 판정(odev-sametarget, `work/odev-sametarget_result.json`)
  - 신규 `poc/ui/src/Pd.Poc.Android/StartupHitTest.cs` 에서 `StartupGesture.cs`·`StartupTouchGate.cs` 를 고쳤다. 게이트 전에 받은 터치의 이벤트 좌표 A 와 보정 좌표 B 를 `TopLevel.InputHitTest` 로 올려, 같은 입력 컨트롤(Button·MenuItem 등 11종) 객체면 보내고 다르면 새 사유 `target_differs` 로 버린다. 대상 경로는 window_moved·direct·coord_shift 다.
  - 오프라인 대조: final6 의 버린 23건(coord_shift 17·window_moved 6)은 전부 같은 Button 안이라 보냄으로 바뀐다(추정 — 마지막 좌표·배치 가정). final 원 결함(i=10·18·21 FileMenu 오열림)은 FileMenu 대 설정 Button 으로 갈려 막힌다(추정).
  - 컴파일은 1차 rc=1(Android.Widget 이름 모호·InputHitTest 미해석 3건) 뒤 2차 rc=0 이다. 실기 판정(경계 시행 4종)은 측정 중이다. otest-m-6 이 교대돼 final7 실기 시험은 아직 결과가 없다.
- Waterfall PoC(odev-waterfall, `work/odev-waterfall_result.json`, `poc/chart_waterfall/README.md`)
  - ScottPlot 5.1.59·LiveCharts2 2.1.0-dev-798·OxyPlot 2.2.0 은 조합으로, ProCharts 12.1.0.6 은 네이티브로 CV-20 모양 렌더에 성공했다(헤드리스 PNG, 전부 MIT). TheOne.Avalonia 는 미실측이다.
  - 판단(후보)은 CV-20 을 ADR-091 예외 목록에 넣을 근거가 약하다는 것이다. ProCharts 는 합계 막대·한글 서체 미지원이다. 표는 pd-a4 가 `plan/01_시장조사/10·11` 에 반영했고(`work/merge_note_0900.md`) README 는 이 사이클에서 커밋한다.
- Fold5 앱 대 웹 비교(`poc/ui/logs/webperf/fold5/final6/COMPARE_final5_final6.md`)
  - 웹 final6÷final5 기준이다. 펼침에서 cold 안정 0.70×, menu 0.37×, doc 0.39~0.40×, tab 0.51×, scroll 첫 반응 0.09×, drag 첫 반응 0.17× 로 빨라졌다. 접힘 doc 0.46~0.50× 이다.
  - 남은 격차는 펼침 tab 이다. 웹 504/559ms(중앙/p90) 대 앱 90ms 이고, 웹 warm 안정 4.1~4.5× 이다.
  - 탭 전환 504ms 원인 분석(`work/otest-w-7_fold5_tab_analysis.md`)은 trace 가 없어 확정하지 못했다. 후보는 DOCK 탭 DocumentControl 의 문서 뷰 재부착 비용이다(추정).
- W4(otest-w-7, `work/otest-w-7_result.json`) — 측정 중(blocked)
  - 3회차 웹 제품(06:19~06:26)은 open_grid 중앙 484·open_form 138·open_gantt 108·tab_switch 37ms 다. sdi_grid 중앙 515·p95 929 이며, 앞 4회는 Windows CPU 97% 구간이라 오염이 의심된다. CDP 끌기는 프레임 간격 26.8ms 로 25 이하 기준에 못 미쳤다. 측정 방식 한계로 본다(추정).
  - 사용자 복귀로 3회 중단됐다(05:54·06:25·08:58). 사용자 결정(08:59)은 PC 를 비울 때까지 대기이며, 사용자가 '계속' 할 때만 재개한다. `pcaway_gate.py item W4` 는 rc=1(W4.json 미작성)이다.
- 사고·절차 이탈
  - odev-webperf 가 5199 포트의 다른 python3(pid 1805516)를 자기 서버로 오인해 종료했다. 원인은 첫 줄 주석이 PEP 263 인코딩 선언으로 읽혀 자기 서버가 뜨지 못한 것이다.
  - final6 판본표 갱신 뒤에도 측정이 final6 Android 진단판으로 K7 을 돌렸다(Fold5 설치 base.apk sha 2dfe327d). team-lead 지시로 두 번 종료했다(09:14·09:22, kill -TERM).
  - 팀원 장기 턴 중 메시지 미배달로 otest-m-5·otest-m-6 이 교대됐다.
  - 재기동 뒤 `poc/ui/logs/pcaway/work/screen_probe.ps1` 이 전부 NUL 로 깨진 것을 발견했다. 빌드 입력 576개·final6 산출물 578개는 정상이다.
- 변경 파일: `poc/ui/src/Pd.Poc.Shell/Docking/ManagedLayerFirst.cs`, `poc/ui/src/Pd.Ui.Views/MainView.axaml.cs`, `poc/ui/src/Pd.Poc.Browser/Pd.Poc.Browser.csproj`, `poc/ui/tools/serve_browser.sh`·`serve_browser.py`(신규), `poc/ui/src/Pd.Poc.Android/StartupHitTest.cs`(신규)·`StartupGesture.cs`·`StartupTouchGate.cs`, `poc/chart_waterfall/`(신규), 이 HISTORY·LESSONS. `poc/ui/test/README.md` 도구 색인에 serve_browser.py 등재가 필요하다(odev-webperf 지적, 반영 여부 미확인).
- 이월 후보
  - W4 PC 웹·앱 실측 재개(PC 미사용 때, 사용자 '계속'), W4.json 작성, drag_os·drag_doc3/doc5·앱 비교 측정.
  - final7 실기 판정: 같은 컨트롤 경계 시행 4종((250,160)·(250,165) 차단, (265,215) 통과, (265,189) 원 결함 대조)과 23건 보냄 전환 확인.
  - Fold5 DOCK 탭 전환 504ms: CDP trace 1회 수집, PC 웹 DOCK 모드 대조, 비활성 문서 뷰 숨김 유지 후보. SDI 그리드 첫 회 느림(TimerFire 긴 작업)도 있다. 성능 개선은 PC 미사용 때 한다.
  - W1 실기 회귀(관리형 창 클릭·겹침·스피너·투어), Waterfall 의 Avalonia 컨트롤·WASM·Android 렌더와 음수 구간·TheOne.Avalonia 실측.
  - screen_probe.ps1 복구, GitLab 정본으로의 병합. 병합은 fast-forward 8커밋이고, 겹치는 파일 `plan/07_구축_배포/05_구성프로파일_최소사양.md` 1개를 먼저 커밋해야 한다.

## 2026-11-05 — UI 개선 요건 2~8 + 결함 D-1~D-17(conv_179371711303, O4, auto): 창·패널·모니터·투어·설정·계층 분리

- 범위는 요건 2~8(창 배치, 패널 규칙, 모니터 처리, 투어, 설정, 계층 분리)과 진행 중 발견된 D-1~D-17이다. D-9~D-16은 사용자가 시험 중 알린 항목이다. D-9 뷰 모드 자동 전환, D-10 다중 모니터 최대화, D-11 MDI 캔버스 밖 이동, D-12 창 제목 줄 우클릭 창 메뉴, D-13 패널 확장 버튼 근접 표시, D-14 알림 제목 건수·위치 메뉴, D-15 도움말 투어 데이터 주도화(tour_catalog.json), D-16 모든 패널을 일반 패널로 끌어 이동하게 하는 변경, D-17 U15 SDI 합류·분할·웹 스피너 숨김·패널 복구·패널 전용 창 닫힘이다.
- 판본은 otest_final3*(Work Shell 78314060, 제품 Shell e5ad9cf4, Web 86c44066·fcc4d58b, APK 4459c8be·75921dd7·07f53e2e)이고 3차 재시험까지 수행했다.
- 판정
  - 데스크톱은 53개 AC 가운데 gate_all 52개가 PASS 이고 AC-6-10 은 목록 밖 단독 PASS 다. 모바일 AC-3-10·6-08·7-06, 웹 AC-2-09·4-05·6-09 가 PASS 다. 사용자 파일과 사용자 Pd(PID 34288)는 불변이다.
  - 조건부 PASS 는 AC-8-02, AC-6-10, AC-5-06, AC-2-06(지연)이다. D21 규칙 불일치가 남아 있다.
  - 판정기·도구 변경은 AC-3-06 경로, run_release 첫 프레임 준비, ui28_web help 이동, 패널 기대값(retest_window_menu_focus·panel_position_checks·retest_android_panel), U15 도구(거짓 PASS 수정)이다.
  - 미측정·보류는 실모니터 최대화 묶음(retest_maximize_monitor·spawn), 제목 줄 popup·nc 단계, measure_hidden_main, 실제 F1, Desktop 제품판 기동, 창 메뉴 HOLD 항목이다. 모두 사용자 PC 비움 시점이 필요하다.
- 사고(최종 보고 대상 3건)
  - 사고 1: odev-fix-viewmode 시험 기동(11-04 16:04:53)이 사용자 Pd(PID 34288)에 단일 인스턴스 활성화 신호를 보냈다. 사용자 파일 md5 는 불변이었고 logs/user_files/pending 이 덮였다.
  - 사고 2: odev-fix-viewmode 시험 창(PID 23596)이 측정 중 화면 밖에서 가운데 모니터로 노출됐다(최대 약 2분, 전경 미탈취, 종료 미확인).
  - 사고 3: odev-fix-maximize 가 lead 승인 없이 실모니터 측정을 했다(16:35:37~16:38:14, PID 37816, PD_ALLOW_ONSCREEN 자체 주입). onscreen_runs.log 사유가 허위로 적혀 정정 줄을 추가하게 했다.
- 절차 이탈: odev-fix-mdidrag 가 가드 없이 측정한 회차가 9회다(171610 이전). 시스템 부하가 포화한 구간이 2회 있었다(load 최대 약 32). 파이썬 쓰기·heredoc·앱 겹침·작업트리 저장으로 공용 빌드 차단·사본 마커 위반이 있었다.
- 역라우팅: 11-04 TEST→DEV(odev-fix-late)와 재시험 3차까지 거쳤다.
- 변경 문서: `plan/04_기술스택/02_소스구조_분할규칙.md`, `plan/09_화면설계/01_화면설계_공통원칙.md`·`03_데스크톱_화면설계.md`·`04_웹_화면설계.md`·`06_모바일_화면설계.md`, 판정보고서 부록 a5·a7·a8, `poc/ui/README.md`, `poc/ui/test/README.md`(재사용 시험 도구 등재), `poc/ui/LAYERS.md`(생성물), 이 HISTORY·LESSONS. 원본 폴더 INFRA.md 45·51행은 정정했고 커밋하지 않는다.
- 이월 후보
  - Android BACK(Fold5 재현, 에뮬 태블릿은 정상), 스플래시 터치 유실·첫 프레임 지연, 실제 알림 안내, `--notify-layout legacy`, win_owner_z 의 숨은 창 오인, 문서 탭 메뉴, 태블릿 탭 단위 이동, MDI 왼쪽 55% 잔존, 태블릿 m2probe 하위 메뉴 사각형, Windows 터치 노트북, 제품판 로그 숨김, 복원 프로필 busy 지연.
  - 사람 확인 H-2-1·2-2·3-1·4-1·5-1·6-1·M1·W1·S1 과 실제 우클릭·마우스 끌기·안내·Alt+Space·제목 줄 버튼·F1, 분할선 체감, 독립 창 밀림 없음, 태블릿 ▾, 스피너 창 밖, 다른 창으로 탭 끌기, 패널 전용 창 최소·최대화.
  - 사용자 결정 대기: "1" 의미, 요건 1번, 2차 묶음, 전 주기 산출물 후보 확정, Fold5 접기.
- 공개 사항: 추적 중인 `*.discarded` 등 36건은 이번에 건드리지 않았다. 다른 세션(pd-57)의 차트 뷰 인계 반영은 같은 사이클 마감에서 rebase 뒤에 했다.

## 2026-11-05 — P89 결정 26건 정본 반영(conv_179379345645·conv_179379376269, O4): ADR-071~075 확정 기록·ADR-076~079 후보·차트뷰 설계·AI 활용 전수·서버 설정 3경로

- P89 사용자 답 26건을 정본에 반영했다(ADR-071~ADR-075 확정 기록, ADR-067~070 개정). 범위는 문서·도구이며 UI·서버 코드 변경은 없고 poc/ui 는 건드리지 않았다.
- 결과 수치(실측): 기능 203건, 릴리스 M1 59·M2 60·M3 29·R2 46·R3 6(`기능목록.csv`). 화면 79개 중 후보 8개(DOC 5·신규 3). 일정표 재산정은 후보 행 포함 278~462.5일·제외 248~408일이며 전부 추정이다.
- 신규 문서: `plan/03_개발범위/06_AI_활용_전수_요구.md`, `plan/04_기술스택/22_Pd_비밀관리_자체개발_대_OpenBao_비교.md`, `plan/05_아키텍처/23e_Pd_오케스트레이터_분석사이클_REA.md`, `plan/05_아키텍처/23f_Pd_상태규칙_소유권_O8_비교.md`, `plan/05_아키텍처/24_Pd_서버설정_3경로_설계.md`, `plan/09_화면설계/08_차트뷰_설계.md`·`08a`·`08b`·`08c`, `plan/09_화면설계/근거자료/`(사용자 지시로 보고서 원본 보관), `plan/90_결정관리/02_결정기록_부록/b8_ADR-071_075.md`·`b9_ADR-076_079.md`, `ref/analysis/dify.md`·`rea_artifacts_b.md`.
- 도구: `tools/ref_rea_policy.py`·`plan/_build/check_ai_usage.py` 신규, `tools/fetch_ref.py` 임계값 외부화(`delete_when_free_below_gb`)·상용 REA 의무 audit·`.ee.` 제외 강화.
- 후보로 남긴 것: O4(비밀 관리, ADR-062 본문 불변)·O8(이슈 상태 규칙, ADR-005 본문 불변)·오픈·운영 구성(오픈 준비 때 질문)·U-CV-1~11(D-67~D-77, ADR-076~079). 사용자 답을 대리 확정하지 않았다.
- 상용 10종 재판정은 약관 금지 7·확인 불가 2·배포물 없음 1이며 상용 REA 실행은 0건이다. Hatchet 은 rc=0, n8n 은 부분 분석(`.ee.`·소스맵 묶음 제외, `analyze-web-bundle` 미사용), Dify 는 클라이언트 difyctl 만 분석했고 서버 엔진은 미수행이다. REA MCP 가 끊겨 CLI 로 실행했다.
- 시험 판정(otest-1, conv_179379376269): 게이트 run3 에서 A1~A22·A24·A26~A28 PASS, A23·A25 는 커밋 뒤 유효 항목으로 deferred 했다. run1·run2 는 A21 이 판정기 4초 예산을 넘겨 UNDET 였고 실행을 합쳐 통과로 치지 않았다. 표본 대조 중 찾은 결함(`20_Pd_상세기능설계_공통_권한_화면.md` L348 옛 후보 문구)은 odev-34 가 고쳤다. UI 시험은 대상이 없어 해당 없음으로 기록했다.
- 공개할 사실: `phase_batches.json` F9 불변 가드를 oplan_review-1 이 file_copy 로 통과해 덮어썼고, 내용이 사용자 차트뷰 요구라 판을 유지했다. odev-12 가 prune --apply 대신 dir_delete 로 2개 폴더를 지웠다. done_gate 가 A28 시간 초과(5초)로 차단한 기록(00:30:03)이 있고 그 뒤 재판정으로 DONE 에 들어왔다.
- 마감 수정(odone-1): `plan/05_아키텍처/07_캐시서버_부록/a2_7_8_1대단계_열린질문.md` 표 1행의 옛 '무root' 문구를 ADR-043 개정으로 고쳤다. 시험 뒤에 바꾼 줄이며 커밋 뒤 전수 판정으로 다시 확인한다. TODO-60·64 를 실제 결과로 갱신하고 TODO-79(C 여유 불변 원인)·TODO-80(pd 기본 그룹 root 영향)을 등재했다.
- 마감 재실행(odone-1, 00:38 KST, a2·TODO 수정 뒤): `md_audit` rc=0(수집 3·등재 69·고아 0·깨진 참조 0), `plan_audit` rc=0 errors=0 warnings=32 이다.
- 인계·이월: `plan/09_화면설계/01_화면설계_공통원칙.md` 144행 옛 값(195·71)은 pd-d9 소관이라 고치지 않고 인계한다. uidmap 설치 요청은 사용자 몫이다(TODO-70). 문서 300줄 초과 분할은 TODO-62, 차트뷰 성능은 TODO-77 이다. 결정 대조는 키워드 grep 과 해당 줄 읽기 기준이라 다른 표현으로 남은 곳이 있을 수 있다.

## 2026-11-04 — 오케스트레이터 사이클 마감 Batch 2(conv_179377456593, O4): 전 주기 산출물 100% 내부 소화 계획 반영·OIDC 잔여·rootless 판정

- 범위는 정본 문서 계획 반영이며 코드는 없다. 사용자 원문 4요구(RFP 입력→기술·솔루션·공수 산출→프로젝트 생성, 에디터 산출물 생성·수정, Excel·Word 변환·역변환, 전 단계 산출물 내부 소화)를 신규 모듈 후보 DOC(DOC-01~08)로 받았다. 범위 확대·ADR-071 은 **후보이며 사용자 확인 대기**이다(제안-P89-D1~D4).
- 해석 고정: "100% 소화"는 전수표(37행)에 적은 산출물이 구조화 문서 또는 원본+메타로 관리되는 것이다. 임의 서식 왕복은 원본 보관 + 변환 손실 보고로 쓰고 RFP 공수는 추정이며 PM 확인 전 확정이 아니다.
- 변경 문서(odev 결과 요약 기준)
  - 요구·범위: `plan/03_개발범위/05_전주기_산출물_요구.md`(신규, REQ-50~54), `plan/00_요구사항/01_사용자요구사항_원문.md`, `20a` 매트릭스·`30`·`30a` WBS 후보 행.
  - 기능목록 정본: `plan/_build/features_data.py`·`features.py`, `기능목록.csv`·`.xlsx`, `01_상세기능목록.md`·`01b` — 기능 195→203건(DOC 8건 추가, DOC-01~05·08 후보 M2, DOC-06·07 후보 M3). IAM-10 에 외부 계정(구글·네이버·카카오 OIDC) 문구를 사용자 확인 대기 표기와 함께 반영했다.
  - 설계·부록: `plan/06_기능목록/21_기능요건상세_부록/a18_DOC.md`(신규), 색인 `21_Pd_기능요건상세_공통_색인.md`, `plan/_build/check_feature_detail.py`, `20`(SCR-DOC-01~05 후보·SCR-IAM-01 외부 계정 노트)·`20b`.
  - 화면: `plan/09_화면설계/07_산출물_에디터_화면설계.md`(신규), `02_메뉴구조.md`·`메뉴구조.csv`(menu_tree.py 재생성, 화면 76개). 다른 세션이 수정 중인 `09/01·03·04·06`, `plan/04_기술스택/02_소스구조_분할규칙.md`, `poc/ui` 는 건드리지 않았다.
  - 결정: `01_결정대기_질문목록.md`(제안-P89-D1~D4·R1~R6), `02_결정기록_ADR.md` 확정 전 후보 절, `03_향후과제_TODO.md`(TODO-60~64).
- OIDC(8669c18) 잔여 4건 처리: ① 기능목록 정본 3종 IAM-10 문구를 반영했다. ② 이 HISTORY 에 항목을 보강했다. ③ 외부 계정 로그인 ADR 확정 여부는 제안-P89-D3 으로 등재했고 대기이다. ④ SCR-IAM-01 외부 계정 노트를 `20` 에 반영했다. 구글·네이버·카카오 공급자 사양의 사실 정확성은 조사하지 않았다. 세션 표기 pd-69·pd-83 불일치는 미확인으로 남는다.
- rootless 운영 판정(사용자 추가 요청): `plan/07_구축_배포/06_Pd_rootless_운영_판정.md`(277줄)에 구성요소 판정, 프로파일별 운영 계획, 1회성/상시 root 작업, 단일 계정 위험, 검증 계획을 정리했다. 설치 시험은 없고 Gitea 만 실측했으며 조회한 서버는 공유 서버 1대뿐이다(06 §11). 결정 6건은 제안-P89-R1~R6 으로 등재했고 전부 대기이다. ADR-043 등 기존 결정은 바꾸지 않았다. 옛 설치 원칙(`INFRA.md` §10, `07_구축_배포/02`·`03`, `05_아키텍처/07_캐시서버_부록/a2_7_8_1대단계_열린질문.md`)의 일괄 정리는 R1 결정 뒤로 TODO-64 에 이월했다.
- 검증(odev-19, 2026-11-04 마감 시점): `plan_audit` rc=0 errors=0 warnings=32(GENERATED 2·IPV4 22·LONG 8, LIMIT 경고 없음, ORPHAN 0), `md_audit` rc=0(수집 3·등재 56·고아 0·깨진 참조 0), `check_feature_detail.py` 기본·`--cite-check`·`--wbs`(203건 전수 매핑)·`--self-test` 전부 rc=0, `fetch_ref.py audit` OK 항목 39·위반 0·경고 0. 판정은 otest·odone 단계 몫이며 이 기록이 대신 PASS 를 올리지 않는다.
- 시험 판정(otest-2, 2026-11-04): 변경 25개·미추적 4개 대상. AC-12·13·15~19·28·29 PASS, AC-14 조건부(`git add -A` 미사용은 커밋 단계에서 경로 지정 add 로 확인), AC-20·22 UNDET(메인 몫), AC-21 PARTIAL(HISTORY Batch 1 OIDC 줄 1건 교체는 메인 지시에 따른 의도적 변경), AC-27 값 PASS(문구는 기준보다 긴 확장형). Batch 1 의 AC-07 PARTIAL·AC-26 FAIL 은 그대로이다. UI·서버는 N/A 이다.
- 사전 점검(odone-2): `06_Pd_rootless_운영_판정.md` 82행의 10143 은 INFRA.md 에 없는 값이며 Dovecot 공식 안내의 고포트 예시이므로 그대로 둔다.
- 이월 후보
  - 건수 195 문구 갱신(TODO-60 확장): DOC 후보 확정 뒤 옛 건수 인용을 갱신하고, 확정되지 않으면 195 를 유지한다. 대상 줄은 TODO-60 에 있다.
  - LONG 8건 중 `01_결정대기_질문목록.md`(389줄)·`20_공통_권한_화면`(353줄)은 이월이다(분할 TODO-62). 일정표 재산정(TODO-61), 변환 라이브러리 선정(TODO-63), 옛 설치 원칙 정리(TODO-64).
  - Batch 1 이월(n8n 번들 분석 SUL 해석, windmill REA 상한, REA MCP 끊김 원인, AC-07 카드 3건, 성능 측정)은 그대로 남는다.
  - plan_audit 경고 수가 사이클 중 27·32·35·36·32 로 달랐고 원인은 미확인이다(이번 실행은 LIMIT 경고가 없었다).

## 2026-11-02 — 사용자 요청 UI 개선 1차 묶음(conv_179357811451, O4): 용어·owner 해제·AUMID·자동 칸·글자 흐림

- 범위는 사용자 요청 9건 중 1차 묶음(1 용어, 2 SDI owner 해제와 '항상 메인 창 위', 5 작업표시줄 AUMID, 6 [자동] 칸, 8 글자 흐림)과 시험에서 나온 결함 수정이다. 사용자 지시(2026-11-02 11:27)로 1차 묶음만 처리하고 멈췄다. next_action 은 만들지 않았다.
- 판정
  - 통과: AC-01·03·04·05·06·07·12·13(로그 기준)·14·15·17·18(데스크톱 수치, 웹은 스크린샷 판독)·19·23·24·25, 웹 칸 숨김·굵기 복원. 데스크톱 재시험(c8final2)과 웹 재시험 모두 PASS 로 evidence/otest_done 이 기록됐다.
  - 미판정·한계: 실제 마우스 클릭, 제품판 트레이 복원, 그림자 화소, AC-M1~M6 사람 확인, Segoe UI 강조 미해결, 밝은 테마·다른 DPI·MobileNavView 미측정, 라틴 전용 폰트의 한글 대체 서체 합성.
- 역라우팅 1회: SdiAboveMainHook 이 설정 변경 때 메인 Z 를 다시 요청하게 고쳤고, 진단 명령 window.main.activate 의 Z 올리기는 진단판 전용으로 했다. 웹은 chkSdiAboveMain 을 숨기고(ShellPlatform.IsSdiWindowSupported) RealWeight 강등을 적용하지 않는다.
- 변경 코드: Pd.Poc.Desktop 의 AppIdentity.cs(AUMID `Projecd.Pd`, 신규)·SdiAboveMainHook.cs(신규)·Program.cs·BgWindowPolicy.cs·SdiBgPinHook.cs·SdiWindowHost.cs, Pd.Poc.Shell 의 RemoteTextRenderCommands.cs(신규)·설정(UiSettings·UiWindowSettings·UiSettingsDto·UiSettingsApplier·UiStyleViewModel)·ShellPlatform.cs·MainTitleBar.axaml·ShellSettingsPanel.axaml·지역화 JSON 2개·글꼴 굵기 처리 axaml 여러 개. 시험 자산은 `poc/ui/test/desktop/measure/win_owner_z.ps1`·`text_sharpness.py` 를 신설하고 `poc/ui/test/README.md` 색인에 등재했다.
- 변경 문서: `plan/05_아키텍처/08_지원_폼팩터_용어.md` §8-1(메뉴 줄·제목 줄·제목 탭·문서 탭, 금지 용어 '탭 띠'), `plan/09_화면설계/01_화면설계_공통원칙.md`·`03_데스크톱_화면설계.md`·`04_웹_화면설계.md`(W-17·W-18). 08 문서는 300줄에 정확히 닿아 있어 더 늘리지 않았다. md_audit rc=0, plan_audit rc=0(error 0, 기존 경고 31).
- 이월 후보
  - 2차 묶음 4건은 미착수다. 3 SDI 우측 아이콘, 4 제목 탭 너비(규칙 초안은 계획서 §4), 7 제목 줄 높이, 9 태블릿형 중복 탭이다. 사용자가 말하면 진행한다.
  - 사용자가 새로 알린 결함 '제목 줄에서 SDI 아이콘을 눌러도 SDI 창 제목 줄이 활성 색으로 바뀌지 않음'(oi_queue)은 미착수다.
  - 기존 현상(회귀 아님): `--bg-pos` 화면 안 기동 제품판에서 SdiBgPinHook 재고정으로 SDI 가 계단식으로 이동한다. 이전 판(611e1f9)에서도 같다. 원인 미확인.
  - 시험 도구 Process.CloseMainWindow 가 owner 해제 뒤 맨 위 SDI 를 잡는 도구 경로 문제, 웹용 Bold 서체 번들 보류.
  - AC-M1~M6 은 사람 확인이 필요하다.
- 증거(`S/` = `/tmp/cc-5aaefbf7-d8aa-4e92-a10e-af04f293090e/session-env/5a01cad7-2b06-412f-a040-38c16799d4ad/`): 계획서 `S/plans/oplan_conv_179357811451.md`, 시험 보고 `poc/ui/logs/otest_c8_b1_r1/report.md` 외(logs 는 git 제외).
- 공개 사항: 같은 작업트리에 다른 세션(pd-83)의 외부 계정 로그인 문서 변경(21c·IAM-10 등)이 미커밋으로 섞여 있어 이번 커밋에서 제외했다. 세션 `work/odone-1_result.json` 은 옛 사이클(cycle 7) 잔류물이라 무시했다.

## 2026-11-02 — oto 사이클7(conv_179349873953, O3, 체인 마지막 사이클): C3 창 외곽선 수정·재판정

- 범위는 c6 에서 이월된 M1·M6·C3 다. 이번이 체인 사이클 상한(cycle_cap 5)에 해당하는 마지막 사이클이다.
- 판정
  - C3 는 **PASS** 다(gate3/C3.json, by=otest-4, c3_gate rc=0). 최종 빌드는 c7fix2(수정안 B1)다. 반경은 활성 7.95·7.95, 비활성 8.75·8.75(기준 7.0~9.0, valid)이고, 외곽선은 1·1·1(기준 1px)이다. box 는 rule_fix 원래 정의 47,40,548,341 이다.
  - M1·M6 은 **UNDET** 이다. write_guard F10-3 이 그대로라(mtime 2026-10-31 21:34:50) 키 주입 경로가 없다. 사람이 직접 확인하는 절차는 세션 `plans/c4_manual_M1_M6.md` 에 있다.
  - **M1·M6 oto_incomplete(cycle_cap)** 로 보고한다. 체인 상한에 닿아 다음 사이클로 자동 이월하지 않는다.
- 경과
  - 회차 3 은 05:03 에 화면 잠금이 풀린 뒤 측정했다. 반경은 통과했지만 외곽선이 2px(좌·우·아래 2·2·2)라 FAIL 이었다.
  - 수정안 A(DWMWA_COLOR_NONE, `SdiBorderNative.cs` 신설)는 외곽선을 1 로 줄였지만 비활성 하단 반경이 9.45 로 FAIL 이었다. 심사자는 frame 가장자리 화소가 바탕색으로 바뀌어 생긴 피팅 아티팩트로 봤고, 판정 값은 바꾸지 않았다.
  - rule_fix2(1px 안쪽 box)로 재니 반경이 3.8/4.75 로 FAIL 이었다. 앱 외곽선이 1px 안쪽 직선이라 모서리에서 DWM 클립(r≈8)에 잘려 끊긴 것이 원인이었다.
  - 수정안 B1 은 A 를 되돌리고, Windows 스타일 SDI 에서 Dock DocumentControl 의 `PART_Border` 두께를 0 으로 했다. 원인은 DWM 비클라이언트 1px 와 Dock `PART_Border` 1px(PdBrushBorder #43454A)의 겹침이었다.
- 변경 파일
  - `poc/ui/src/Pd.Poc.Shell/Views/SdiTitleBar.axaml.cs` +2줄. OnActiveChanged 에서 틀에 `.pdWinChrome`(EffectiveStyle==Windows)을 붙이고, 스타일 전환(OnCaptionApplied) 때 다시 호출한다.
  - `poc/ui/src/Pd.Poc.Shell/Themes/MdiChrome.axaml` +2줄. `Border.pdSdiFrame.pdWinChrome DocumentControl /template/ Border#PART_Border` 의 BorderThickness=0 이다.
  - `plan/04_기술스택/05_창크롬_OS디자인_기준.md` §2.8.1 의 COLOR_NONE 줄을 사실대로 1줄 교체했다.
  - `poc/ui/src/Pd.Poc.Desktop/SdiBorderNative.cs` 는 수정안 A 때 만들었다가 B1 에서 삭제했다. `poc/ui/src/Pd.Poc.Desktop/SdiWindowHost.cs` 는 2줄을 원복해 원래와 같다(189줄).
  - 빌드는 `PD_OUT_NAME=c7fix2 build_desktop.sh --bg Work` 로 했고 rc=0, 오류 0, 경고 5(기존)이다.
- 규칙 이력은 rule.md → rule_fix(05:13) → rule_fix2(05:23) → rule_fix3(05:40) 순이다. 모두 해당 캡처 전에 고정했고 기준값(반경 7.0~9.0, 외곽선 1px)은 바꾸지 않았다. 최종 판정은 원래 정의(rule_fix box)로 했다. 규칙을 바꿔 가며 다시 잰 것은 외곽선 출처를 바꾸는 수정이 들어가 대상 빌드·적용 확인 방식이 달라졌기 때문이다. 이 사실을 공개 사항으로 남긴다.
- 화소 확인 1화소(y=144)는 x=48…547 전 폭이 같은 색인 본문 가로선으로 확인돼 심사자가 판정 자료로 채택했다. 남은 frame 가장자리 1px 는 DWM 기본 테두리로 추정하며 확인하지 않았다.
- 증거(`S/` = `/home/rioky/.claude/session-env/5a01cad7-2b06-412f-a040-38c16799d4ad/`): 판정 로그 `S/logs/otest_conv_179349873953_c7.md`, 변경 기록 `S/logs/odev_c7fix_conv_179349873953.md`, 결정 기록 `S/auto_decisions.jsonl`, 항목별 판정 `S/gate3/*.json`.
- 공개 사항
  - 메인이 auto_decisions 일부 ts 를 실측하지 않고 적었다.
  - shadow_q16 의 frame_stable 은 active 와 inactive 만 비교한다. before 와의 이동은 감지하지 못한다.
  - odev-c3f 가 임시 파일 `poc/ui/logs/odev-c3/c7fix2_r5/enum.ps1` 을 logs 폴더에 남겼다.
  - plan_audit 가 b2_ADR-041 의 '12.1.0.12'(Dock 버전)를 IP 로 오탐했다.

## 2026-11-01 — oto 사이클5(conv_179349873953, goal G-C5B2E19A, O3): pd-83 P87 등재 요청 반영

- 범위는 pd-83(P87 결정 반영 세션)이 pd-69 소유 문서에 보낸 등재 요청서(세션 7be609e4 `plans/pd69_P87_registration_request.md` §A~§G, history·lessons 요청안 포함)를 문서와 대조해 반영하거나 미반영 사유를 남기는 것이다. M1·M6 은 가드를 우회하지 않는 판정 수단이 있는지 다시 확인했다. 코드·DB·UI 변경은 없다.
- M1·M6 은 **UNDET 유지(측정 한계)**다. Aero Snap 을 키 입력 없이 일으키는 공개 Win32 API 와 Alt+Tab 목록을 노출하는 공개 열거 API 를 확인하지 못했다(계획서 §3, 출처 AutoIt 포럼 topic 175804·forrestthewoods 'Building a Better Aero Snap'). UIA WindowPattern 은 OS 스냅 경로가 아니어서 c4 간접 증거와 같은 등급이다. 사람용 절차는 세션 `plans/c4_manual_M1_M6.md` 그대로다.
- 반영표(요청 항목 → 반영 위치 또는 미반영 사유)

| 요청 | 처리 | 위치·사유 |
|---|---|---|
| §A D-41·45·50·54·48·49·12·08·53 | 반영 | `plan/90_결정관리/01_결정대기_질문목록.md` D 행 9개에 결정(P87)(2026-11-01), `plan/index.json` decisions 같은 9항목. D-41 은 `05_아키텍처/05_제품구성_시스템메뉴.md` §1.2 에도 1줄 |
| §B ADR-014·015·016·036 | 반영 | `02_결정기록_ADR.md` 개정 이력(odev-2 합계: ADR 개정 15건·신규 9건 ADR-058~066) |
| §B ADR-055 / 050·049·047·045·044·043 / 040 / 025·028 / 019 | 반영 | 부록 b0 / b1(050 은 오프라인 미지원 종결+①~⑦, 043 은 P84 정합 확인만) / b2 / b3 / b4 |
| §B 신규 8건 | 반영 | ADR-058~065, 신규 부록 `02_결정기록_부록/b6_ADR-066_058.md`, `plan/README.md` 부록 행 b0~b6 |
| §B ADR-006 개정 | 반영 위치 변경 | 번호 불일치(실제 ADR-006 은 CI/CD·K8s). 오픈소스 선정 기준은 신규 ADR-066 으로 등재, ADR-006 은 수정 안 함 |
| §B ADR-035 | 반영 위치 변경 | 번호 불일치(실제 ADR-035 는 플랫폼 확장·다중 통화). Jira 읽기 전용 R2 는 ADR-014 개정 이력에 흡수, ADR-035 수정 안 함 |
| §C plt 헤더·공통규약 §2·§4·§9·§14·신규 절 | 반영 | `10_API설계/plt.md` §3 `X-Pd-Client-Version`·`core.client_outdated`, `10_API설계/00_공통규약.md` §2·§4·§9·§14(`__Host-Http-pd_s`·`X-Pd-Csrf`)·§15 전역 운영 규약(P87) 신설. 자체 발견으로 plt §2·공통규약 §6 의 '오프라인' 표기도 정정 |
| D-C5-1 `core.client_outdated` 상태 코드 | 정합 | 403(잠정)으로 정합, 확정 시 §14 만 변경, 확정값이 403 이 아니면 a05 72·82행 갱신을 pd-83 에 통지. pd-83 이 21a 48행을 403(잠정, 정본 plan/10)으로 고침 |
| §C 설정 키 이름 정본 | 미반영 | 키 전수 목록(TODO-21)이 6항목뿐이라 확정이 이르다. TODO-49, §15 에 '잠정' 표기 |
| §D 08 데이터모델 6건(표준 View·AI 허용·계산식·후보 테이블·AI 변경 저장·CapEx) | 미반영 | 08 모델 미설계 모듈(FIN·AIP 등, `fin.md` 없음)이고 계산식은 미정(결정 13). TODO-45 로 묶어 TODO-32 에 연결 |
| §E 07 캐시서버 | 반영 | `05_아키텍처/07_캐시서버_아키텍처.md` 112행 교체·0절 C11·§9.3·9.4·9.6·9.7, §9.8 '결정(P87) 종결' |
| §F TODO-12·신규 ①~⑧ | 반영 | `90_결정관리/03_향후과제_TODO.md` TODO-12 갱신, ①은 TODO-34 에 덧붙임(중복 방지), ②~⑧ = TODO-37~43. 추가로 TODO-44~49 신설, TODO-24·25·26·27·30 완료(P87) |
| §G csv-정정 IAM-03·IAM-10·PLT-02 설명 | 반영 | `_build/features_data.py` 설명 정정 후 재생성(기능목록 csv·xlsx·01_상세기능목록·메뉴구조 csv) |
| §G csv-ISS(ISS-17·18 M1→R2)·PLT-02 기능명 | 반영(D-C5-2, pd-83 부록 선행 정정) | 처음 적용 때 check_feature_detail rc=1(a09_ISS_2 95·115행 F3·F4, a05_PLT 48행 F1)이라 해당 부분만 원복하고 정정을 요청했다. pd-83 이 a09·a05 를 고친 뒤 `features_data.py` 에 ISS-17·18 `R2`, PLT-02 기능명 `로컬 캐시(모바일, 속도 전용)` 을 다시 적용하고 재생성했다. 건수 M1 59·R2 46. 재적용 직후 남았던 a09_ISS_2 95·115행 F3(`- 의존·선행:` 라벨) 2건도 pd-83 이 정정해 check_feature_detail rc=0 |
| §G PRJ-11 → M3 | 미반영(근거 충돌) | 결정 18 은 'PRJ-11 확장'을 M3 에 편입, 본체는 M2(30a 58행·features.py STAGE_OVERRIDE, WBS-M2-12) |
| §G csv-CapEx·csv-MCP 신규 등재 | 미반영 | 기능 ID·화면 미정, 195건 전수 검사·화면·부록·WBS 로 파급. TODO-44 |
| §G 09 DLQ 화면 | 미반영 | 화면 ID 는 menu_tree.py 가 기능 ID 에서 생성하는데 DLQ 재처리 기능 ID 가 없다. TODO-46 |
| §G 09 연결끊김 | 반영 | `09_화면설계/01_화면설계_공통원칙.md` §8 규칙 3줄 |
| §G Webhook DLQ·11 이벤트 | 미반영 | Webhook DLQ 적용은 결정 필요, 이벤트 이름·페이로드는 outbox 정본 설계 선행. TODO-47 |
| §G 시험계획 ID(`AC-<기능ID>-n`) | 미반영 | plan/ 에 시험계획 정본 문서가 아직 없다. TODO-48 |
| §G WBS-01 | 반영 | csv 의 ISS-17·18 릴리스는 위 D-C5-2 로 반영. 20b 는 pd-83 이 정정(소유 밖). index.json documents.features.stages 건수 갱신(D-OT-1, M1 61→59·R2 44→46) |
| §G ADR-014 지표 | 해당 없음 | 요청서 스스로 '없음' |
| history·lessons 요청안 | 반영 | 아래 pd-83 항목, `LESSONS.md` 사이클5 |

- 변경 파일: odev-1 `plan/10_API설계/plt.md`·`00_공통규약.md`·`plan/05_아키텍처/07_캐시서버_아키텍처.md`·`05_제품구성_시스템메뉴.md`·`plan/09_화면설계/01_화면설계_공통원칙.md`, odev-2 `plan/90_결정관리/02_결정기록_ADR.md`·부록 b0·b1·b2·b3·b4·b6(신규)·`plan/README.md`, odev-3 `plan/90_결정관리/01_결정대기_질문목록.md`(D 행만)·`03_향후과제_TODO.md`·`plan/index.json`·`plan/_build/features_data.py`와 생성물(`06_기능목록/01_상세기능목록.md`·`기능목록.csv`·`기능목록.xlsx`·`09_화면설계/메뉴구조.csv`)·`HISTORY.md`·`LESSONS.md`. odev-4 가 `plan/index.json` documents.features.stages 건수와 이 항목을 고쳤다.
- pd-83 이 직접 고친 문서(pd-83 회신 메시지로 확인, 우리 세션은 수정하지 않음): `plan/05_아키텍처/21a_Pd_연계명세_클라이언트_실시간_서버군.md` 48행(403 `core.client_outdated` 잠정·정본 포인터), `plan/06_기능목록/21_기능요건상세_부록/a05_PLT.md` 48행(PLT-02 기능명), `plan/06_기능목록/20a_Pd_상세기능설계_모듈A_기반_실행.md` 83행(같은 기능명), `plan/06_기능목록/21_기능요건상세_부록/a09_ISS_2.md` 95·115행(R2 와 '의존·선행' 라벨).
- 검증: P1~P4 auto_script rc=0, check_feature_detail·`--wbs`·check_m1_coverage rc=0, md_audit rc=0, plan_audit rc=0(errors 0·warnings 31 기준선 동일). M1·M6 은 UNDET 로 rc=1 유지.
- 최종 판정(검증 루프 5회차, otest-2 로그 세션 `logs/otest_conv_179349873953_c5_r1.md`): **P1~P4 PASS, M1·M6·C3 UNDET.**
  - M1·M6 UNDET 사유: 키 주입은 write_guard F10-3 이 막고, 키 입력 없이 스냅·Alt+Tab 목록을 다루는 공개 API 는 없다. 사람용 절차는 세션 `plans/c4_manual_M1_M6.md` 다.
  - C3 UNDET 사유: Windows 화면이 23:36~01:34 동안 계속 잠겨 있었다. 회차 1~5 에서 LogonUI.exe 를 모두 118회 확인했고 매번 잠김이었다. 첫 캡처 4장은 전 화소 검정이라 측정값이 없다(증거 세션 `evidence/c5/c3/c3_table.md`·`rule.md`, rule.md sha256 18c0488a…7c80a). c4 C3 PASS 는 측정 규칙을 캡처 뒤에 바꾼 탓에 근거가 부족해(D-OT-2) gate3 에서 이 재판정으로 대체됐다.
  - AC-SCOPE: 처음엔 FAIL 이었다. 원인은 소유자(pd-83)의 동시 수정을 우리 세션 수정과 구분하지 못하는 AC 설계 한계다. 위 pd-83 수정 파일들은 우리 세션이 수정하지 않았다(D-OT-3). 나중 회차에서 PASS 로 바뀌었지만, 이는 goal.json 에 내용을 덧붙이면서 mtime 기준선이 옮겨졌기 때문이다. 실제로 해소된 것이 아니다.
- C3 재측정 위험(추정, 확인 안 함): c4 합성 캡처를 다시 해석하면 바깥 1px 이 Windows 11 DWM 창 테두리일 수 있다. 그렇다면 앱 외곽선 1px 에 DWM 1px 가 더해져 2px 로 재지고, 규칙(합성 값 그대로)대로면 외곽선 FAIL 소견이 나올 수 있다(c3_table.md §5).
- 공개 사항
  - odev-2 가 `02_결정기록_ADR.md` 를 oio file_edit 대신 bash_exec 안의 python3 로 일괄 치환했다(결과 정상, 자진 보고).
  - AC-SCOPE 의 PASS 전환은 위와 같이 기준선 이동 때문이다.
  - c4 증거가 세션 `logs/prev_cycle/` 로 옮겨져 M7 gate3 이 가리키던 증거 경로가 사라졌다.
  - b6(ADR-058~066)에 인용한 MinIO·Caddy 수치는 이번 사이클에 다시 확인하지 않았다.
  - 메인이 `auto_decisions.jsonl` 의 일부 ts 를 실측 없이 적었다.
- 자율 결정 기록: 세션 `auto_decisions.jsonl` 의 D-C5-1·D-C5-1b·D-C5-2·D-OT-1·D-OT-2·D-OT-3.
- git 저장소가 아니어서 커밋은 하지 않았다.
- 이월 후보: M1·M6(사용자 직접 확인 또는 공개 판정 수단 확보), C3(잠금 해제 뒤 rule.md 그대로 재측정, DWM 테두리 위험 함께 확인), TODO-44~49.

## 2026-11-01 — oto 사이클4(conv_179349873953, goal G-C4A1D07E, O3): 사용자 실기 확인 항목 M1·M3·M6·M7·C3 실측

- 범위는 사이클3 에서 사용자 조치로 남긴 M1(Win+화살표 스냅)·M3(트레이)·M6(Alt+Tab)·M7(메모장 그림자 비교)·C3(창틀 비교)를 입력 없이 가능한 한 직접 실측하는 것이다. 소스 코드 변경은 없다(시험 도구와 문서만). 증거는 세션 `evidence/c4/summary.md`, 판단 기록은 세션 `auto_decisions.jsonl` 의 D-C4-1~7 이다.
- 판정은 gate3 기준 5개 중 PASS 3(M3·M7·C3), UNDET 2(M1·M6)이다.
  - M3 PASS: 트레이 아이콘을 UIA 로 찾아(오버플로 영역) 최소화 상태에서 Invoke 1회 → 창 복원, 앱 내장 `tray.state` 와 일치. 우클릭 메뉴는 입력 없이 열 수 없어 측정 불가(항목 8개는 선언만 확인).
  - M7·C3 PASS(oreview-1 검토 포함): 새 메모장 창 비교 및 창틀 계측. Windows 스타일 제목색 띠 수정 전 2/2/3 → 후 0/0/0, 외곽선 1px, 반경 8±1, 단일 원호. Mars 클래식은 전후 동일(불변).
  - M1 UNDET: Win+←/→/↑ 키 입력 가드(F10-3)로 Win·Tab·화살표 키 주입 경로가 없다. 대체로 `docs.state`/`sdi.setBounds` 상태 전환에서 그림자 추종·최대화 숨김을 확인했으나 OS 스냅을 직접 본 것이 아니어서 직접 증거가 아니다.
  - M6 UNDET: Alt+Tab 후보 창 규칙 평가(Pd 메인·SDI 포함, PdSdiShadow 제외)만 가능했고 Alt+Tab 화면은 직접 보지 못했다. 사람용 절차는 세션 `plans/c4_manual_M1_M6.md` 에 있다.
- 새 시험 자산(`poc/ui/test/README.md` 색인 등재 완료): `poc/ui/test/desktop/measure/notepad_new_window.ps1`(새 메모장 창 확보·종료, HWND 기준), `alttab_list.ps1`(Alt+Tab 후보 규칙 평가), `tray_uia.ps1`(트레이 UIA 측정), `frame_meter.py`(C3 창틀 계측기).
- 공개 사항
  - 22:18 에 띄운 새 메모장이 사용자의 저장 안 된 세션 탭을 복원했다(Win11 메모장 세션 복원). 캡처 4장을 삭제했고 창은 조작하지 않았다.
  - `PD_ALLOW_ONSCREEN` 으로 화면 안에서 기동한 것은 사용자의 22:34 포그라운드 허가에 근거한다.
  - 수정 전 비교용 빌드 c2dev_z 가 `%LOCALAPPDATA%\PdPoc\remote-control.json` 을 만들고 종료 시 삭제했다. 시험 이전에 그 파일이 있었는지는 확인하지 못했다(layout/settings md5 는 전후 동일).
  - SDI 최대화 확인 중 주 모니터 -8,-8 1936x1168 에 짧게 올라가 사용자 화면에 순간 노출됐을 수 있다(키·마우스 입력 아님).
  - otest_done 마커는 F-CRUD-1 차단으로 쓰지 못해 메인이 PARTIAL 로 기록했다.
- git 저장소가 아니어서 커밋은 하지 않았다.
- 이월 후보: M1·M6 사용자 직접 확인(위 매뉴얼), pd-83 에 P87 등재 요청(D-C4-7).

## 2026-11-01 — oto 사이클3(conv_179349873953, O4): pd-83 등재 요청 반영·SDI 그림자 견고성·창 크롬 후속·UI 형 규칙

> 판정 결과는 아래 "판정 결과" 항목에 적었다(PASS 37/43, gate3 재집계). 판정기는 세션 `c3_gate.py item <ID>`, 기록은 세션 `gate3/<ID>.json` 이다.

- 범위는 goal G-C0FBAF43 의 R1~R18·W1~W8·M1~M8·H1 과 덧붙인 C1~C3·L1~L4 이다. 근거는 세션 `plans/c3_request.md` 와 계획서 `plans/oplan_conv_179349873953.md`·`plans/oplan_c3_supp_layout.md` 다.
- pd-83 등재 요청 반영(문서, 우리 소유 정본)
  - ADR-016(서버 Rust)·ADR-014(PRJ-04·11 M2, M1 61·M2 54·M3 27·R2 44·R3 6)는 `plan/90_결정관리/02_결정기록_ADR.md`, ADR-023·019는 `02_결정기록_부록/b4_ADR-024_017.md`, ADR-043은 `b1_ADR-049_042.md`, ADR-025는 `b3_ADR-035_025.md` 에 개정 표기(결정(P84)(2026-11-01))로 반영했다.
  - `01_결정대기_질문목록.md` 의 D-22·D-19·D-08·D-23·D-44 행을 P84 결과로 갱신하고 D-48 서술을 고쳤다. 요청서 ⑦ 의 번호 매핑을 실제 행 내용으로 정정했다(D-19=제품명, D-23=검색, D-44=root·설치). 토큰 계산기 결정 행은 D-62 로 통일했다(D-56 이후가 index.json 에서 이미 사용 중이라 번호를 바꿨다).
  - `plan/08_데이터모델/adm.md`·`lic.md`·`imp.md` 에 설정 상속(inherit·override_value·시스템 기본값 테이블·이력·유효값 해석)을 넣고 "기본값 제안" 표기를 "기본값(시스템 설정 상속)"으로 바꿨다(grep 0건).
  - `plan/10_API설계/00_공통규약.md`·`col.md` 의 실시간 서술을 서버 Rust 기준으로 고치고, 설정 조회 응답 필드(value·source·limit·clamped)를 `00_공통규약.md`·`adm.md`·`lic.md` 에 넣었다. `plan/11_이벤트/01_outbox_이벤트목록.md` 에 `lic.SystemSettingChanged` 를 추가했다.
  - `plan/README.md`·`plan/index.json`·`.claude/rules/docs-map.md` 의 기능 수와 결정 문구를 P84 확정으로 맞췄다. `plan/_build/menu_tree.py` 를 재실행해 `메뉴구조.csv` 의 PRJ-04·11 기능릴리스가 M2 임을 확인했다(`02_메뉴구조.md` md5 불변).
  - `plan/09_화면설계/03_데스크톱_화면설계.md` §8.1 에 설정 상속 표시(P85)를 신설했다. 토큰 계산기는 별도 화면 없이 기존 화면 안에서 제공하는 것으로 결정 행을 남겼다.
- 코드 변경(poc/ui, 세부는 세션 `work/odev-*_result.json`)
  - SDI 그림자 안정화: `SdiShadowWindow.cs`(재진입 가드·try/catch·IsWindow 정리·Destroy), 신규 `SdiShadowSurface.cs`(그리기 분리), `SdiShadowNative.cs`(DeferWindowPos 묶음), `SdiShadowStack.cs`(SyncAll)·신규 `SdiShadowShape.cs`, `SdiWindowHost.cs`·신규 `SdiHostPlacement.cs`(Closed 정리·배치 분리), 진단 전용 `PD_SDI_SHADOW=fail`.
  - SDI 위치 동일화: `WindowActivation.cs`·신규 `SdiScreenPlacement.cs` 가 MDI↔SDI 전환 시 보이는 프레임 위치를 유지한다.
  - 창틀 자연화: Windows 스타일 `PdChromeFramePadding` 2,0,2,3 → 0, `PdChromeContentCorner` 0,0,4,4 → 0 (`WindowChromeStyle.cs`·`ChromeResources.axaml`, 설계값은 `plan/04_기술스택/05_창크롬_OS디자인_기준.md` §2.8.1 에 먼저 기록). Mars 클래식·macOS 값은 불변이다.
  - 창 버튼 PathIcon: `MainView.axaml` 의 `×` 버튼 2건을 `PdGlyphClose` PathIcon 으로 바꿨다.
  - 원격 정보 경로: `RemoteInfoPath.cs`(신규)가 `--data-dir`·PD_DATA_DIR 에 따라 `remote-control.json` 위치를 정하고, `pdctl.py` 가 격리 폴더 파일도 찾는다.
  - UI 형 용어·기기별 규칙: `UiStyleRules.cs`·`UiStyleViewModel.cs`(기기별 기본·선택 제한·자동 600/840dp·히스테리시스 32dp·디바운스 200ms), 설정 저장(`UiStyle`), 보기 메뉴·제목줄 스위치, 원격 명령 `ui.style`·`nav.menu`.
  - 자동 전환 애니메이션: `UiStyleTransition.cs`(120ms 퇴장+180ms 진입, 줄이기 존중 `MotionPreference.cs`), 플랫폼별 공급자(Desktop·Browser·Android Program/MainActivity).
  - 모바일형: `MobileNavView`·`MobileNavViewModel`·`NavMenuCatalog.cs`(하단 탭 5개+상단 왼쪽 삼선 서랍), 문자열 ko·en 각 191키, 도달 판정기 `test/common/judge/nav_reach_judge.py`.
  - 시험 도구: q16 측정기를 `test/desktop/measure/shadow_q16.py`·`judge/shadow_q16_judge.py` 로 승격하고 `poc/ui/test/README.md` 에 등재했다.
- 아이콘 공유 폴더: 프로젝트 루트 `assets/`(`_LICENSES.csv`, `generated/icons/`)를 만들었다. 합의 내용은 pd-83 과 공동 사용이라는 리드 지시이며, 아이콘 파일을 만드는 작업은 취소됐다(odev-l4t 결과).
- 판정 결과 (시험 뒤 기입)
  - 집계는 PASS 37/43 이다. 미통과 6건은 다음과 같다. M1·M6 은 키 입력 가드(F10-3)로 측정 수단이 없다. M3 은 트레이 화면 경계 100px 안전 규칙 때문이다. C3·M7 은 UNDET 이다(사용자 메모장 PID 38344 실행 중, iteration 2~5 재점검 결과 evidence/c3/fg/keys/otest3_iter2_recheck.txt). M8 은 blocked 이다(물리 다중 모니터 없음). R16·R18 은 이 문서 작업 뒤 기록한다.
  - 최종 판본은 데스크톱 c3dev_f·c3dev_h, 웹 c3dev_g·c3dev_h, 모바일 c3dev_h(헤드리스 에뮬레이터와 Fold5 실기기)다. W2~W8·H1·C2·M5 는 c3dev_b·c3dev_c 격리 실측이다. manifest 는 `poc/ui/logs/odev_c3/build/c3dev_{b,c,f,g,h}_*/manifest.md` 다.
  - PASS 는 R1~R15·R17(문서), L1(용어 정본·ADR-051), W1~W8(그림자 끔·켬·MarsClassic 즉시 반영·q16), H1(격리 remote-control.json), C1(모바일형 닫기 PathIcon), C2(MDI↔SDI 위치 3회 왕복 차 0), C4(실입력 6회, 놓은 뒤 2초 제목줄 중앙 차 (0,0), 분할 회귀 0), M2·M4·M5, L2·L3·L4(3축 합산, Fold5 nav_reach 98/98·입력 손실 0·☰ 실터치 3/3)다.
  - C3 의 Pd 단독 수치는 기준을 충족했다(띠 0·외곽선 1px·반경 8~9, Mars 클래식은 c3dev_b 대비 화소 차 0). 메모장과 비교하지 못해 UNDET 로 둔다.
- 사용자 조치 5건
  - M1: Pd SDI·주 창에서 Win+←/→/↑ 로 스냅되는지, 그림자가 따라가고 최대화 때 숨는지 확인한다.
  - M6: SDI 를 띄운 채 Alt+Tab 을 눌러 SDI 는 목록에 있고 그림자 창(빈 항목)은 없는지 확인한다.
  - M3: 알림 영역 Pd 아이콘을 눌러 메뉴와 창 복원이 동작하는지 확인한다.
  - C3: 창 아래 두 모서리와 창틀이 자연스러운지 직접 본다. 비교 측정은 사용자 메모장을 닫은 상태에서 요청하면 다시 한다.
  - M7: 메모장과 Pd SDI 의 바깥 그림자를 나란히 놓고 직접 본다. 재측정 조건은 C3 와 같다.
- 관찰 공개 8건(결함 확정 전 또는 이번 사이클 미수정)
  - D-C3-15: 태블릿 uiautomator bounds 에 상태바 inset 이 빠져 ☰ 를 오판정했다. 측정 좌표 문제이며 기능 결함이 아니다.
  - D-C3-17: SDI 그림자 여백·감쇠 곡선은 max(active, inactive) Extent 로 그린다. inactive Extent 는 active 보다 클 때만 효과가 난다. 설계이며 고치지 않았다.
  - SDI 열기 1회당 바인딩 오류 8건, 닫기 1회당 3건, MDI 문서 닫기 1개당 DockAdornerHost 1건이 나온다(c3dev_f, 기존 Dock 계열 추정, L4 범위 밖). c3dev_h 에서는 문서 닫기 건이 0 이었고 odev-c8 이 분석 중이다.
  - `WindowChromeStyle.cs` 의 ChromeInactive.axaml avares 런타임 로드는 웹 트리밍 산출물에서 빌드되지 않는다. 지금은 실행되지 않는 경로라 고치지 않았다.
  - 데스크톱 Program.cs:62 도 Android 와 같은 원격 서버·Dispatcher 경쟁 패턴이다(진단판 한정, 미재현, 미수정).
  - D-C3-16: 선임 측정이 사용자 메모장을 옮기고 전경화했다. 사용자 창 재조작이 되므로 복원하지 않고 보고했다.
  - 포그라운드 실입력 뒤 커서(끝 위치 (1388,810))와 전경 창을 복원하지 못했다.
  - c3dev_h 웹 회귀 때 1분 load 가 14~17 이라 전환 반응 시간 값은 성능 근거로 쓰지 않는다.
- 결함·수정 흐름(결정 기록 D-C3-11~19, 세션 `auto_decisions.jsonl`)
  - C4 제목줄 중앙 정렬: 사용자 추가 요구(D-C3-7)로 분할 밖 놓기 때 제목줄 중앙을 놓은 좌표에 맞추게 했다. "패널에서 창 하나"는 문서 탭으로 해석했다(D-C3-11). 가장자리 clamp 가 창을 캔버스 안으로 밀어 어긋나던 것을 없앴고(D-C3-12), center 안내 놓기는 예외로 기존 위치 규칙을 유지했다(D-C3-13). c3dev_e 에서는 두 번째 배치가 첫 배치를 덮어 로그는 OK 인데 실측이 틀렸다. s_inDrop 가드를 넣은 c3dev_f 에서 PASS 했다(세션 `work/handoff_odev-c8.md`).
  - 역라우팅 #3~#5 는 바인딩·웹 기동·Android 크래시·콜드 기동 결함을 다뤘다. 번호별 세부는 세션 `work/handoff_odev-c8.md`·`work/odev-c9_result.json` 에 있다.
  - 바인딩: 모바일형 전환 때 Compact.CompactDocument 캐스트 오류(D-C3-6), 상태줄 Value is null, Android·웹 Ancestor not found 를 고쳤다(`MainView.axaml`·`ShellStatusBar.axaml`·신규 `SingleWindowPlatformFix.cs`·`PinnedDockNoWindow.axaml`). 최종 판본의 L4 범위 [Binding] 은 0건이다.
  - 웹 기동: c3dev_f 웹·Android 가 PinnedDockNoWindow.axaml 사전 컴파일 XAML 부재로 기동에 실패했다(D-C3-18). c3dev_g 에서 웹이 정상 기동했다.
  - Android 크래시·콜드 기동: 원격 서버 /api/health 처리가 Setup 전에 Dispatcher 를 스레드풀 소유로 만들어 새 설치 첫 기동이 크래시했다. StartRemoteControl 을 AfterSetup 으로 옮겼다(D-C3-19). 태블릿 콜드 기동이 과도기 뷰 폭으로 Mobile 을 거치던 것은 SmallestScreenWidthDp·첫 ScreenWidthDp 사용과 디바운스 우선순위 조정으로 고쳤다(D-C3-14·19). c3dev_h 에서 태블릿 첫 판정 5/5 'Auto->Tablet' 이다.
- R16 확인: pd-83 요청 3건(P83 문서 추가, P84 결정 반영, P85 상속 보정)은 아래 세 항목에 이미 있다.
- 이월 후보와 자율 결정: D-C3-1~19(`auto_decisions.jsonl`). 이월 후보는 사용자 조치 5건의 결과 확인, M8 물리 다중 모니터 측정, 관찰 공개 항목 중 바인딩 오류·ChromeInactive trimming·데스크톱 원격 서버 경쟁, pd-83 P86 본문 반영(D-C3-10)이다.
- 마무리(odone)에서 LESSONS 에 c3 교훈을 보탠다. 프로젝트는 git 저장소가 아니어서 커밋 단계는 해당 없다.

## 2026-11-01 — UI PoC oto 사이클2(conv_179346042968, O4): 사용자 요청 U1~U21·품질 계약 재시험·판정보고서 §6.5 기록

- 범위는 goal 계약(2026-11-01 00:30, 36항목)과 덧붙인 D21·D22 다. 사용자 요청 U1~U21(창 UX·크롬·패널·탭·모니터·포커스)을 구현했고, 데스크톱·웹(Chrome)·Fold5 세 축에서 재시험했다. 성능 수치는 사용자 지시로 측정에서 뺐다.
- 빌드는 u → v → w → x → y8 → z 로 바꿔 가며 확정했다. 최종 판본 z 는 진단·제품 6종이 모두 rc=0 이다(`poc/ui/logs/odev_c2/build/manifest_c2dev_z_c2prod_z.md`). 진단 z 의 Desktop.dll·Shell.dll 은 Q2 #16 측정판 y8 과 md5 가 같다. y8·z 에서 고친 것은 SDI 그림자 별도 창(SdiShadowWindow.cs·SdiWindowHost.cs 등 Pd.Poc.Desktop 안)과 Shell 문자열 리소스 키 2개다. x 에서 고친 파일은 ChromeLogo.cs·WindowChromeStyle.cs·ChromeResources.axaml·DesktopMonitorProvider.cs·DesktopWindowHost.cs 다. 이전 판본에서 고친 파일은 팀원별 결과 파일(세션 `work/odev-*_result.json`)에 있다.
- 판정 결과(gate, 판정자 ≠ 구현자)는 다음과 같다. goal 36항목과 D21·D22, 곧 gate 38개가 모두 rc=0 이다(z 판본, oreview-4 10:36). Q2·Q14 는 재측정으로 rc=0 이 됐다.
  - Q2(데스크톱 재시험)는 PASS(z) 다. #16 SDI 창 그림자를 그림자 별도 창 방식(접근 C)으로 고쳤고, 기준 v3.2 를 결과 전에 고정한 뒤 A0~A4·B1~B8 이 PASS 다. 바깥 16px 밝기 감소 합(왼/아래)은 활성 390.4/447.1·비활성 234.1/290.0 이다(원자료 `poc/ui/logs/otest/c2/q16_y8/`, 기준 `poc/ui/logs/otest/c2/q16_t4/criteria.md`). 한계는 메모장 기준값을 08:03 캡처 고정 상수로 쓴 것(결과를 본 뒤 조항 변경, D-C2-58·61), Alt+Tab 직접 확인·다중 모니터·DPI 측정 불가, 회귀 HOLD 4건(#4~#7, 포그라운드 노출 불가)과 화면 안 보정(MDI-12) 회귀 미포함(도구에 항목 없음, 그림자는 별도 창이라 SDI 좌표 보정 로직 무변경, 다음 사이클로 넘김 D-C2-64)이다. 개선 과제 15항목은 다음 사이클로 넘겼다(D-C2-62).
    - 이력은 다음과 같다. x 까지는 활성·비활성 차이가 없었다(Pd 111.5/162.1 활성=비활성, Win11 메모장 활성 440.1·비활성 239.3). DWM 변형 11종과 앱 그림자 프로토타입이 모두 효과가 없거나 나빠서 한때 이월했고(D-C2-51), 변형·프로토타입 코드는 원복했다.
  - Q14(제목줄 한 줄)는 시험 축 otest-4 재측정으로 PASS 다(D-C2-56). ③ Win11 스냅은 Snap Layouts 50/50 두 칸을 2회 쟀고, 오른쪽 (960,0)-(1920,1152)·왼쪽 (0,0)-(960,1152)로 오차 0 이다. 사용자 창 16개 변화 0, 가드 차단 0, 기준은 사전 고정(`poc/ui/logs/otest/c2/snap_t4/criteria.md`)이다. 한계는 Win+←/→·가장자리 끌기 측정 불가, c2prod_x 측정 안 함, 커서 미복원이다(원자료 `poc/ui/logs/otest/c2/snap_t4/`).
    - 이력은 다음과 같다. 처음에는 입력 가드 F10-3 때문에 시험 축이 측정하지 못했고, 근거 없던 이전 PASS 를 환원했다(D-C2-52). DEV 라운드의 Snap Layouts 실측(왼쪽 절반 960×1152)은 가드 차단 뒤 PID 를 바꿔 재시도한 결과라 보조 근거로만 뒀다(D-C2-54·55). 그 과정에서 사용자 터미널 창이 함께 배치돼 즉시 원복했다.
  - Q13(판정보고서 기록)은 Q2 PASS 반영본을 oreview-4 가 재심사해 PASS 다. 최종 판본 md5 는 d72b43f5 다. gate 38개 rc=0 은 oreview-4 가 10:46:39 에 다시 쟀다.
- 문서 변경
  - `plan/04_기술스택/04_PoC_UI_판정보고서.md` §6.5.1·§6.5.2 에 최종 판정과 사이클2 결과를 적었다. 결론은 "제품 승격 불가(현 시점)"다. 남은 조건은 성능 측정, 품질 Q3·Q7·Q8 점검, Q5 로그 전수 판독이다. §6.5.1 에 Q2 개선 과제 15항목을 "다음 사이클 개선" 목록으로 적었다.
  - `plan/04_기술스택/05_창크롬_OS디자인_기준.md` §2.9 최소 크기 실측값(MDI 198×35, SDI 198×42, 클래식 미측정)을 채웠다. §2.10 에 SDI 그림자 DWM/Win32 분석을 새로 썼다(style·exStyle 대조, DLGFRAME 을 켜면 그림자 0).
  - `plan/README.md`·`.claude/rules/docs-map.md` 의 웹 차이표 범위를 W-01~W-16 으로 고쳤다. README·index.json 에 pd-83 문서 9개를 등재했다(아래 항목 요청).
- 측정 한계는 다음과 같다. Q6 ⑥·U20 멀티 모니터는 가상 모니터로 흉내만 냈다(물리 모니터 1대). Q9 ⑤ 는 z 재측정에서 서빙 루트를 기록했다(`poc/ui/logs/otest_c2w/z/serve_root.txt`, 2회 모두 rAF 0). 트레이 실표시, Q2 #18 최소화 pressed, Fold5 관리형 창 로고는 측정하지 못했다.
- 위반 기록은 다음과 같다. 웹 시험에서 새 스크립트 2건(pw_q9_prod_web.js·pw_w_a.js)을 만들어 사용자 지시를 어겼고, 그 결과는 보조 근거로만 남겼다(D-C2-33). 팀원 1명이 gate 파일을 직접 쓴 일이 있었다.
- 다음 사이클 이월 후보는 Q2 #16 그림자 개선 15항목(D-C2-62), Q14 ③ 스냅의 Win+←/→·가장자리 끌기·제품판 측정, 진단 필드 정확도(D-C2-30·49), 작업표시줄 탭 → 관리형 창 합류(D-C2-47, 신규 기능), PocDockFactory.cs 299/300줄이다.
- 자율 결정은 D-C2-19~64 이다(세션 `auto_decisions.jsonl`).
- 마무리(odone)에서 LESSONS 에 교훈 10~12번(리드 오안내, 개발 팀원 확인을 판정 근거로 쓰려 한 일, gate 회귀 범위 오기)을 보탰다. 이 프로젝트는 git 저장소가 아니어서 커밋 단계는 해당 없음으로 처리했다.

