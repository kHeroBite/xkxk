# frameworks/ui 소스 계층 분류표

> 자동 생성물 — 고치지 말고 `python3 -B tools/classify_layers.py`(영역 루트 `frameworks/ui` 기준) 로 재생성한다. 예외는 `tools/classify_layers_overrides.json`(사유 필수).
> 층 정의와 참조 규칙의 정본은 `plan/04_기술스택/02_소스구조_분할규칙.md`, 강제는 `tools/check_layers.py` 다. 형식 참조는 정규식 근사이고 빌드가 최종 판정이다.

## 분류별 파일 수

| 층 | 파일 수 |
|---|---:|
| Control | 273 |
| Data | 35 |
| Diag | 90 |
| Host | 74 |
| Ui | 26 |
| Views | 240 |

합계 738개(.cs·.axaml).

## 끊어야 하는 참조(현재 기준 금지 방향)

0건. Shell 이 화면을 직접 만드는 지점과 Data 후보가 밖을 보는 지점이다.

| 참조하는 파일 | 층 | 참조되는 파일 | 층 | 형식 |
|---|---|---|---|---|

## 층에 걸친 partial 형식

0건. 없음

## 옮기지 않기로 한 판단

- **`xkxk.frameworks.ui` 가 상위 층의 자원 사전 키·토큰 이름을 아는 결합(리뷰 M-7)** — 유지한다. 키 이름은 형식 참조가 아니라 자원 조회 문자열이고, `xkxk.frameworks.ui` 프로젝트 참조 방향은 그대로 `Avalonia` 뿐이다. Avalonia 제어층의 정상 범위로 판단했다. 키를 없애려면 컨트롤마다 브러시 속성을 새로 열어 값을 넘겨야 해 얻는 분리보다 호출부 비용이 크다.
- **`xkxk.frameworks.ui.shell` 의 시각 트리 순회 18개 파일(`Diagnostics` 제외)** — 옮기지 않는다. 창 관리·드래그·투어 같은 제어 로직이 Dock 템플릿 안 컨트롤을 찾는 일로, 화면을 그리는 코드가 아니라 화면을 조작하는 코드다. Avalonia 제어층의 정상 범위로 판단했다. 화면 쪽과의 결합은 부품 이름뿐이며 `xkxk.frameworks.ui` 의 `TemplateParts` 상수로 묶고 `check_layers.py` 가 실재를 대조한다. 18개는 2026-11-04 실측값이다(`PanelViewState` 를 Views 로 옮긴 뒤).

## 파일별 분류

