# 시험 고정 자료(test/common/testdata — 2026-10-31 tools/ 에서 옮김)

원본은 앱이 실행되면서 덮어쓰므로, 회귀 시험에 쓰는 표본은 여기 복사해 고정한다.

| 파일 | 출처 | 용도 |
|---|---|---|
| `layout_v3_right.json` | 2026-11-04 패널 위치 메뉴 판(`logs/odev-fix-panelmenu/compat/profile/layout.json`)이 저장한 실제 Version 3 파일. 탐색기·로그 가장자리 값이 없다 | `desktop/measure/retest_layout_v3_read.py` 고정 시험. 알림 위치·분할·모서리는 이 표본을 고쳐서 만든다 |
| `layout_v1_z5_20261028_2333.json` | `%LOCALAPPDATA%\\PdPoc\\layout.json`(2026-10-28 23:33:45). `retest_remote_z.py --mode user` 의 메인 창 닫기 자동 저장본이다. md5 089584c2c14324107133898b13d4cc17 | 레이아웃 저장 형식 **Version 1** 표본이다. MDI, 문서 Z1~Z5, ActiveId=Grid-32 이고 IsPinned·IsLocked·ZOrder 가 들어 있다. UI-11 의 Version 2 저장소가 Version 1 을 읽는지 보는 회귀 시험에 쓴다 |
| `dtheme_legacy_settings.json` | REQ-101 판(요소별 사용자 지정 직후, 디자인 테마 축 이전) 저장 형식. `UiSettingsDto.cs` 직렬화 키와 17 설계 §3·§4(`BaseTemplateId`·`Design` 희소 저장, 키 `<요소>.<속성>`, 값 문자열)를 근거로 작성. 사용자 테마 `시험 요소별 테마`(Id `user-dtheme01`, 기반 Light, `BaseTemplateId` pd-light, 색 3개 + `Design` 8키). `DesignThemeId`·`MotionLevel`·`EffectOverrides` 키는 없다. 커밋 대조 없음 | REQ-104 디자인 테마 판 호환 시험(T-legacy-fixture, AC-K8). 가져온 뒤 `Design` 8키가 유지되고 디자인 테마 값은 기본값으로 채워져야 하며, 화면 계산값이 저장 전과 같아야 한다 |
| `theme_legacy_settings.json` | 요소별 디자인 키(`Design`·`BaseTemplateId`)가 없던 구판 테마 저장본. 워킹트리 소스를 읽어 작성(커밋 대조 없음). 최상위 키는 UiSettingsDto 일부 키만 포함. 사용자 테마 `시험 구판 테마`(Id `user-legacy01`, 기반 Light, 색 3개 — 바탕 `#FAF0E6`·강조 `#7A1F8E`·글자 `#1B1B1B`)를 선택한 상태다. 나머지 색 토큰은 없어서 기반 pd-light 값으로 채워진다 | REQ-101 요소별 사용자 지정의 옛 저장본 호환 시험(T10 `legacy_profile`·옛 저장본 무변경 확인). 가져온 뒤 `Design` 사전이 비어 있고 화면 계산값이 구판과 같아야 한다 |
