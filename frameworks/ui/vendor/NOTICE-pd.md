# vendor 폴더 고지

## avalonia-12.1.4

- 출처: https://github.com/AvaloniaUI/Avalonia 태그 `12.1.4`(커밋 2d79ba55fae1b1e71c69f93a668080c9029e6a31). 필요한 경로만 sparse checkout 했다(src/Browser/Avalonia.Browser, src/Shared/RawEventGrouping.cs, build/, 루트 설정 파일).
- 라이선스: MIT(원문 `avalonia-12.1.4/licence.md`, Copyright (c) AvaloniaUI OÜ). 원문을 지우거나 고치지 않는다. 패치판을 배포하면 이 고지와 원문을 함께 둔다.
- PD 수정(12.1.4-pd.1): `patches/avalonia-browser-12.1.4-idle-raf.pd.1.patch`
  - `webapp/modules/avalonia/timer.ts`: rAF 루프를 start/stop 쌍으로 바꾸고 요청 ID 를 보관해 취소한다.
  - `Interop/TimerHelper.cs`: StartAnimationFrames·StopAnimationFrames JSImport 추가.
  - `Rendering/BrowserRenderTimer.cs`: 단일 스레드에서 Tick=null(RenderLoop 유휴)이면 정지, 값이 오면 재개. 다중 스레드 경로는 원본 동작.
  - `Avalonia.Browser.csproj`: PdStandalone=true 면 Avalonia·Skia·HarfBuzz 를 NuGet 12.1.4 로 참조(단독 빌드).
- 목적: 웹 유휴 중 requestAnimationFrame 약 60회/s(Q9 ⑤) 제거. 근거 `poc/ui/logs/odev3/web_idle_raf*.md`.
- 업스트림 이슈 초안: `poc/ui/logs/odev3/upstream_issue_browser_raf_draft.md`(등록은 사용자 승인 뒤).