| 파일 | 층 | 이동 뒤 경로 | 근거 | Avalonia | Dock.Model | 내부 형식 |
|---|---|---|---|:-:|:-:|---|
| `xkxk.frameworks.ui.android/AndroidApp.cs` | Host |  | 호스트 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.android/AndroidDeviceInfo.cs` | Host |  | 호스트 프로젝트 |  |  | AndroidDeviceInfo |
| `xkxk.frameworks.ui.android/AndroidLayoutStore.cs` | Host |  | 호스트 프로젝트 |  |  |  |
| `xkxk.frameworks.ui.android/AndroidSafeArea.cs` | Host |  | 호스트 프로젝트 | O |  | AndroidSafeArea |
| `xkxk.frameworks.ui.android/MainActivity.cs` | Host |  | 호스트 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.android/StartupGesture.cs` | Host |  | 호스트 프로젝트 |  |  | StartupGesture |
| `xkxk.frameworks.ui.android/StartupHitTest.cs` | Host |  | 호스트 프로젝트 | O |  | StartupHitTest |
| `xkxk.frameworks.ui.android/StartupMarks.cs` | Host |  | 호스트 프로젝트 |  |  | StartupMarks |
| `xkxk.frameworks.ui.android/StartupTouchGate.Discard.cs` | Host |  | 호스트 프로젝트 | O |  | StartupTouchGate |
| `xkxk.frameworks.ui.android/StartupTouchGate.cs` | Host |  | 호스트 프로젝트 | O |  | StartupTouchGate |
| `xkxk.frameworks.ui.browser/AutomationExports.cs` | Host |  | 호스트 프로젝트 |  |  |  |
| `xkxk.frameworks.ui.browser/BrowserBlobStore.cs` | Host |  | 호스트 프로젝트 |  |  |  |
| `xkxk.frameworks.ui.browser/BrowserHostOs.cs` | Host |  | 호스트 프로젝트 |  |  | BrowserHostOs |
| `xkxk.frameworks.ui.browser/BrowserKeyValueStore.cs` | Host |  | 호스트 프로젝트 |  |  |  |
| `xkxk.frameworks.ui.browser/BrowserLayoutStore.cs` | Host |  | 호스트 프로젝트 |  |  |  |
| `xkxk.frameworks.ui.browser/BrowserMotionPreference.cs` | Host |  | 호스트 프로젝트 |  |  | BrowserMotionPreference |
| `xkxk.frameworks.ui.browser/BrowserPressTimeSource.cs` | Host |  | 호스트 프로젝트 |  |  | BrowserPressTimeSource |
| `xkxk.frameworks.ui.browser/LifecycleExports.cs` | Host |  | 호스트 프로젝트 |  |  |  |
| `xkxk.frameworks.ui.browser/PageKeyFocus.cs` | Host |  | 호스트 프로젝트 |  |  | PageKeyFocus |
| `xkxk.frameworks.ui.browser/PagePointerLeave.cs` | Host |  | 호스트 프로젝트 |  |  | PagePointerLeave |
| `xkxk.frameworks.ui.browser/Program.Storage.cs` | Host |  | 호스트 프로젝트 |  |  | Program |
| `xkxk.frameworks.ui.browser/Program.cs` | Host |  | 호스트 프로젝트 | O |  | Program |
| `xkxk.frameworks.ui.browser/StartupTiming.cs` | Host |  | 호스트 프로젝트 | O |  | StartupTiming |
| `xkxk.frameworks.ui.data/AppChannel.cs` | Data |  | xkxk.frameworks.ui.data 프로젝트 |  |  |  |
| `xkxk.frameworks.ui.data/AppDataFolder.cs` | Data |  | xkxk.frameworks.ui.data 프로젝트 |  |  |  |
| `xkxk.frameworks.ui.data/Layout/BackupBundle.cs` | Data |  | xkxk.frameworks.ui.data 프로젝트 |  |  |  |
| `xkxk.frameworks.ui.data/Layout/BackupIndex.cs` | Data |  | xkxk.frameworks.ui.data 프로젝트 |  |  |  |
| `xkxk.frameworks.ui.data/Layout/BackupIndexItem.cs` | Data |  | xkxk.frameworks.ui.data 프로젝트 |  |  |  |
| `xkxk.frameworks.ui.data/Layout/BackupJsonContext.cs` | Data |  | xkxk.frameworks.ui.data 프로젝트 |  |  |  |
| `xkxk.frameworks.ui.data/Layout/FileKeyValueStore.cs` | Data |  | xkxk.frameworks.ui.data 프로젝트 |  |  |  |
| `xkxk.frameworks.ui.data/Layout/FreshGuardStores.cs` | Data |  | xkxk.frameworks.ui.data 프로젝트 |  |  |  |
| `xkxk.frameworks.ui.data/Layout/ILayoutLibrary.cs` | Data |  | xkxk.frameworks.ui.data 프로젝트 |  |  |  |
| `xkxk.frameworks.ui.data/Layout/ISettingsBackupSource.cs` | Data |  | xkxk.frameworks.ui.data 프로젝트 |  |  |  |
| `xkxk.frameworks.ui.data/Layout/LayoutStore.cs` | Data |  | xkxk.frameworks.ui.data 프로젝트 |  |  |  |
| `xkxk.frameworks.ui.data/Layout/PanelTabsSnapshot.cs` | Data |  | xkxk.frameworks.ui.data 프로젝트 |  |  |  |
| `xkxk.frameworks.ui.data/Layout/ProfileStores.cs` | Data |  | xkxk.frameworks.ui.data 프로젝트 |  |  |  |
| `xkxk.frameworks.ui.data/Logging/ConsoleLogSink.cs` | Data |  | xkxk.frameworks.ui.data 프로젝트 |  |  |  |
| `xkxk.frameworks.ui.data/Logging/FileLogSink.cs` | Data |  | xkxk.frameworks.ui.data 프로젝트 |  |  |  |
| `xkxk.frameworks.ui.data/Logging/LogExport.cs` | Data |  | xkxk.frameworks.ui.data 프로젝트 |  |  |  |
| `xkxk.frameworks.ui.data/Logging/MemoryLogSink.cs` | Data |  | xkxk.frameworks.ui.data 프로젝트 |  |  |  |
| `xkxk.frameworks.ui.data/Logging/PdLog.cs` | Data |  | xkxk.frameworks.ui.data 프로젝트 |  |  |  |
| `xkxk.frameworks.ui.data/Logging/PdLogEntry.cs` | Data |  | xkxk.frameworks.ui.data 프로젝트 |  |  |  |
| `xkxk.frameworks.ui.data/Navigation/CmdIconKeys.Generated.cs` | Data |  | xkxk.frameworks.ui.data 프로젝트 |  |  |  |
| `xkxk.frameworks.ui.data/Navigation/MenuIconCatalog.Generated.cs` | Data |  | xkxk.frameworks.ui.data 프로젝트 |  |  |  |
| `xkxk.frameworks.ui.data/Navigation/MenuIconCatalog.cs` | Data |  | xkxk.frameworks.ui.data 프로젝트 |  |  |  |
| `xkxk.frameworks.ui.data/Navigation/NavMenuCatalog.cs` | Data |  | xkxk.frameworks.ui.data 프로젝트 |  |  |  |
| `xkxk.frameworks.ui.data/Navigation/PersonalFavoriteStore.cs` | Data |  | xkxk.frameworks.ui.data 프로젝트 |  |  | FavoriteJsonContext |
| `xkxk.frameworks.ui.data/Rendering/GpuSetting.cs` | Data |  | xkxk.frameworks.ui.data 프로젝트 |  |  |  |
| `xkxk.frameworks.ui.data/Samples/GridSeedLegacy.cs` | Data |  | xkxk.frameworks.ui.data 프로젝트 |  |  |  |
| `xkxk.frameworks.ui.data/Samples/SampleLoadDelay.cs` | Data |  | xkxk.frameworks.ui.data 프로젝트 |  |  |  |
| `xkxk.frameworks.ui.data/Theming/BackgroundImageStore.cs` | Data |  | xkxk.frameworks.ui.data 프로젝트 |  |  |  |
| `xkxk.frameworks.ui.data/Theming/LocalBlobStore.cs` | Data |  | xkxk.frameworks.ui.data 프로젝트 |  |  |  |
| `xkxk.frameworks.ui.data/Theming/ThemeFileExchange.cs` | Data |  | xkxk.frameworks.ui.data 프로젝트 |  |  |  |
| `xkxk.frameworks.ui.desktop/AppIdentity.cs` | Host |  | 호스트 프로젝트 |  |  | AppIdentity |
| `xkxk.frameworks.ui.desktop/AppWindowIcon.cs` | Host |  | 호스트 프로젝트 | O |  | AppWindowIcon |
| `xkxk.frameworks.ui.desktop/BgPlacement.cs` | Host |  | 호스트 프로젝트 |  |  |  |
| `xkxk.frameworks.ui.desktop/BgWindowPolicy.cs` | Host |  | 호스트 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.desktop/CaptureProtection.cs` | Host |  | 호스트 프로젝트 | O |  | CaptureProtection |
| `xkxk.frameworks.ui.desktop/DesktopDiagHooks.cs` | Host |  | 호스트 프로젝트 |  |  | DesktopDiagHooks |
| `xkxk.frameworks.ui.desktop/DesktopMonitorProvider.cs` | Host |  | 호스트 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.desktop/DesktopWindowHost.cs` | Host |  | 호스트 프로젝트 | O | O |  |
| `xkxk.frameworks.ui.desktop/FileLayoutStore.cs` | Host |  | 호스트 프로젝트 |  |  |  |
| `xkxk.frameworks.ui.desktop/HostArgs.cs` | Host |  | 호스트 프로젝트 |  |  | HostArgs |
| `xkxk.frameworks.ui.desktop/HostLog.cs` | Host |  | 호스트 프로젝트 |  |  | HostLog |
| `xkxk.frameworks.ui.desktop/HostRenderOptions.cs` | Host |  | 호스트 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.desktop/LaunchOptions.cs` | Host |  | 호스트 프로젝트 | O |  | LaunchOptions |
| `xkxk.frameworks.ui.desktop/MainBgPinHook.cs` | Host |  | 호스트 프로젝트 | O |  | MainBgPinHook |
| `xkxk.frameworks.ui.desktop/MainNcMenuHook.cs` | Host |  | 호스트 프로젝트 | O |  | MainNcMenuHook |
| `xkxk.frameworks.ui.desktop/MainWindowChrome.cs` | Host |  | 호스트 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.desktop/MainWindowDecorations.cs` | Host |  | 호스트 프로젝트 | O |  | MainWindowDecorations |
| `xkxk.frameworks.ui.desktop/MaxTrackHook.cs` | Host |  | 호스트 프로젝트 | O |  | MaxTrackHook |
| `xkxk.frameworks.ui.desktop/MonitorChangeWatcher.cs` | Host |  | 호스트 프로젝트 | O |  | MonitorChangeWatcher |
| `xkxk.frameworks.ui.desktop/MonitorFitService.cs` | Host |  | 호스트 프로젝트 | O |  | MonitorFitService |
| `xkxk.frameworks.ui.desktop/OffscreenGuardHook.cs` | Host |  | 호스트 프로젝트 | O |  | OffscreenGuardHook |
| `xkxk.frameworks.ui.desktop/OsSnapProbe.cs` | Host |  | 호스트 프로젝트 |  |  | OsSnapProbe |
| `xkxk.frameworks.ui.desktop/Program.cs` | Host |  | 호스트 프로젝트 | O |  | Program |
| `xkxk.frameworks.ui.desktop/SdiAboveMainHook.cs` | Host |  | 호스트 프로젝트 | O |  | SdiAboveMainHook |
| `xkxk.frameworks.ui.desktop/SdiBgPinHook.cs` | Host |  | 호스트 프로젝트 | O |  | SdiBgPinHook |
| `xkxk.frameworks.ui.desktop/SdiDocumentIcon.cs` | Host |  | 호스트 프로젝트 | O | O | SdiDocumentIcon |
| `xkxk.frameworks.ui.desktop/SdiHostPlacement.cs` | Host |  | 호스트 프로젝트 | O |  | SdiHostPlacement |
| `xkxk.frameworks.ui.desktop/SdiMagnetHook.cs` | Host |  | 호스트 프로젝트 | O |  | SdiMagnetHook |
| `xkxk.frameworks.ui.desktop/SdiMagnetSnapper.cs` | Host |  | 호스트 프로젝트 | O |  | SdiMagnetSnapper |
| `xkxk.frameworks.ui.desktop/SdiQuietState.cs` | Host |  | 호스트 프로젝트 | O | O | SdiQuietState |
| `xkxk.frameworks.ui.desktop/SdiResizeDebounce.cs` | Host |  | 호스트 프로젝트 | O |  | SdiResizeDebounce |
| `xkxk.frameworks.ui.desktop/SdiShadowBitmap.cs` | Host |  | 호스트 프로젝트 |  |  | SdiShadowBitmap |
| `xkxk.frameworks.ui.desktop/SdiShadowNative.cs` | Host |  | 호스트 프로젝트 |  |  | SdiShadowNative |
| `xkxk.frameworks.ui.desktop/SdiShadowShape.cs` | Host |  | 호스트 프로젝트 | O |  | SdiShadowShape |
| `xkxk.frameworks.ui.desktop/SdiShadowStack.cs` | Host |  | 호스트 프로젝트 | O |  | SdiShadowStack |
| `xkxk.frameworks.ui.desktop/SdiShadowSurface.cs` | Host |  | 호스트 프로젝트 |  |  | SdiShadowSurface |
| `xkxk.frameworks.ui.desktop/SdiShadowWindow.cs` | Host |  | 호스트 프로젝트 | O |  | SdiShadowWindow |
| `xkxk.frameworks.ui.desktop/SdiSnapAssistHost.cs` | Host |  | 호스트 프로젝트 | O |  | SdiSnapAssistHost |
| `xkxk.frameworks.ui.desktop/SdiSnapBarHost.cs` | Host |  | 호스트 프로젝트 | O |  | SdiSnapBarHost |
| `xkxk.frameworks.ui.desktop/SdiSnapHook.cs` | Host |  | 호스트 프로젝트 | O | O | SdiSnapHook |
| `xkxk.frameworks.ui.desktop/SdiTabJoinHook.cs` | Host |  | 호스트 프로젝트 | O |  | SdiTabJoinHook |
| `xkxk.frameworks.ui.desktop/SdiWindowHost.cs` | Host |  | 호스트 프로젝트 | O | O |  |
| `xkxk.frameworks.ui.desktop/SdiZOrderProbe.cs` | Host |  | 호스트 프로젝트 | O | O | SdiZOrderProbe |
| `xkxk.frameworks.ui.desktop/SdiZOrderRaiser.cs` | Host |  | 호스트 프로젝트 | O | O | SdiZOrderRaiser |
| `xkxk.frameworks.ui.desktop/SingleInstance.cs` | Host |  | 호스트 프로젝트 |  |  |  |
| `xkxk.frameworks.ui.desktop/SysWindowBgPinHook.cs` | Host |  | 호스트 프로젝트 | O |  | SysWindowBgPinHook |
| `xkxk.frameworks.ui.desktop/Win32ModalBlock.cs` | Host |  | 호스트 프로젝트 | O |  | Win32ModalBlock |
| `xkxk.frameworks.ui.desktop/WindowDefaultMove.cs` | Host |  | 호스트 프로젝트 | O | O | WindowDefaultMove |
| `xkxk.frameworks.ui.desktop/WindowFrameMetrics.cs` | Host |  | 호스트 프로젝트 | O |  | WindowFrameMetrics |
| `xkxk.frameworks.ui.desktop/WindowPlacement.cs` | Host |  | 호스트 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.desktop/WindowPosTrace.cs` | Host |  | 호스트 프로젝트 | O |  | WindowPosTrace |
| `xkxk.frameworks.ui.shell/App.cs` | Control |  | 정적 진입점 표면(App.*)을 셸 42곳·호스트가 쓴다. axaml 은 xkxk.frameworks.ui.views 의 PdApp 으로 가고 이 파일은 기반 클래스로 남는다 | O | O |  |
| `xkxk.frameworks.ui.shell/Busy/BusyCursorDriver.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  | BusyCursorDriver |
| `xkxk.frameworks.ui.shell/Busy/BusyHooks.Release.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(BusyCursorDriver.cs) |  |  |  |
| `xkxk.frameworks.ui.shell/Busy/BusyHooks.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `xkxk.frameworks.ui.shell/Busy/BusyScope.cs` | Control |  | BusyService 짝. 판정기 고정 경로(AC-2-02) |  |  |  |
| `xkxk.frameworks.ui.shell/Busy/BusyService.cs` | Control |  | 표시 지연 상태 전이. 판정기 고정 경로(AC-2-02·2-05) |  |  |  |
| `xkxk.frameworks.ui.shell/Busy/DispatcherBusyClock.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `xkxk.frameworks.ui.shell/Diagnostics/AutomationArgs.cs` | Diag |  | 진단 전용 폴더 |  |  | AutomationArgs |
| `xkxk.frameworks.ui.shell/Diagnostics/AutomationBridge.Product.cs` | Diag |  | 진단 전용 폴더 | O |  |  |
| `xkxk.frameworks.ui.shell/Diagnostics/AutomationBridge.cs` | Diag |  | 진단 전용 폴더 | O |  |  |
| `xkxk.frameworks.ui.shell/Diagnostics/AutomationCommandRunner.cs` | Diag |  | 진단 전용 폴더 | O | O | AutomationCommandRunner |
| `xkxk.frameworks.ui.shell/Diagnostics/AutomationOptions.cs` | Diag |  | 진단 전용 폴더 |  |  |  |
| `xkxk.frameworks.ui.shell/Diagnostics/GridMeasureTrace.cs` | Diag |  | 진단 전용 폴더 | O |  |  |
| `xkxk.frameworks.ui.shell/Diagnostics/IDocumentDiagnostics.cs` | Diag |  | 진단 전용 폴더 | O |  |  |
| `xkxk.frameworks.ui.shell/Diagnostics/MdiDragFrameDiag.Stages.cs` | Diag |  | 진단 전용 폴더 | O |  |  |
| `xkxk.frameworks.ui.shell/Diagnostics/MdiDragFrameDiag.cs` | Diag |  | 진단 전용 폴더 | O |  |  |
| `xkxk.frameworks.ui.shell/Diagnostics/MdiDragProbe.cs` | Diag |  | 진단 전용 폴더 | O |  |  |
| `xkxk.frameworks.ui.shell/Diagnostics/NotificationPanelProbe.cs` | Diag |  | 진단 전용 폴더 |  |  |  |
| `xkxk.frameworks.ui.shell/Diagnostics/PerfCommitLog.cs` | Diag |  | 진단 전용 폴더 | O |  | PerfCommitLog |
| `xkxk.frameworks.ui.shell/Diagnostics/PerfFrameLog.cs` | Diag |  | 진단 전용 폴더 |  |  | PerfFrameLog |
| `xkxk.frameworks.ui.shell/Diagnostics/PerfLayoutLog.cs` | Diag |  | 진단 전용 폴더 | O |  | PerfLayoutLog |
| `xkxk.frameworks.ui.shell/Diagnostics/PerfOpenTracker.cs` | Diag |  | 진단 전용 폴더 | O |  | PerfOpenTracker |
| `xkxk.frameworks.ui.shell/Diagnostics/PerfOperations.cs` | Diag |  | 진단 전용 폴더 |  |  | PerfOperations |
| `xkxk.frameworks.ui.shell/Diagnostics/PerfProbe.Counters.cs` | Diag |  | 진단 전용 폴더 |  |  |  |
| `xkxk.frameworks.ui.shell/Diagnostics/PerfProbe.Product.cs` | Diag |  | 진단 전용 폴더 | O |  |  |
| `xkxk.frameworks.ui.shell/Diagnostics/PerfProbe.cs` | Diag |  | 진단 전용 폴더 | O |  |  |
| `xkxk.frameworks.ui.shell/Diagnostics/PerfSampler.cs` | Diag |  | 진단 전용 폴더 | O |  | PerfSampler |
| `xkxk.frameworks.ui.shell/Diagnostics/PerfStats.cs` | Diag |  | 진단 전용 폴더 |  |  | PerfStats |
| `xkxk.frameworks.ui.shell/Diagnostics/PerfUiRunner.cs` | Diag |  | 진단 전용 폴더 | O | O | PerfUiRunner |
| `xkxk.frameworks.ui.shell/Diagnostics/PerfUiSplit.cs` | Diag |  | 진단 전용 폴더 | O |  | PerfUiSplit |
| `xkxk.frameworks.ui.shell/Diagnostics/PinchZoomDiag.cs` | Diag |  | 진단 전용 폴더 | O |  |  |
| `xkxk.frameworks.ui.shell/Diagnostics/Remote/ContextMenuInspector.cs` | Diag |  | 진단 전용 폴더 | O |  | ContextMenuInspector |
| `xkxk.frameworks.ui.shell/Diagnostics/Remote/MenuHeaderText.cs` | Diag |  | 진단 전용 폴더 | O |  | MenuHeaderText |
| `xkxk.frameworks.ui.shell/Diagnostics/Remote/RemoteBackgroundCommands.cs` | Diag |  | 진단 전용 폴더 | O |  | RemoteBackgroundCommands |
| `xkxk.frameworks.ui.shell/Diagnostics/Remote/RemoteBackgroundSelfTest.cs` | Diag |  | 진단 전용 폴더 |  |  | RemoteBackgroundSelfTest |
| `xkxk.frameworks.ui.shell/Diagnostics/Remote/RemoteBusyCommands.Grid.cs` | Diag |  | 진단 전용 폴더 | O |  | RemoteBusyCommands |
| `xkxk.frameworks.ui.shell/Diagnostics/Remote/RemoteBusyCommands.cs` | Diag |  | 진단 전용 폴더 | O |  | RemoteBusyCommands |
| `xkxk.frameworks.ui.shell/Diagnostics/Remote/RemoteBusySelfTest.cs` | Diag |  | 진단 전용 폴더 |  |  | RemoteBusySelfTest |
| `xkxk.frameworks.ui.shell/Diagnostics/Remote/RemoteChromeCommands.cs` | Diag |  | 진단 전용 폴더 | O |  | RemoteChromeCommands |
| `xkxk.frameworks.ui.shell/Diagnostics/Remote/RemoteCommandRouter.cs` | Diag |  | 진단 전용 폴더 | O |  |  |
| `xkxk.frameworks.ui.shell/Diagnostics/Remote/RemoteCommands.cs` | Diag |  | 진단 전용 폴더 |  |  |  |
| `xkxk.frameworks.ui.shell/Diagnostics/Remote/RemoteControlServer.Routes.cs` | Diag |  | 진단 전용 폴더 |  |  |  |
| `xkxk.frameworks.ui.shell/Diagnostics/Remote/RemoteControlServer.cs` | Diag |  | 진단 전용 폴더 |  |  |  |
| `xkxk.frameworks.ui.shell/Diagnostics/Remote/RemoteDesignThemeCommands.cs` | Diag |  | 진단 전용 폴더 | O |  | RemoteDesignThemeCommands |
| `xkxk.frameworks.ui.shell/Diagnostics/Remote/RemoteDockAccess.cs` | Diag |  | 진단 전용 폴더 | O | O | RemoteDockAccess |
| `xkxk.frameworks.ui.shell/Diagnostics/Remote/RemoteDockAsDocumentCommands.cs` | Diag |  | 진단 전용 폴더 |  | O | RemoteDockAsDocumentCommands |
| `xkxk.frameworks.ui.shell/Diagnostics/Remote/RemoteDockMenuCommands.cs` | Diag |  | 진단 전용 폴더 | O | O | RemoteDockMenuCommands |
| `xkxk.frameworks.ui.shell/Diagnostics/Remote/RemoteDockMenuInvokeCommands.cs` | Diag |  | 진단 전용 폴더 | O | O | RemoteDockMenuInvokeCommands |
| `xkxk.frameworks.ui.shell/Diagnostics/Remote/RemoteDockMenuOpenCommands.cs` | Diag |  | 진단 전용 폴더 | O | O | RemoteDockMenuOpenCommands |
| `xkxk.frameworks.ui.shell/Diagnostics/Remote/RemoteDockTraceCommands.cs` | Diag |  | 진단 전용 폴더 | O | O | RemoteDockTraceCommands |
| `xkxk.frameworks.ui.shell/Diagnostics/Remote/RemoteEffectPerfCommands.cs` | Diag |  | 진단 전용 폴더 |  |  | RemoteEffectPerfCommands |
| `xkxk.frameworks.ui.shell/Diagnostics/Remote/RemoteHttp.cs` | Diag |  | 진단 전용 폴더 |  |  |  |
| `xkxk.frameworks.ui.shell/Diagnostics/Remote/RemoteInfoPath.cs` | Diag |  | 진단 전용 폴더 |  |  |  |
| `xkxk.frameworks.ui.shell/Diagnostics/Remote/RemoteLangCommands.cs` | Diag |  | 진단 전용 폴더 | O |  | RemoteLangCommands |
| `xkxk.frameworks.ui.shell/Diagnostics/Remote/RemoteLayoutsCommands.cs` | Diag |  | 진단 전용 폴더 |  |  | RemoteLayoutsCommands |
| `xkxk.frameworks.ui.shell/Diagnostics/Remote/RemoteLoadSelfTest.cs` | Diag |  | 진단 전용 폴더 | O |  | RemoteLoadSelfTest |
| `xkxk.frameworks.ui.shell/Diagnostics/Remote/RemoteMenuIconCommands.cs` | Diag |  | 진단 전용 폴더 | O |  | RemoteMenuIconCommands |
| `xkxk.frameworks.ui.shell/Diagnostics/Remote/RemoteModalCommands.cs` | Diag |  | 진단 전용 폴더 | O |  |  |
| `xkxk.frameworks.ui.shell/Diagnostics/Remote/RemoteMonitorCommands.cs` | Diag |  | 진단 전용 폴더 | O |  | RemoteMonitorCommands |
| `xkxk.frameworks.ui.shell/Diagnostics/Remote/RemoteNotifyCommands.cs` | Diag |  | 진단 전용 폴더 |  |  | RemoteNotifyCommands |
| `xkxk.frameworks.ui.shell/Diagnostics/Remote/RemotePanelCornerCommands.cs` | Diag |  | 진단 전용 폴더 | O | O | RemotePanelCornerCommands |
| `xkxk.frameworks.ui.shell/Diagnostics/Remote/RemotePanelsCommands.cs` | Diag |  | 진단 전용 폴더 | O | O | RemotePanelsCommands |
| `xkxk.frameworks.ui.shell/Diagnostics/Remote/RemotePlacementCommands.Groups.cs` | Diag |  | 진단 전용 폴더 | O | O | RemotePlacementCommands |
| `xkxk.frameworks.ui.shell/Diagnostics/Remote/RemotePlacementCommands.Ratio.cs` | Diag |  | 진단 전용 폴더 | O |  | RemotePlacementCommands |
| `xkxk.frameworks.ui.shell/Diagnostics/Remote/RemotePlacementCommands.Sdi.cs` | Diag |  | 진단 전용 폴더 | O | O | RemotePlacementCommands |
| `xkxk.frameworks.ui.shell/Diagnostics/Remote/RemotePlacementCommands.cs` | Diag |  | 진단 전용 폴더 | O | O | RemotePlacementCommands |
| `xkxk.frameworks.ui.shell/Diagnostics/Remote/RemotePointerInputCommands.cs` | Diag |  | 진단 전용 폴더 | O |  | RemotePointerInputCommands |
| `xkxk.frameworks.ui.shell/Diagnostics/Remote/RemoteProbeCommands.cs` | Diag |  | 진단 전용 폴더 | O | O | RemoteProbeCommands |
| `xkxk.frameworks.ui.shell/Diagnostics/Remote/RemoteProductStubs.cs` | Diag |  | 진단 전용 폴더 |  |  |  |
| `xkxk.frameworks.ui.shell/Diagnostics/Remote/RemoteProtocol.cs` | Diag |  | 진단 전용 폴더 |  |  |  |
| `xkxk.frameworks.ui.shell/Diagnostics/Remote/RemoteRenderOverlayCommands.cs` | Diag |  | 진단 전용 폴더 | O |  | RemoteRenderOverlayCommands |
| `xkxk.frameworks.ui.shell/Diagnostics/Remote/RemoteRenderProbeCommands.cs` | Diag |  | 진단 전용 폴더 | O | O | RemoteRenderProbeCommands |
| `xkxk.frameworks.ui.shell/Diagnostics/Remote/RemoteScrollCommands.cs` | Diag |  | 진단 전용 폴더 | O | O | RemoteScrollCommands |
| `xkxk.frameworks.ui.shell/Diagnostics/Remote/RemoteSdiShadowCommands.cs` | Diag |  | 진단 전용 폴더 | O |  | RemoteSdiShadowCommands |
| `xkxk.frameworks.ui.shell/Diagnostics/Remote/RemoteSdiZ.cs` | Diag |  | 진단 전용 폴더 | O |  | RemoteSdiZ |
| `xkxk.frameworks.ui.shell/Diagnostics/Remote/RemoteSettingsCommands.cs` | Diag |  | 진단 전용 폴더 | O |  | RemoteSettingsCommands |
| `xkxk.frameworks.ui.shell/Diagnostics/Remote/RemoteShellAccess.cs` | Diag |  | 진단 전용 폴더 | O | O | RemoteShellAccess |
| `xkxk.frameworks.ui.shell/Diagnostics/Remote/RemoteSimActiveCommands.cs` | Diag |  | 진단 전용 폴더 | O | O | RemoteSimActiveCommands |
| `xkxk.frameworks.ui.shell/Diagnostics/Remote/RemoteSnapAssistCommands.cs` | Diag |  | 진단 전용 폴더 |  |  | RemoteSnapAssistCommands |
| `xkxk.frameworks.ui.shell/Diagnostics/Remote/RemoteSnapCommands.cs` | Diag |  | 진단 전용 폴더 | O |  | RemoteSnapCommands |
| `xkxk.frameworks.ui.shell/Diagnostics/Remote/RemoteSplitCommands.cs` | Diag |  | 진단 전용 폴더 | O | O | RemoteSplitCommands |
| `xkxk.frameworks.ui.shell/Diagnostics/Remote/RemoteSystemCommands.cs` | Diag |  | 진단 전용 폴더 | O | O | RemoteSystemCommands |
| `xkxk.frameworks.ui.shell/Diagnostics/Remote/RemoteTextRenderCommands.cs` | Diag |  | 진단 전용 폴더 | O |  | RemoteTextRenderCommands |
| `xkxk.frameworks.ui.shell/Diagnostics/Remote/RemoteThemeDesignCommands.cs` | Diag |  | 진단 전용 폴더 | O |  | RemoteThemeDesignCommands |
| `xkxk.frameworks.ui.shell/Diagnostics/Remote/RemoteTitleMenuCommands.cs` | Diag |  | 진단 전용 폴더 | O | O | RemoteTitleMenuCommands |
| `xkxk.frameworks.ui.shell/Diagnostics/Remote/RemoteTourCatalogCommands.cs` | Diag |  | 진단 전용 폴더 | O |  | RemoteTourCatalogCommands |
| `xkxk.frameworks.ui.shell/Diagnostics/Remote/RemoteTourCommands.cs` | Diag |  | 진단 전용 폴더 | O |  | RemoteTourCommands |
| `xkxk.frameworks.ui.shell/Diagnostics/Remote/RemoteTrayCommands.cs` | Diag |  | 진단 전용 폴더 | O | O | RemoteTrayCommands |
| `xkxk.frameworks.ui.shell/Diagnostics/Remote/RemoteUiCommands.cs` | Diag |  | 진단 전용 폴더 | O |  | RemoteUiCommands |
| `xkxk.frameworks.ui.shell/Diagnostics/Remote/RemoteUiModeCommands.cs` | Diag |  | 진단 전용 폴더 | O |  |  |
| `xkxk.frameworks.ui.shell/Diagnostics/Remote/RemoteUiTargets.cs` | Diag |  | 진단 전용 폴더 | O |  | RemoteUiTargets |
| `xkxk.frameworks.ui.shell/Diagnostics/Remote/RemoteWheelCommands.cs` | Diag |  | 진단 전용 폴더 | O |  | RemoteWheelCommands |
| `xkxk.frameworks.ui.shell/Diagnostics/Remote/RemoteWindowMenuCommands.cs` | Diag |  | 진단 전용 폴더 | O |  | RemoteWindowMenuCommands |
| `xkxk.frameworks.ui.shell/Diagnostics/Remote/RemoteWindowsCommands.MoveLight.cs` | Diag |  | 진단 전용 폴더 | O |  | RemoteWindowsCommands, MdiReleaseTrace |
| `xkxk.frameworks.ui.shell/Diagnostics/Remote/RemoteWindowsCommands.cs` | Diag |  | 진단 전용 폴더 | O | O | RemoteWindowsCommands |
| `xkxk.frameworks.ui.shell/Diagnostics/Remote/VisualTreeInspector.cs` | Diag |  | 진단 전용 폴더 | O |  |  |
| `xkxk.frameworks.ui.shell/Diagnostics/ThemeCensusRunner.cs` | Diag |  | 진단 전용 폴더 | O |  | ThemeCensusRunner |
| `xkxk.frameworks.ui.shell/Docking/ActivationOrigin.cs` | Data | xkxk.frameworks.ui.data/Layout/ActivationOrigin.cs | Avalonia 무참조, 참조 형식도 전부 Data |  |  |  |
| `xkxk.frameworks.ui.shell/Docking/CaptionLayout.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `xkxk.frameworks.ui.shell/Docking/CenterDropTitleAlign.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O | CenterDropTitleAlign |
| `xkxk.frameworks.ui.shell/Docking/CenterDropToMdi.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O | CenterDropToMdi |
| `xkxk.frameworks.ui.shell/Docking/ChromeApplyDiag.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  | ChromeApplyDiag |
| `xkxk.frameworks.ui.shell/Docking/ChromeInactiveDim.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(WindowChromeStyle.Design.cs) |  |  |  |
| `xkxk.frameworks.ui.shell/Docking/ChromePlatform.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(ChromeStyleKind.cs) |  |  |  |
| `xkxk.frameworks.ui.shell/Docking/ChromeStyleKind.cs` | Control |  | 창 크롬 상태 열거. 제어층 어휘 |  |  |  |
| `xkxk.frameworks.ui.shell/Docking/DockFactoryDiag.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(App.cs) |  | O |  |
| `xkxk.frameworks.ui.shell/Docking/DockLayoutBuilder.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(DockFactoryDiag.cs) |  | O | DockLayoutBuilder |
| `xkxk.frameworks.ui.shell/Docking/DockLayoutState.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(PanelArea.cs) |  | O | DockLayoutState |
| `xkxk.frameworks.ui.shell/Docking/DocumentAutoTitles.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(LayoutSnapshot.cs) |  |  | DocumentAutoTitles |
| `xkxk.frameworks.ui.shell/Docking/DocumentChromeService.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O | DocumentChromeService |
| `xkxk.frameworks.ui.shell/Docking/DocumentChromeState.cs` | Control |  | 창 크롬 상태. 제어층 어휘 |  |  |  |
| `xkxk.frameworks.ui.shell/Docking/DocumentLifecycle.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O | DocumentLifecycle |
| `xkxk.frameworks.ui.shell/Docking/DocumentSplitCollapse.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(PanelDocumentDock.cs) |  | O | DocumentSplitCollapse |
| `xkxk.frameworks.ui.shell/Docking/DocumentSplitMode.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(PanelDocumentDock.cs) |  | O | DocumentSplitMode |
| `xkxk.frameworks.ui.shell/Docking/DocumentTabMenu.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O | DocumentTabMenu |
| `xkxk.frameworks.ui.shell/Docking/DocumentZoom.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  | DocumentZoom |
| `xkxk.frameworks.ui.shell/Docking/IContentLoadReporter.cs` | Data | xkxk.frameworks.ui.data/Layout/IContentLoadReporter.cs | Avalonia 무참조, 참조 형식도 전부 Data |  |  |  |
| `xkxk.frameworks.ui.shell/Docking/IDocumentViewFactory.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `xkxk.frameworks.ui.shell/Docking/IDocumentZoomHost.cs` | Control |  | 문서 래퍼 표식 계약. 데이터가 아니라 화면 경계를 찾는 제어 어휘 |  |  |  |
| `xkxk.frameworks.ui.shell/Docking/ILayoutStore.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(Loc.cs) |  | O |  |
| `xkxk.frameworks.ui.shell/Docking/IMonitorProvider.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `xkxk.frameworks.ui.shell/Docking/IPressTimeSource.cs` | Control |  | 입력 시각 추상화. 제어층 어휘 |  |  |  |
| `xkxk.frameworks.ui.shell/Docking/IWindowHostCommands.cs` | Control |  | 창 명령 인터페이스. 제어층 어휘 |  | O |  |
| `xkxk.frameworks.ui.shell/Docking/LayoutAutoSaver.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O |  |
| `xkxk.frameworks.ui.shell/Docking/LayoutBackupService.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(BusyHooks.Release.cs) |  |  | LayoutBackupService |
| `xkxk.frameworks.ui.shell/Docking/LayoutGroupCodec.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(LayoutSnapshot.cs) |  | O |  |
| `xkxk.frameworks.ui.shell/Docking/LayoutLibrary.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(BusyHooks.Release.cs) |  |  | LayoutIndexJsonContext |
| `xkxk.frameworks.ui.shell/Docking/LayoutModeGate.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O |  |
| `xkxk.frameworks.ui.shell/Docking/LayoutRestorer.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O | LayoutRestorer |
| `xkxk.frameworks.ui.shell/Docking/LayoutSerializer.cs` | Control |  | 직렬화지만 Data 로 내리지 않았다: Avalonia·Dock.Avalonia·PocDockFactory 를 참조한다. 옮기려면 별도 리팩터링이 필요하다 | O | O |  |
| `xkxk.frameworks.ui.shell/Docking/LayoutSnapshot.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O | LayoutJsonContext |
| `xkxk.frameworks.ui.shell/Docking/LayoutTemplates.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(ILayoutStore.cs) |  |  |  |
| `xkxk.frameworks.ui.shell/Docking/MagnetDriver.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O |  |
| `xkxk.frameworks.ui.shell/Docking/MagnetMdiLayoutManager.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O |  |
| `xkxk.frameworks.ui.shell/Docking/MagnetSnap.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `xkxk.frameworks.ui.shell/Docking/ManagedLayerFirst.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `xkxk.frameworks.ui.shell/Docking/ManagedSnapDrag.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O |  |
| `xkxk.frameworks.ui.shell/Docking/ManagedWindowHostCommands.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O |  |
| `xkxk.frameworks.ui.shell/Docking/MdiActiveSync.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O | MdiActiveSync |
| `xkxk.frameworks.ui.shell/Docking/MdiDragLimit.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `xkxk.frameworks.ui.shell/Docking/MdiGrabZoneCache.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  | MdiGrabZoneCache, MdiGrabZoneDiag |
| `xkxk.frameworks.ui.shell/Docking/MdiGrabZoneReader.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  | MdiGrabZoneReader |
| `xkxk.frameworks.ui.shell/Docking/MdiMoveLight.RestoreSpread.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(MdiMoveLight.cs) |  |  |  |
| `xkxk.frameworks.ui.shell/Docking/MdiMoveLight.Shadow.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(MdiMoveLight.RestoreSpread.cs) |  |  |  |
| `xkxk.frameworks.ui.shell/Docking/MdiMoveLight.Spread.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(MdiMoveLight.RestoreSpread.cs) |  |  |  |
| `xkxk.frameworks.ui.shell/Docking/MdiMoveLight.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `xkxk.frameworks.ui.shell/Docking/MdiMovePointerCoalesce.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  | MdiMovePointerCoalesce |
| `xkxk.frameworks.ui.shell/Docking/MdiSnapDrag.Timing.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O |  |
| `xkxk.frameworks.ui.shell/Docking/MdiSnapDrag.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O |  |
| `xkxk.frameworks.ui.shell/Docking/MdiStripFold.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  | MdiStripFold |
| `xkxk.frameworks.ui.shell/Docking/MdiTabGroup.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(RemoteCommandRouter.cs) |  | O | MdiTabGroupDiag |
| `xkxk.frameworks.ui.shell/Docking/MdiTabJoinDiag.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O | MdiTabJoinDiag |
| `xkxk.frameworks.ui.shell/Docking/MdiTabJoinDrag.Near.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `xkxk.frameworks.ui.shell/Docking/MdiTabJoinDrag.Timing.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `xkxk.frameworks.ui.shell/Docking/MdiTabJoinDrag.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O |  |
| `xkxk.frameworks.ui.shell/Docking/MdiTileMinSize.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(MdiTabGroup.cs) |  | O |  |
| `xkxk.frameworks.ui.shell/Docking/MdiVisibilityGuard.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O |  |
| `xkxk.frameworks.ui.shell/Docking/MdiZOrder.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O | MdiZOrder |
| `xkxk.frameworks.ui.shell/Docking/PanelArea.cs` | Control |  | 패널 영역 어휘. 제어층 |  |  |  |
| `xkxk.frameworks.ui.shell/Docking/PanelAreaNames.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(PanelArea.cs) |  |  |  |
| `xkxk.frameworks.ui.shell/Docking/PanelCornerBuild.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O | PanelCornerBuild |
| `xkxk.frameworks.ui.shell/Docking/PanelCornerMinSize.cs` | Control |  | Avalonia 무참조지만 저장 데이터가 아니라 패널 끝 확장의 제어 계산이다. PanelCornerBuild(Control)만 쓴다 |  |  | PanelCornerMinSize |
| `xkxk.frameworks.ui.shell/Docking/PanelCornerOwnership.cs` | Control |  | 모서리 소유 상태. 판정기 고정 경로(AC-3-06) |  |  |  |
| `xkxk.frameworks.ui.shell/Docking/PanelCornerRules.cs` | Control |  | 모서리 규칙 논리. 판정기 고정 경로(AC-3-01·3-05) |  |  |  |
| `xkxk.frameworks.ui.shell/Docking/PanelCornerService.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O |  |
| `xkxk.frameworks.ui.shell/Docking/PanelDocumentDock.cs` | Control |  | Dock 모델 파생(트리 구성 요소). 저장 DTO 가 아니다 |  | O |  |
| `xkxk.frameworks.ui.shell/Docking/PanelDropService.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O | PanelDropService |
| `xkxk.frameworks.ui.shell/Docking/PanelEdge.cs` | Control |  | 패널 변 어휘. 제어층 |  |  |  |
| `xkxk.frameworks.ui.shell/Docking/PanelEdgeNames.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(PanelEdge.cs) |  |  |  |
| `xkxk.frameworks.ui.shell/Docking/PanelFeatureGate.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(PocDockFactory.TabGroups.cs) |  |  |  |
| `xkxk.frameworks.ui.shell/Docking/PanelFocusActivation.cs` | Control |  | 포커스 전이 논리 |  | O | PanelFocusActivation |
| `xkxk.frameworks.ui.shell/Docking/PanelHideService.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(DockLayoutState.cs) |  | O | PanelHideService |
| `xkxk.frameworks.ui.shell/Docking/PanelJoinDiag.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O | PanelJoinDiag |
| `xkxk.frameworks.ui.shell/Docking/PanelJoinDrag.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O |  |
| `xkxk.frameworks.ui.shell/Docking/PanelJoinMoveCandidates.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  | PanelJoinMoveCandidates |
| `xkxk.frameworks.ui.shell/Docking/PanelJoinTargets.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O | PanelJoinTargets |
| `xkxk.frameworks.ui.shell/Docking/PanelMoveService.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(DockLayoutState.cs) |  | O | PanelMoveService |
| `xkxk.frameworks.ui.shell/Docking/PanelOpenStates.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O | PanelOpenStates |
| `xkxk.frameworks.ui.shell/Docking/PanelPinShares.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(PocDockFactory.TabGroups.cs) |  | O | PinShare, PanelPinShares |
| `xkxk.frameworks.ui.shell/Docking/PanelPositionOps.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(PanelArea.cs) |  | O |  |
| `xkxk.frameworks.ui.shell/Docking/PanelQuery.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(DockLayoutState.cs) |  | O | PanelQuery |
| `xkxk.frameworks.ui.shell/Docking/PanelTabStripDrop.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O |  |
| `xkxk.frameworks.ui.shell/Docking/PanelTabsService.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O | PanelTabsService |
| `xkxk.frameworks.ui.shell/Docking/PlacementRecovery.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O |  |
| `xkxk.frameworks.ui.shell/Docking/PocDockFactory.TabGroups.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O |  |
| `xkxk.frameworks.ui.shell/Docking/PocDockFactory.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O |  |
| `xkxk.frameworks.ui.shell/Docking/PocDocument.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(ILayoutStore.cs) |  | O |  |
| `xkxk.frameworks.ui.shell/Docking/RestoreViewDeferral.Idle.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `xkxk.frameworks.ui.shell/Docking/RestoreViewDeferral.Load.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(PerfProbe.Counters.cs) |  |  |  |
| `xkxk.frameworks.ui.shell/Docking/RestoreViewDeferral.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O |  |
| `xkxk.frameworks.ui.shell/Docking/SdiPlacement.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O | SdiPlacement |
| `xkxk.frameworks.ui.shell/Docking/SdiRedockPolicy.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `xkxk.frameworks.ui.shell/Docking/SdiResetLayout.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `xkxk.frameworks.ui.shell/Docking/SdiScreenPlacement.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O | SdiScreenPlacement |
| `xkxk.frameworks.ui.shell/Docking/SidePanelVisibility.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(PerfProbe.Counters.cs) |  |  | SidePanelVisibility |
| `xkxk.frameworks.ui.shell/Docking/SnapAssistCandidates.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O | SnapAssistCandidateList, SnapAssistCandidates |
| `xkxk.frameworks.ui.shell/Docking/SnapAssistHost.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `xkxk.frameworks.ui.shell/Docking/SnapAssistSession.Diag.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(SnapAssistSession.Flow.cs) |  |  |  |
| `xkxk.frameworks.ui.shell/Docking/SnapAssistSession.Flow.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `xkxk.frameworks.ui.shell/Docking/SnapAssistSession.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `xkxk.frameworks.ui.shell/Docking/SnapFlyoutState.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(SnapLayoutCatalog.cs) |  |  |  |
| `xkxk.frameworks.ui.shell/Docking/SnapKeyboardCommands.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(SnapLayoutCatalog.cs) |  |  |  |
| `xkxk.frameworks.ui.shell/Docking/SnapLayoutCatalog.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `xkxk.frameworks.ui.shell/Docking/SnapZoneResolver.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `xkxk.frameworks.ui.shell/Docking/SplitAreaCleanup.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(DockLayoutState.cs) |  | O | SplitAreaCleanup |
| `xkxk.frameworks.ui.shell/Docking/SplitAreaService.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(DockLayoutState.cs) |  | O | SplitAreaService |
| `xkxk.frameworks.ui.shell/Docking/SplitCarryService.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(DockLayoutState.cs) |  | O | SplitCarryService |
| `xkxk.frameworks.ui.shell/Docking/SplitLayoutCodec.cs` | Control |  | 직렬화지만 Data 로 내리지 않았다: PocDockFactory·LayoutSerializer 를 참조한다 |  | O | SplitLayoutCodec |
| `xkxk.frameworks.ui.shell/Docking/SplitMergeService.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O |  |
| `xkxk.frameworks.ui.shell/Docking/SplitPaneInfo.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(PanelArea.cs) |  | O |  |
| `xkxk.frameworks.ui.shell/Docking/TabJoinRules.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `xkxk.frameworks.ui.shell/Docking/TabSlotMarker.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `xkxk.frameworks.ui.shell/Docking/TaskbarTabDrag.cs` | Control |  | 탭 끌기 상태 제어(PanelJoinDrag 연동). axaml 없음, 진단이 상태를 읽는다 | O |  |  |
| `xkxk.frameworks.ui.shell/Docking/TitleDragRestore.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O |  |
| `xkxk.frameworks.ui.shell/Docking/WindowActivation.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O | WindowActivation |
| `xkxk.frameworks.ui.shell/Docking/WindowChromeDiag.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  | WindowChromeDiag |
| `xkxk.frameworks.ui.shell/Docking/WindowChromeStyle.Design.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  | WindowChromeStyle |
| `xkxk.frameworks.ui.shell/Docking/WindowChromeStyle.Inactive.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  | WindowChromeStyle |
| `xkxk.frameworks.ui.shell/Docking/WindowChromeStyle.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  | WindowChromeStyle |
| `xkxk.frameworks.ui.shell/Docking/WindowCommands.Origin.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(PocDocument.cs) |  |  |  |
| `xkxk.frameworks.ui.shell/Docking/WindowCommands.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O |  |
| `xkxk.frameworks.ui.shell/Docking/WindowCommandsPartAttribute.cs` | Control |  | WindowCommands 표식 특성 |  |  | WindowCommandsPartAttribute |
| `xkxk.frameworks.ui.shell/Docking/WindowLoadState.Query.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(PocDocument.cs) |  |  |  |
| `xkxk.frameworks.ui.shell/Docking/WindowLoadState.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `xkxk.frameworks.ui.shell/Docking/WindowMenuOps.Snap.Menu.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O |  |
| `xkxk.frameworks.ui.shell/Docking/WindowMenuOps.Snap.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O | SnapRestoreMemory, ManagedWindowRect |
| `xkxk.frameworks.ui.shell/Docking/WindowMenuOps.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O |  |
| `xkxk.frameworks.ui.shell/Docking/WindowMinSize.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O |  |
| `xkxk.frameworks.ui.shell/Docking/WindowSession.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(ILayoutStore.cs) |  | O | WindowSession |
| `xkxk.frameworks.ui.shell/Docking/WindowStateOps.Windows.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(MdiDragProbe.cs) |  | O | WindowStateOps |
| `xkxk.frameworks.ui.shell/Docking/WindowStateOps.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O | WindowStateOps |
| `xkxk.frameworks.ui.shell/Docking/WindowTabJoinService.DockGuard.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O |  |
| `xkxk.frameworks.ui.shell/Docking/WindowTabJoinService.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O |  |
| `xkxk.frameworks.ui.shell/IViewRegistry.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `xkxk.frameworks.ui.shell/Input/PinchZoom.cs` | Control |  | SettingsInput 이 부르는 입력 처리. 진단이 직접 쓴다 | O |  |  |
| `xkxk.frameworks.ui.shell/Input/SettingsInput.cs` | Control |  | 전역 입력 처리기(파일 주석상 컨트롤 계층). App·진단이 직접 부른다 | O |  |  |
| `xkxk.frameworks.ui.shell/Input/TouchToolTipGuard.cs` | Control |  | PinchZoom 이 부르는 입력 처리 | O |  |  |
| `xkxk.frameworks.ui.shell/Lifecycle/ShellExitSave.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `xkxk.frameworks.ui.shell/Lifecycle/ShellStartup.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `xkxk.frameworks.ui.shell/Localization/DockStrings.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `xkxk.frameworks.ui.shell/Localization/Loc.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `xkxk.frameworks.ui.shell/Localization/MenuLabel.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(Loc.cs) |  |  |  |
| `xkxk.frameworks.ui.shell/Localization/TrExtension.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `xkxk.frameworks.ui.shell/Platform/BackDismissal.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `xkxk.frameworks.ui.shell/Platform/DeviceTraits.cs` | Data | xkxk.frameworks.ui.data/Platform/DeviceTraits.cs | Avalonia 무참조, 참조 형식도 전부 Data |  |  |  |
| `xkxk.frameworks.ui.shell/Platform/FittedFlyout.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `xkxk.frameworks.ui.shell/Platform/InputFormFactor.cs` | Control |  | 폼팩터 판정 논리. 데이터가 아니고 화면도 아니다 |  |  |  |
| `xkxk.frameworks.ui.shell/Platform/MenuIconBitmap.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `xkxk.frameworks.ui.shell/Platform/ModalScope.Gate.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `xkxk.frameworks.ui.shell/Platform/ModalScope.Leaks.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `xkxk.frameworks.ui.shell/Platform/ModalScope.RawGate.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `xkxk.frameworks.ui.shell/Platform/ModalScope.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `xkxk.frameworks.ui.shell/Platform/ShellPlatform.cs` | Control |  | 트레이·플랫폼 서비스. axaml 없음. 호스트 2곳·진단 2곳이 직접 쓴다 | O |  |  |
| `xkxk.frameworks.ui.shell/Platform/SystemDialogHost.cs` | Control |  | 창 수명 서비스(내용은 Control 로 받음, 화면 형식 무참조). 도킹·호스트·진단이 BackgroundMode 를 읽는다 | O |  |  |
| `xkxk.frameworks.ui.shell/Platform/TrayMenuModel.cs` | Control |  | 트레이 메뉴 뷰모델. ShellPlatform 과 한 묶음 | O |  |  |
| `xkxk.frameworks.ui.shell/Platform/UiDeferred.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `xkxk.frameworks.ui.shell/Rendering/RenderInfo.cs` | Control |  | Avalonia 내부를 리플렉션으로 읽는다. 문자열 Avalonia 때문에 Data 규칙도 위반한다 | O |  |  |
| `xkxk.frameworks.ui.shell/Theming/BuiltinThemes.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(Loc.cs) |  |  |  |
| `xkxk.frameworks.ui.shell/Theming/CellFontDiag.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  | CellFontDiag |
| `xkxk.frameworks.ui.shell/Theming/DepthProbeHost.cs` | Data | xkxk.frameworks.ui.data/Theming/DepthProbeHost.cs | Avalonia 무참조, 참조 형식도 전부 Data |  |  |  |
| `xkxk.frameworks.ui.shell/Theming/DesignEffectsApplier.Brushes.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `xkxk.frameworks.ui.shell/Theming/DesignEffectsApplier.Image.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `xkxk.frameworks.ui.shell/Theming/DesignEffectsApplier.ImageTiming.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `xkxk.frameworks.ui.shell/Theming/DesignEffectsApplier.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `xkxk.frameworks.ui.shell/Theming/DesignThemeDefinition.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(Loc.cs) |  |  |  |
| `xkxk.frameworks.ui.shell/Theming/DesignThemeResolver.Elements.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(ThemeDefinition.cs) |  |  |  |
| `xkxk.frameworks.ui.shell/Theming/DesignThemeResolver.Motion.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(DesignThemeDefinition.cs) |  |  |  |
| `xkxk.frameworks.ui.shell/Theming/DesignThemeResolver.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(DesignThemeDefinition.cs) |  |  |  |
| `xkxk.frameworks.ui.shell/Theming/DesignThemes.Levels.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(DesignThemeDefinition.cs) |  |  |  |
| `xkxk.frameworks.ui.shell/Theming/DesignThemes.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(BuiltinThemes.cs) |  |  |  |
| `xkxk.frameworks.ui.shell/Theming/FluentDynamicSync.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  | FluentDynamicSync |
| `xkxk.frameworks.ui.shell/Theming/MotionEffects.cs` | Data | xkxk.frameworks.ui.data/Theming/MotionEffects.cs | Avalonia 무참조, 참조 형식도 전부 Data |  |  |  |
| `xkxk.frameworks.ui.shell/Theming/PanelFeatureRules.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(UiStyleRules.cs) |  |  |  |
| `xkxk.frameworks.ui.shell/Theming/SingleWindowPlatformFix.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `xkxk.frameworks.ui.shell/Theming/ThemeApplier.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `xkxk.frameworks.ui.shell/Theming/ThemeApplierDiag.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  | ThemeApplierDiag |
| `xkxk.frameworks.ui.shell/Theming/ThemeAutomation.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(DesignThemes.Levels.cs) |  |  |  |
| `xkxk.frameworks.ui.shell/Theming/ThemeCatalog.Custom.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(Loc.cs) |  |  |  |
| `xkxk.frameworks.ui.shell/Theming/ThemeCatalog.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `xkxk.frameworks.ui.shell/Theming/ThemeDefinition.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  | ThemeJsonContext |
| `xkxk.frameworks.ui.shell/Theming/ThemeDesignApplier.Diag.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `xkxk.frameworks.ui.shell/Theming/ThemeDesignApplier.Switch.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `xkxk.frameworks.ui.shell/Theming/ThemeDesignApplier.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `xkxk.frameworks.ui.shell/Theming/ThemeDesignStyles.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  | ThemeDesignStyles |
| `xkxk.frameworks.ui.shell/Theming/ThemeElements.Targets.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(ThemeElements.cs) |  |  |  |
| `xkxk.frameworks.ui.shell/Theming/ThemeElements.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `xkxk.frameworks.ui.shell/Theming/UiLanguageSettings.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  | UiLanguageSettings |
| `xkxk.frameworks.ui.shell/Theming/UiSettings.Parts.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(ChromeInactiveDim.cs) |  |  |  |
| `xkxk.frameworks.ui.shell/Theming/UiSettings.Shell.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(MdiMoveLight.Shadow.cs) |  |  |  |
| `xkxk.frameworks.ui.shell/Theming/UiSettings.Snap.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `xkxk.frameworks.ui.shell/Theming/UiSettings.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `xkxk.frameworks.ui.shell/Theming/UiSettingsApplier.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  | UiSettingsApplier |
| `xkxk.frameworks.ui.shell/Theming/UiSettingsDto.Motion.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(UiSettingsDto.cs) |  |  |  |
| `xkxk.frameworks.ui.shell/Theming/UiSettingsDto.cs` | Control |  | 저장 DTO 지만 Data 로 내리지 않았다: UiSettings 기본값과 제어층 어휘(ChromeStyleKind·UiStyle)를 참조한다. 옮기려면 별도 리팩터링이 필요하다 |  |  | UiSettingsJsonContext |
| `xkxk.frameworks.ui.shell/Theming/UiSettingsStore.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  | UiSettingsStore |
| `xkxk.frameworks.ui.shell/Theming/UiStyleRules.cs` | Control |  | UI 형 판정 논리 |  |  |  |
| `xkxk.frameworks.ui.shell/Theming/UiSystemSettings.Args.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(UiSettings.Parts.cs) |  |  | UiSystemSettings |
| `xkxk.frameworks.ui.shell/Theming/UiSystemSettings.Grid.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(UiSettingsDto.Motion.cs) |  |  | UiSystemSettings |
| `xkxk.frameworks.ui.shell/Theming/UiSystemSettings.Mdi.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(UiSettingsDto.Motion.cs) |  |  | UiSystemSettings |
| `xkxk.frameworks.ui.shell/Theming/UiSystemSettings.Restore.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(UiSettingsDto.Motion.cs) |  |  | UiSystemSettings |
| `xkxk.frameworks.ui.shell/Theming/UiSystemSettings.Spread.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(MdiMoveLight.RestoreSpread.cs) |  |  | UiSystemSettings |
| `xkxk.frameworks.ui.shell/Theming/UiSystemSettings.Timing.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(ChromeInactiveDim.cs) |  |  | UiSystemSettings |
| `xkxk.frameworks.ui.shell/Theming/UiSystemSettings.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(PanelFeatureRules.cs) |  |  | UiSystemSettings |
| `xkxk.frameworks.ui.shell/Theming/UiThemeSettings.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  | UiThemeSettings |
| `xkxk.frameworks.ui.shell/Theming/UiWindowSettings.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  | UiWindowSettings |
| `xkxk.frameworks.ui.shell/Tour/ShortcutCatalog.cs` | Control |  | 단축키 표. 판정기 고정 경로(AC-6-05·6-09) |  |  |  |
| `xkxk.frameworks.ui.shell/Tour/TourCatalog.Nav.cs` | Control |  | 투어 단계 표. 판정기 고정 경로(AC-6-08) |  |  | TourNavSteps |
| `xkxk.frameworks.ui.shell/Tour/TourCatalog.cs` | Control |  | 투어 단계 표. 판정기 고정 경로(AC-6-01) |  |  |  |
| `xkxk.frameworks.ui.shell/Tour/TourCatalogParser.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(ShortcutCatalog.cs) |  |  | TourCatalogParser |
| `xkxk.frameworks.ui.shell/Tour/TourConditions.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(App.cs) |  |  |  |
| `xkxk.frameworks.ui.shell/Tour/TourController.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `xkxk.frameworks.ui.shell/Tour/TourInputGate.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `xkxk.frameworks.ui.shell/Tour/TourLayerContent.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(Loc.cs) |  |  | TourLayerContent |
| `xkxk.frameworks.ui.shell/Tour/TourScopeResolver.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  | TourScopeResolver |
| `xkxk.frameworks.ui.shell/Tour/TourSkipLog.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(TourStep.cs) |  |  | TourSkipLog |
| `xkxk.frameworks.ui.shell/Tour/TourStep.cs` | Control |  | TourCatalog 의 레코드. 카탈로그와 같은 층 |  |  |  |
| `xkxk.frameworks.ui.shell/Tour/TourTargets.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `xkxk.frameworks.ui.shell/ViewModels/BackgroundFallbackNotifier.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  | BackgroundFallbackNotifier |
| `xkxk.frameworks.ui.shell/ViewModels/CompactModeViewModel.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(PocDocument.cs) |  |  |  |
| `xkxk.frameworks.ui.shell/ViewModels/DesignThemeViewModel.Levels.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(Loc.cs) |  |  |  |
| `xkxk.frameworks.ui.shell/ViewModels/DesignThemeViewModel.Motion.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(Loc.cs) |  |  |  |
| `xkxk.frameworks.ui.shell/ViewModels/DesignThemeViewModel.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `xkxk.frameworks.ui.shell/ViewModels/ExplorerTool.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(MainViewModel.cs) |  | O |  |
| `xkxk.frameworks.ui.shell/ViewModels/FavoriteBarViewModel.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `xkxk.frameworks.ui.shell/ViewModels/LayoutBackupViewModel.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(Loc.cs) |  |  |  |
| `xkxk.frameworks.ui.shell/ViewModels/LayoutManagerViewModel.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(Loc.cs) |  |  |  |
| `xkxk.frameworks.ui.shell/ViewModels/LayoutSaveStatusText.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(Loc.cs) |  |  | LayoutSaveStatusText |
| `xkxk.frameworks.ui.shell/ViewModels/LayoutTemplatePickerViewModel.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(Loc.cs) |  |  |  |
| `xkxk.frameworks.ui.shell/ViewModels/LocalizedOption.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(Loc.cs) |  |  |  |
| `xkxk.frameworks.ui.shell/ViewModels/LogTool.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(MainViewModel.cs) |  | O |  |
| `xkxk.frameworks.ui.shell/ViewModels/MainViewModel.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(App.cs) |  | O |  |
| `xkxk.frameworks.ui.shell/ViewModels/MobileNavViewModel.Selection.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(PocDocument.cs) |  |  |  |
| `xkxk.frameworks.ui.shell/ViewModels/MobileNavViewModel.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(Loc.cs) |  |  |  |
| `xkxk.frameworks.ui.shell/ViewModels/MonitorMenuViewModel.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(IMonitorProvider.cs) |  |  |  |
| `xkxk.frameworks.ui.shell/ViewModels/NotificationPanelViewModel.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(Loc.cs) |  | O |  |
| `xkxk.frameworks.ui.shell/ViewModels/NotifyBadgeViewModel.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O |  |
| `xkxk.frameworks.ui.shell/ViewModels/OpenWindowListViewModel.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(WindowCommands.Origin.cs) |  |  |  |
| `xkxk.frameworks.ui.shell/ViewModels/OpenWindowSlot.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(PocDocument.cs) |  |  |  |
| `xkxk.frameworks.ui.shell/ViewModels/Settings/AppearancePageViewModel.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(BuiltinThemes.cs) |  |  |  |
| `xkxk.frameworks.ui.shell/ViewModels/Settings/ISettingsRootView.cs` | Control |  | 설정 화면 표식 계약 |  |  |  |
| `xkxk.frameworks.ui.shell/ViewModels/Settings/LanguagePageViewModel.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(Loc.cs) |  |  |  |
| `xkxk.frameworks.ui.shell/ViewModels/Settings/LayoutPageViewModel.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(MainViewModel.cs) |  |  |  |
| `xkxk.frameworks.ui.shell/ViewModels/Settings/SettingsInventory.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(Loc.cs) |  |  |  |
| `xkxk.frameworks.ui.shell/ViewModels/Settings/SettingsWindowViewModel.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(Loc.cs) |  |  |  |
| `xkxk.frameworks.ui.shell/ViewModels/Settings/SystemPageViewModel.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(UiSettings.Parts.cs) |  |  |  |
| `xkxk.frameworks.ui.shell/ViewModels/Settings/WindowPageViewModel.Shell.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(MdiMoveLight.RestoreSpread.cs) |  |  |  |
| `xkxk.frameworks.ui.shell/ViewModels/Settings/WindowPageViewModel.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(App.cs) |  |  |  |
| `xkxk.frameworks.ui.shell/ViewModels/SettingsPanelViewModel.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(Loc.cs) |  |  |  |
| `xkxk.frameworks.ui.shell/ViewModels/ShellContext.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(PocDockFactory.TabGroups.cs) |  |  |  |
| `xkxk.frameworks.ui.shell/ViewModels/ShellExtrasViewModel.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `xkxk.frameworks.ui.shell/ViewModels/SystemMenu/AboutViewModel.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `xkxk.frameworks.ui.shell/ViewModels/SystemMenu/AppMonitorViewModel.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `xkxk.frameworks.ui.shell/ViewModels/SystemMenu/InquiryListViewModel.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(Loc.cs) |  |  |  |
| `xkxk.frameworks.ui.shell/ViewModels/SystemMenu/InquiryStore.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(App.cs) |  |  |  |
| `xkxk.frameworks.ui.shell/ViewModels/SystemMenu/InquiryViewModel.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(Loc.cs) |  |  |  |
| `xkxk.frameworks.ui.shell/ViewModels/SystemMenu/LogViewerViewModel.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `xkxk.frameworks.ui.shell/ViewModels/SystemMenu/SystemInfo.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `xkxk.frameworks.ui.shell/ViewModels/SystemMenuViewModel.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(App.cs) |  |  |  |
| `xkxk.frameworks.ui.shell/ViewModels/SystemThemePairViewModel.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(BuiltinThemes.cs) |  |  |  |
| `xkxk.frameworks.ui.shell/ViewModels/ThemeDesignRow.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `xkxk.frameworks.ui.shell/ViewModels/ThemeDesignViewModel.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `xkxk.frameworks.ui.shell/ViewModels/ThemeEditorViewModel.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(Loc.cs) |  |  |  |
| `xkxk.frameworks.ui.shell/ViewModels/ThemeTokenRow.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `xkxk.frameworks.ui.shell/ViewModels/TitleStripViewModel.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O |  |
| `xkxk.frameworks.ui.shell/ViewModels/UiStyleViewModel.Options.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `xkxk.frameworks.ui.shell/ViewModels/UiStyleViewModel.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `xkxk.frameworks.ui.shell/ViewModels/WindowMenuViewModel.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(App.cs) |  |  |  |
| `xkxk.frameworks.ui.shell/ViewRegistry.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(IViewRegistry.cs) |  |  |  |
| `xkxk.frameworks.ui.views/CaptionClickTiming.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  | CaptionClickTiming |
| `xkxk.frameworks.ui.views/ChromeButtons.axaml` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/ChromeButtons.axaml.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/ChromeLogo.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/CrossRootLayoutFlush.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  | CrossRootLayoutFlush |
| `xkxk.frameworks.ui.views/DefaultDocumentViewFactory.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O | O |  |
| `xkxk.frameworks.ui.views/DeferredContentLoader.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O | O | DeferredContentLoader |
| `xkxk.frameworks.ui.views/Depth/DepthPrewarm.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Depth/DepthTransitions.Open.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Depth/DepthTransitions.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O | O |  |
| `xkxk.frameworks.ui.views/Depth/SnapshotBackdrop.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  | BackdropLayer, SnapshotBackdrop |
| `xkxk.frameworks.ui.views/Depth/SnapshotGeometry.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  | SnapshotGeometry |
| `xkxk.frameworks.ui.views/Depth/SnapshotPicture.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  | SnapshotPicture |
| `xkxk.frameworks.ui.views/Depth/SnapshotRenderer.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  | SnapshotRenderer |
| `xkxk.frameworks.ui.views/Depth/WindowOverview3D.Layout.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Depth/WindowOverview3D.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Depth/WindowTransitions.AfterLayout.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Depth/WindowTransitions.Backdrop.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Depth/WindowTransitions.Clip.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Depth/WindowTransitions.Close.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O | O |  |
| `xkxk.frameworks.ui.views/Depth/WindowTransitions.CloseCover.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Depth/WindowTransitions.NavShown.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O | O |  |
| `xkxk.frameworks.ui.views/Depth/WindowTransitions.Probe.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Depth/WindowTransitions.Switch.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Depth/WindowTransitions.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O | O |  |
| `xkxk.frameworks.ui.views/DesignThemePanel.axaml` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/DesignThemePanel.axaml.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Diagnostics/Gallery/ControlGallery.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Diagnostics/Gallery/GalleryView.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Diagnostics/NotificationPanelDiag.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  | NotificationPanelDiag |
| `xkxk.frameworks.ui.views/Diagnostics/PanelPositionMenuDiag.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O | O | PanelPositionMenuDiag |
| `xkxk.frameworks.ui.views/DocumentPrewarm.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/DocumentTabKeepAlive.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O | O |  |
| `xkxk.frameworks.ui.views/DocumentTabMenu.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  | DocumentTabMenu |
| `xkxk.frameworks.ui.views/DocumentTabSwitchProbe.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  | DocumentTabSwitchProbe |
| `xkxk.frameworks.ui.views/DocumentViewLocator.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O | O |  |
| `xkxk.frameworks.ui.views/Documents/Bench/AllocMark.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 |  |  | AllocMark |
| `xkxk.frameworks.ui.views/Documents/Bench/BenchDocumentView.axaml` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Documents/Bench/BenchDocumentView.axaml.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Documents/Bench/BenchFonts.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Documents/Bench/BenchProbe.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Documents/Bench/BenchRemote.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 |  |  |  |
| `xkxk.frameworks.ui.views/Documents/Bench/BenchRow.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 |  |  |  |
| `xkxk.frameworks.ui.views/Documents/Bench/BenchSignal.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 |  |  |  |
| `xkxk.frameworks.ui.views/Documents/Bench/ChartAdapter.LiveCharts.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Documents/Bench/ChartAdapter.OxyPlot.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Documents/Bench/ChartAdapter.ProCharts.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Documents/Bench/ChartAdapter.ScottPlot.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Documents/Bench/ChartAdapters.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Documents/Bench/ChartBenchView.axaml` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Documents/Bench/ChartBenchView.axaml.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Documents/Bench/Dash.Baseline.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Documents/Bench/Dash.LiveCharts.DragGate.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Documents/Bench/Dash.LiveCharts.DragSnapshot.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Documents/Bench/Dash.LiveCharts.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Documents/Bench/Dash.PdTokens.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Documents/Bench/Dash.ScottPlot.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Documents/Bench/Dash.TheOne.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Documents/Bench/DashboardBench.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Documents/Bench/DashboardBenchView.axaml` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Documents/Bench/DashboardBenchView.axaml.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Documents/Bench/DashboardRemote.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 |  |  |  |
| `xkxk.frameworks.ui.views/Documents/Bench/DashboardSpec.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 |  |  |  |
| `xkxk.frameworks.ui.views/Documents/Bench/GridBenchBands.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  | GridBenchBands |
| `xkxk.frameworks.ui.views/Documents/Bench/GridBenchHighlight.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  | GridBenchHighlight |
| `xkxk.frameworks.ui.views/Documents/Bench/GridBenchModel.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Documents/Bench/GridBenchPro.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  | GridBenchPro |
| `xkxk.frameworks.ui.views/Documents/Bench/GridBenchView.axaml` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Documents/Bench/GridBenchView.axaml.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Documents/Bench/HangulFirstComparer.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 |  |  |  |
| `xkxk.frameworks.ui.views/Documents/Bench/IDashboardAdapter.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Documents/BenchEntry.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Documents/FormDocumentView.axaml` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Documents/FormDocumentView.axaml.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Documents/GanttCanvas.Dense.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Documents/GanttCanvas.Render.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Documents/GanttCanvas.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Documents/GanttDocumentView.axaml` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Documents/GanttDocumentView.axaml.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Documents/GridCellStrokes.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  | GridCellStrokes |
| `xkxk.frameworks.ui.views/Documents/GridColumnDrafts.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  | GridColumnDrafts |
| `xkxk.frameworks.ui.views/Documents/GridColumnScaler.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  | GridColumnScaler |
| `xkxk.frameworks.ui.views/Documents/GridDenseTarget.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  | GridDenseTarget |
| `xkxk.frameworks.ui.views/Documents/GridDocumentView.axaml` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Documents/GridDocumentView.axaml.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Documents/GridDocumentViewModel.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Documents/GridLayoutProbe.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  | GridLayoutProbe |
| `xkxk.frameworks.ui.views/Documents/GridOpenBreakdown.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  | GridOpenBreakdown, CellText |
| `xkxk.frameworks.ui.views/Documents/GridRouteProbe.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  | GridRouteProbe |
| `xkxk.frameworks.ui.views/Documents/GridRow.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  | GridSeed |
| `xkxk.frameworks.ui.views/Documents/GridScrollCoalescer.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  | GridScrollCoalescer |
| `xkxk.frameworks.ui.views/Documents/GridScrollProbe.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  | GridScrollProbe |
| `xkxk.frameworks.ui.views/Documents/GridTouchSelection.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  | GridTouchSelection |
| `xkxk.frameworks.ui.views/Documents/PdCellChrome.Text.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  | PdCellChrome, PdCellTextLook |
| `xkxk.frameworks.ui.views/Documents/PdCellChrome.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  | PdCellChrome |
| `xkxk.frameworks.ui.views/Documents/PdCellTemplate.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  | PdCellTemplate |
| `xkxk.frameworks.ui.views/Documents/PdDataGrid.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Documents/PdDataGridStyles.axaml` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Documents/PdDataGridStyles.axaml.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Documents/PdRowTemplate.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  | PdRowTemplate |
| `xkxk.frameworks.ui.views/Documents/PdTextColumn.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/EscCloseInput.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/FavoriteBarView.axaml` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/FavoriteBarView.axaml.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/FavoriteListView.axaml` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/FavoriteListView.axaml.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/GridViewPool.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Input/ShortcutBindings.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Input/TabFocusScope.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Input/WindowKeyGestures.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/LayoutBackupView.axaml` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/LayoutBackupView.axaml.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/LayoutManagerView.axaml` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/LayoutManagerView.axaml.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/MacNativeCaption.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  | MacNativeCaption |
| `xkxk.frameworks.ui.views/MainTitleBar.axaml` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/MainTitleBar.axaml.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/MainView.FavoriteBar.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/MainView.RecyclePrune.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/MainView.TabClose.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/MainView.TitleOrigin.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/MainView.axaml` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/MainView.axaml.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/MdiLockDragGuard.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  | MdiLockDragGuard |
| `xkxk.frameworks.ui.views/MdiMoveHitTest.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  | MdiMoveHitTest |
| `xkxk.frameworks.ui.views/MdiMoveLightener.Shadow.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/MdiMoveLightener.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O | O |  |
| `xkxk.frameworks.ui.views/MdiStaticCache.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O | O | MdiStaticCache |
| `xkxk.frameworks.ui.views/MdiStaticLight.Restore.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  | MdiStaticLight |
| `xkxk.frameworks.ui.views/MdiStaticLight.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O | O | MdiStaticLight |
| `xkxk.frameworks.ui.views/Menus/FileMenu.axaml` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Menus/FileMenu.axaml.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Menus/MenuIcon.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Menus/SnapCellGlyph.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Menus/SystemMenuItem.axaml` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Menus/SystemMenuItem.axaml.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Menus/ViewMenu.axaml` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Menus/ViewMenu.axaml.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Menus/WindowMenu.axaml` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Menus/WindowMenu.axaml.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/MobileNavView.axaml` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/MobileNavView.axaml.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/MoreWindowsView.axaml` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/MoreWindowsView.axaml.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/NarrowToolChrome.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O | O |  |
| `xkxk.frameworks.ui.views/NotificationPanelView.axaml` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/NotificationPanelView.axaml.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/OptionComboWidth.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/PanelCornerChrome.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O | O | PanelCornerChrome |
| `xkxk.frameworks.ui.views/PanelPositionMenu.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O | O |  |
| `xkxk.frameworks.ui.views/PanelViewState.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  | PanelViewState |
| `xkxk.frameworks.ui.views/PdApp.axaml` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/PdApp.axaml.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/PdViewRegistry.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/RootSafeRecyclingDataTemplate.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/SdiDocumentWatch.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O | O | SdiDocumentWatch |
| `xkxk.frameworks.ui.views/SdiTitleBar.TabJoin.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O | O | ManagedSdiTabJoin |
| `xkxk.frameworks.ui.views/SdiTitleBar.axaml` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/SdiTitleBar.axaml.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O | O |  |
| `xkxk.frameworks.ui.views/Settings/AppearancePage.axaml` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Settings/AppearancePage.axaml.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Settings/LanguagePage.axaml` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Settings/LanguagePage.axaml.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Settings/LayoutPage.axaml` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Settings/LayoutPage.axaml.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Settings/SettingsView.axaml` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Settings/SettingsView.axaml.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Settings/SystemPage.axaml` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Settings/SystemPage.axaml.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Settings/WindowPage.axaml` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Settings/WindowPage.axaml.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/ShellStatusBar.axaml` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/ShellStatusBar.axaml.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Snap/SnapAssistLayer.Input.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Snap/SnapAssistLayer.Visuals.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Snap/SnapAssistLayer.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Snap/SnapAssistPicker.axaml` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Snap/SnapAssistPicker.axaml.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Snap/SnapAssistThumbnail.Diag.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Snap/SnapAssistThumbnail.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Snap/SnapFlyoutHover.Pointer.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Snap/SnapFlyoutHover.Target.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Snap/SnapFlyoutHover.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Snap/SnapLayoutBar.axaml` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Snap/SnapLayoutBar.axaml.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Snap/SnapLayoutFlyout.axaml` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Snap/SnapLayoutFlyout.axaml.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Snap/SnapOverlayLayer.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/SystemMenu/AboutView.axaml` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/SystemMenu/AboutView.axaml.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/SystemMenu/AppMonitorView.axaml` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/SystemMenu/AppMonitorView.axaml.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/SystemMenu/ChangelogView.axaml` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/SystemMenu/ChangelogView.axaml.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/SystemMenu/InquiryListView.axaml` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/SystemMenu/InquiryListView.axaml.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/SystemMenu/InquiryView.axaml` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/SystemMenu/InquiryView.axaml.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/SystemMenu/LogViewerView.axaml` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/SystemMenu/LogViewerView.axaml.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/SystemMenu/RestartRequiredView.axaml` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/SystemMenu/RestartRequiredView.axaml.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/SystemMenu/SystemViews.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/SystemMenu/UpdateCheckView.axaml` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/SystemMenu/UpdateCheckView.axaml.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/ThemeDesignPanel.axaml` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/ThemeDesignPanel.axaml.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/ThemeEditorView.axaml` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/ThemeEditorView.axaml.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Themes/CaptionButtons.axaml` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Themes/ChromeButtonStyles.axaml` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Themes/ChromeIcons.axaml` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Themes/ChromeInactive.axaml` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Themes/ChromeInactiveStyles.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Themes/ChromeResources.axaml` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Themes/DataGridTheme.Builtin.axaml` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Themes/DataGridTheme.Pro.axaml` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Themes/DesignEffects.axaml` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Themes/DockJoinTarget.axaml` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Themes/DocumentDockMode.axaml` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Themes/FocusVisual.axaml` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Themes/MacCaptionResources.axaml` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Themes/MdiChrome.axaml` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Themes/MdiDocumentWindowTheme.axaml` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Themes/MdiWindowMenu.axaml` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Themes/MenuIconStyles.axaml` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Themes/MenuIcons.axaml` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Themes/NarrowToolChrome.axaml` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Themes/PinnedDockNoWindow.axaml` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Themes/PinnedDockNoWindowTheme.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Themes/SdiHostWindowTheme.axaml` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/Themes/ThinScrollBar.axaml` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/UiStyleTransition.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/WindowIcon.Automation.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/WindowIcon.Bindings.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/WindowIcon.State.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/WindowIcon.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/WindowSystemMenu.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/WindowTitleMenu.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O |  |  |
| `xkxk.frameworks.ui.views/ZoomDecorator.cs` | Views |  | xkxk.frameworks.ui.views 프로젝트 | O | O |  |
| `xkxk.frameworks.ui/Controls/BusyOverlay.cs` | Ui |  | xkxk.frameworks.ui 프로젝트 | O |  |  |
| `xkxk.frameworks.ui/Controls/ClipBorder.cs` | Ui |  | xkxk.frameworks.ui 프로젝트 | O |  |  |
| `xkxk.frameworks.ui/Controls/CursorSpinnerFrames.cs` | Ui |  | xkxk.frameworks.ui 프로젝트 | O |  |  |
| `xkxk.frameworks.ui/Controls/GanttSpinner.cs` | Ui |  | xkxk.frameworks.ui 프로젝트 | O |  |  |
| `xkxk.frameworks.ui/Controls/GanttSpinnerVisual.cs` | Ui |  | xkxk.frameworks.ui 프로젝트 | O |  | GanttSpinnerMessage, GanttSpinnerVisual |
| `xkxk.frameworks.ui/Controls/IconSpinnerVisual.cs` | Ui |  | xkxk.frameworks.ui 프로젝트 | O |  | IconSpinnerMessage, IconSpinnerVisual |
| `xkxk.frameworks.ui/Controls/PanelCornerButton.cs` | Ui |  | xkxk.frameworks.ui 프로젝트 | O |  |  |
| `xkxk.frameworks.ui/Controls/PanelCornerProximity.cs` | Ui |  | xkxk.frameworks.ui 프로젝트 | O |  |  |
| `xkxk.frameworks.ui/Controls/PointerFollower.cs` | Ui |  | xkxk.frameworks.ui 프로젝트 | O |  |  |
| `xkxk.frameworks.ui/Controls/SettingsRow.cs` | Ui |  | xkxk.frameworks.ui 프로젝트 | O |  |  |
| `xkxk.frameworks.ui/Controls/SettingsShell.axaml` | Ui |  | xkxk.frameworks.ui 프로젝트 | O |  |  |
| `xkxk.frameworks.ui/Controls/SettingsShell.axaml.cs` | Ui |  | xkxk.frameworks.ui 프로젝트 | O |  |  |
| `xkxk.frameworks.ui/Controls/ShortcutLayer.axaml` | Ui |  | xkxk.frameworks.ui 프로젝트 | O |  |  |
| `xkxk.frameworks.ui/Controls/ShortcutLayer.axaml.cs` | Ui |  | xkxk.frameworks.ui 프로젝트 | O |  |  |
| `xkxk.frameworks.ui/Controls/TemplateParts.cs` | Ui |  | xkxk.frameworks.ui 프로젝트 |  |  |  |
| `xkxk.frameworks.ui/Controls/TitleTabPanel.cs` | Ui |  | xkxk.frameworks.ui 프로젝트 | O |  |  |
| `xkxk.frameworks.ui/Controls/TitleTabWidth.cs` | Ui |  | xkxk.frameworks.ui 프로젝트 |  |  |  |
| `xkxk.frameworks.ui/Controls/TourCard.axaml` | Ui |  | xkxk.frameworks.ui 프로젝트 | O |  |  |
| `xkxk.frameworks.ui/Controls/TourCard.axaml.cs` | Ui |  | xkxk.frameworks.ui 프로젝트 | O |  |  |
| `xkxk.frameworks.ui/Controls/TourOverlay.axaml` | Ui |  | xkxk.frameworks.ui 프로젝트 | O |  |  |
| `xkxk.frameworks.ui/Controls/TourOverlay.axaml.cs` | Ui |  | xkxk.frameworks.ui 프로젝트 | O |  |  |
| `xkxk.frameworks.ui/Input/DenseList.cs` | Ui |  | xkxk.frameworks.ui 프로젝트 | O |  | ListDenseTarget |
| `xkxk.frameworks.ui/Input/DenseWheel.cs` | Ui |  | xkxk.frameworks.ui 프로젝트 | O |  |  |
| `xkxk.frameworks.ui/Motion/MotionPreference.cs` | Ui |  | xkxk.frameworks.ui 프로젝트 |  |  |  |
| `xkxk.frameworks.ui/Styles/PdUiStyles.axaml` | Ui |  | xkxk.frameworks.ui 프로젝트 | O |  |  |
| `xkxk.frameworks.ui/Text/UiText.cs` | Ui |  | xkxk.frameworks.ui 프로젝트 |  |  |  |
