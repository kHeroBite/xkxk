# frameworks/ui 소스 계층 분류표

> 자동 생성물 — 고치지 말고 `python3 -B tools/classify_layers.py`(영역 루트 `frameworks/ui` 기준) 로 재생성한다. 예외는 `tools/classify_layers_overrides.json`(사유 필수).
> 층 정의와 참조 규칙의 정본은 `plan/04_기술스택/02_소스구조_분할규칙.md`, 강제는 `tools/check_layers.py` 다. 형식 참조는 정규식 근사이고 빌드가 최종 판정이다.

## 분류별 파일 수

| 층 | 파일 수 |
|---|---:|
| Control | 266 |
| Data | 33 |
| Diag | 86 |
| Host | 72 |
| Ui | 26 |
| Views | 235 |

합계 718개(.cs·.axaml).

## 끊어야 하는 참조(현재 기준 금지 방향)

0건. Shell 이 화면을 직접 만드는 지점과 Data 후보가 밖을 보는 지점이다.

| 참조하는 파일 | 층 | 참조되는 파일 | 층 | 형식 |
|---|---|---|---|---|

## 층에 걸친 partial 형식

0건. 없음

## 옮기지 않기로 한 판단

- **`Pd.Ui` 가 상위 층의 자원 사전 키·토큰 이름을 아는 결합(리뷰 M-7)** — 유지한다. 키 이름은 형식 참조가 아니라 자원 조회 문자열이고, `Pd.Ui` 프로젝트 참조 방향은 그대로 `Avalonia` 뿐이다. Avalonia 제어층의 정상 범위로 판단했다. 키를 없애려면 컨트롤마다 브러시 속성을 새로 열어 값을 넘겨야 해 얻는 분리보다 호출부 비용이 크다.
- **`Pd.Poc.Shell` 의 시각 트리 순회 18개 파일(`Diagnostics` 제외)** — 옮기지 않는다. 창 관리·드래그·투어 같은 제어 로직이 Dock 템플릿 안 컨트롤을 찾는 일로, 화면을 그리는 코드가 아니라 화면을 조작하는 코드다. Avalonia 제어층의 정상 범위로 판단했다. 화면 쪽과의 결합은 부품 이름뿐이며 `Pd.Ui` 의 `TemplateParts` 상수로 묶고 `check_layers.py` 가 실재를 대조한다. 18개는 2026-11-04 실측값이다(`PanelViewState` 를 Views 로 옮긴 뒤).

## 파일별 분류

| 파일 | 층 | 이동 뒤 경로 | 근거 | Avalonia | Dock.Model | 내부 형식 |
|---|---|---|---|:-:|:-:|---|
| `Pd.Client.Data/AppChannel.cs` | Data |  | Pd.Client.Data 프로젝트 |  |  |  |
| `Pd.Client.Data/Layout/BackupBundle.cs` | Data |  | Pd.Client.Data 프로젝트 |  |  |  |
| `Pd.Client.Data/Layout/BackupIndex.cs` | Data |  | Pd.Client.Data 프로젝트 |  |  |  |
| `Pd.Client.Data/Layout/BackupIndexItem.cs` | Data |  | Pd.Client.Data 프로젝트 |  |  |  |
| `Pd.Client.Data/Layout/BackupJsonContext.cs` | Data |  | Pd.Client.Data 프로젝트 |  |  |  |
| `Pd.Client.Data/Layout/FileKeyValueStore.cs` | Data |  | Pd.Client.Data 프로젝트 |  |  |  |
| `Pd.Client.Data/Layout/FreshGuardStores.cs` | Data |  | Pd.Client.Data 프로젝트 |  |  |  |
| `Pd.Client.Data/Layout/ILayoutLibrary.cs` | Data |  | Pd.Client.Data 프로젝트 |  |  |  |
| `Pd.Client.Data/Layout/ISettingsBackupSource.cs` | Data |  | Pd.Client.Data 프로젝트 |  |  |  |
| `Pd.Client.Data/Layout/LayoutStore.cs` | Data |  | Pd.Client.Data 프로젝트 |  |  |  |
| `Pd.Client.Data/Layout/PanelTabsSnapshot.cs` | Data |  | Pd.Client.Data 프로젝트 |  |  |  |
| `Pd.Client.Data/Layout/ProfileStores.cs` | Data |  | Pd.Client.Data 프로젝트 |  |  |  |
| `Pd.Client.Data/Logging/ConsoleLogSink.cs` | Data |  | Pd.Client.Data 프로젝트 |  |  |  |
| `Pd.Client.Data/Logging/FileLogSink.cs` | Data |  | Pd.Client.Data 프로젝트 |  |  |  |
| `Pd.Client.Data/Logging/LogExport.cs` | Data |  | Pd.Client.Data 프로젝트 |  |  |  |
| `Pd.Client.Data/Logging/MemoryLogSink.cs` | Data |  | Pd.Client.Data 프로젝트 |  |  |  |
| `Pd.Client.Data/Logging/PdLog.cs` | Data |  | Pd.Client.Data 프로젝트 |  |  |  |
| `Pd.Client.Data/Logging/PdLogEntry.cs` | Data |  | Pd.Client.Data 프로젝트 |  |  |  |
| `Pd.Client.Data/Navigation/MenuIconCatalog.Generated.cs` | Data |  | Pd.Client.Data 프로젝트 |  |  |  |
| `Pd.Client.Data/Navigation/MenuIconCatalog.cs` | Data |  | Pd.Client.Data 프로젝트 |  |  |  |
| `Pd.Client.Data/Navigation/NavMenuCatalog.cs` | Data |  | Pd.Client.Data 프로젝트 |  |  |  |
| `Pd.Client.Data/Navigation/PersonalFavoriteStore.cs` | Data |  | Pd.Client.Data 프로젝트 |  |  | FavoriteJsonContext |
| `Pd.Client.Data/Rendering/GpuSetting.cs` | Data |  | Pd.Client.Data 프로젝트 |  |  |  |
| `Pd.Client.Data/Samples/GridSeedLegacy.cs` | Data |  | Pd.Client.Data 프로젝트 |  |  |  |
| `Pd.Client.Data/Samples/SampleLoadDelay.cs` | Data |  | Pd.Client.Data 프로젝트 |  |  |  |
| `Pd.Client.Data/Theming/BackgroundImageStore.cs` | Data |  | Pd.Client.Data 프로젝트 |  |  |  |
| `Pd.Client.Data/Theming/LocalBlobStore.cs` | Data |  | Pd.Client.Data 프로젝트 |  |  |  |
| `Pd.Client.Data/Theming/ThemeFileExchange.cs` | Data |  | Pd.Client.Data 프로젝트 |  |  |  |
| `Pd.Poc.Android/AndroidApp.cs` | Host |  | 호스트 프로젝트 | O |  |  |
| `Pd.Poc.Android/AndroidDeviceInfo.cs` | Host |  | 호스트 프로젝트 |  |  | AndroidDeviceInfo |
| `Pd.Poc.Android/AndroidLayoutStore.cs` | Host |  | 호스트 프로젝트 |  |  |  |
| `Pd.Poc.Android/AndroidSafeArea.cs` | Host |  | 호스트 프로젝트 | O |  | AndroidSafeArea |
| `Pd.Poc.Android/MainActivity.cs` | Host |  | 호스트 프로젝트 | O |  |  |
| `Pd.Poc.Android/StartupGesture.cs` | Host |  | 호스트 프로젝트 |  |  | StartupGesture |
| `Pd.Poc.Android/StartupHitTest.cs` | Host |  | 호스트 프로젝트 | O |  | StartupHitTest |
| `Pd.Poc.Android/StartupMarks.cs` | Host |  | 호스트 프로젝트 |  |  | StartupMarks |
| `Pd.Poc.Android/StartupTouchGate.Discard.cs` | Host |  | 호스트 프로젝트 | O |  | StartupTouchGate |
| `Pd.Poc.Android/StartupTouchGate.cs` | Host |  | 호스트 프로젝트 | O |  | StartupTouchGate |
| `Pd.Poc.Browser/AutomationExports.cs` | Host |  | 호스트 프로젝트 |  |  |  |
| `Pd.Poc.Browser/BrowserBlobStore.cs` | Host |  | 호스트 프로젝트 |  |  |  |
| `Pd.Poc.Browser/BrowserHostOs.cs` | Host |  | 호스트 프로젝트 |  |  | BrowserHostOs |
| `Pd.Poc.Browser/BrowserKeyValueStore.cs` | Host |  | 호스트 프로젝트 |  |  |  |
| `Pd.Poc.Browser/BrowserLayoutStore.cs` | Host |  | 호스트 프로젝트 |  |  |  |
| `Pd.Poc.Browser/BrowserMotionPreference.cs` | Host |  | 호스트 프로젝트 |  |  | BrowserMotionPreference |
| `Pd.Poc.Browser/BrowserPressTimeSource.cs` | Host |  | 호스트 프로젝트 |  |  | BrowserPressTimeSource |
| `Pd.Poc.Browser/LifecycleExports.cs` | Host |  | 호스트 프로젝트 |  |  |  |
| `Pd.Poc.Browser/PageKeyFocus.cs` | Host |  | 호스트 프로젝트 |  |  | PageKeyFocus |
| `Pd.Poc.Browser/PagePointerLeave.cs` | Host |  | 호스트 프로젝트 |  |  | PagePointerLeave |
| `Pd.Poc.Browser/Program.Storage.cs` | Host |  | 호스트 프로젝트 |  |  | Program |
| `Pd.Poc.Browser/Program.cs` | Host |  | 호스트 프로젝트 | O |  | Program |
| `Pd.Poc.Browser/StartupTiming.cs` | Host |  | 호스트 프로젝트 | O |  | StartupTiming |
| `Pd.Poc.Desktop/AppIdentity.cs` | Host |  | 호스트 프로젝트 |  |  | AppIdentity |
| `Pd.Poc.Desktop/AppWindowIcon.cs` | Host |  | 호스트 프로젝트 | O |  | AppWindowIcon |
| `Pd.Poc.Desktop/BgPlacement.cs` | Host |  | 호스트 프로젝트 |  |  |  |
| `Pd.Poc.Desktop/BgWindowPolicy.cs` | Host |  | 호스트 프로젝트 | O |  |  |
| `Pd.Poc.Desktop/CaptureProtection.cs` | Host |  | 호스트 프로젝트 | O |  | CaptureProtection |
| `Pd.Poc.Desktop/DesktopDiagHooks.cs` | Host |  | 호스트 프로젝트 |  |  | DesktopDiagHooks |
| `Pd.Poc.Desktop/DesktopMonitorProvider.cs` | Host |  | 호스트 프로젝트 | O |  |  |
| `Pd.Poc.Desktop/DesktopWindowHost.cs` | Host |  | 호스트 프로젝트 | O | O |  |
| `Pd.Poc.Desktop/FileLayoutStore.cs` | Host |  | 호스트 프로젝트 |  |  |  |
| `Pd.Poc.Desktop/HostArgs.cs` | Host |  | 호스트 프로젝트 |  |  | HostArgs |
| `Pd.Poc.Desktop/HostLog.cs` | Host |  | 호스트 프로젝트 |  |  | HostLog |
| `Pd.Poc.Desktop/HostRenderOptions.cs` | Host |  | 호스트 프로젝트 | O |  |  |
| `Pd.Poc.Desktop/LaunchOptions.cs` | Host |  | 호스트 프로젝트 | O |  | LaunchOptions |
| `Pd.Poc.Desktop/MainBgPinHook.cs` | Host |  | 호스트 프로젝트 | O |  | MainBgPinHook |
| `Pd.Poc.Desktop/MainNcMenuHook.cs` | Host |  | 호스트 프로젝트 | O |  | MainNcMenuHook |
| `Pd.Poc.Desktop/MainWindowChrome.cs` | Host |  | 호스트 프로젝트 | O |  |  |
| `Pd.Poc.Desktop/MainWindowDecorations.cs` | Host |  | 호스트 프로젝트 | O |  | MainWindowDecorations |
| `Pd.Poc.Desktop/MaxTrackHook.cs` | Host |  | 호스트 프로젝트 | O |  | MaxTrackHook |
| `Pd.Poc.Desktop/MonitorChangeWatcher.cs` | Host |  | 호스트 프로젝트 | O |  | MonitorChangeWatcher |
| `Pd.Poc.Desktop/MonitorFitService.cs` | Host |  | 호스트 프로젝트 | O |  | MonitorFitService |
| `Pd.Poc.Desktop/OffscreenGuardHook.cs` | Host |  | 호스트 프로젝트 | O |  | OffscreenGuardHook |
| `Pd.Poc.Desktop/OsSnapProbe.cs` | Host |  | 호스트 프로젝트 |  |  | OsSnapProbe |
| `Pd.Poc.Desktop/Program.cs` | Host |  | 호스트 프로젝트 | O |  | Program |
| `Pd.Poc.Desktop/SdiAboveMainHook.cs` | Host |  | 호스트 프로젝트 | O |  | SdiAboveMainHook |
| `Pd.Poc.Desktop/SdiBgPinHook.cs` | Host |  | 호스트 프로젝트 | O |  | SdiBgPinHook |
| `Pd.Poc.Desktop/SdiHostPlacement.cs` | Host |  | 호스트 프로젝트 | O |  | SdiHostPlacement |
| `Pd.Poc.Desktop/SdiMagnetHook.cs` | Host |  | 호스트 프로젝트 | O |  | SdiMagnetHook |
| `Pd.Poc.Desktop/SdiMagnetSnapper.cs` | Host |  | 호스트 프로젝트 | O |  | SdiMagnetSnapper |
| `Pd.Poc.Desktop/SdiQuietState.cs` | Host |  | 호스트 프로젝트 | O | O | SdiQuietState |
| `Pd.Poc.Desktop/SdiResizeDebounce.cs` | Host |  | 호스트 프로젝트 | O |  | SdiResizeDebounce |
| `Pd.Poc.Desktop/SdiShadowBitmap.cs` | Host |  | 호스트 프로젝트 |  |  | SdiShadowBitmap |
| `Pd.Poc.Desktop/SdiShadowNative.cs` | Host |  | 호스트 프로젝트 |  |  | SdiShadowNative |
| `Pd.Poc.Desktop/SdiShadowShape.cs` | Host |  | 호스트 프로젝트 | O |  | SdiShadowShape |
| `Pd.Poc.Desktop/SdiShadowStack.cs` | Host |  | 호스트 프로젝트 | O |  | SdiShadowStack |
| `Pd.Poc.Desktop/SdiShadowSurface.cs` | Host |  | 호스트 프로젝트 |  |  | SdiShadowSurface |
| `Pd.Poc.Desktop/SdiShadowWindow.cs` | Host |  | 호스트 프로젝트 | O |  | SdiShadowWindow |
| `Pd.Poc.Desktop/SdiSnapAssistHost.cs` | Host |  | 호스트 프로젝트 | O |  | SdiSnapAssistHost |
| `Pd.Poc.Desktop/SdiSnapBarHost.cs` | Host |  | 호스트 프로젝트 | O |  | SdiSnapBarHost |
| `Pd.Poc.Desktop/SdiSnapHook.cs` | Host |  | 호스트 프로젝트 | O | O | SdiSnapHook |
| `Pd.Poc.Desktop/SdiTabJoinHook.cs` | Host |  | 호스트 프로젝트 | O |  | SdiTabJoinHook |
| `Pd.Poc.Desktop/SdiWindowHost.cs` | Host |  | 호스트 프로젝트 | O | O |  |
| `Pd.Poc.Desktop/SdiZOrderProbe.cs` | Host |  | 호스트 프로젝트 | O | O | SdiZOrderProbe |
| `Pd.Poc.Desktop/SdiZOrderRaiser.cs` | Host |  | 호스트 프로젝트 | O | O | SdiZOrderRaiser |
| `Pd.Poc.Desktop/SingleInstance.cs` | Host |  | 호스트 프로젝트 |  |  |  |
| `Pd.Poc.Desktop/SysWindowBgPinHook.cs` | Host |  | 호스트 프로젝트 | O |  | SysWindowBgPinHook |
| `Pd.Poc.Desktop/WindowDefaultMove.cs` | Host |  | 호스트 프로젝트 | O | O | WindowDefaultMove |
| `Pd.Poc.Desktop/WindowFrameMetrics.cs` | Host |  | 호스트 프로젝트 | O |  | WindowFrameMetrics |
| `Pd.Poc.Desktop/WindowPlacement.cs` | Host |  | 호스트 프로젝트 | O |  |  |
| `Pd.Poc.Desktop/WindowPosTrace.cs` | Host |  | 호스트 프로젝트 | O |  | WindowPosTrace |
| `Pd.Poc.Shell/App.cs` | Control |  | 정적 진입점 표면(App.*)을 셸 42곳·호스트가 쓴다. axaml 은 Pd.Ui.Views 의 PdApp 으로 가고 이 파일은 기반 클래스로 남는다 | O | O |  |
| `Pd.Poc.Shell/Busy/BusyCursorDriver.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  | BusyCursorDriver |
| `Pd.Poc.Shell/Busy/BusyHooks.Release.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(BusyCursorDriver.cs) |  |  |  |
| `Pd.Poc.Shell/Busy/BusyHooks.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `Pd.Poc.Shell/Busy/BusyScope.cs` | Control |  | BusyService 짝. 판정기 고정 경로(AC-2-02) |  |  |  |
| `Pd.Poc.Shell/Busy/BusyService.cs` | Control |  | 표시 지연 상태 전이. 판정기 고정 경로(AC-2-02·2-05) |  |  |  |
| `Pd.Poc.Shell/Busy/DispatcherBusyClock.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `Pd.Poc.Shell/Diagnostics/AutomationArgs.cs` | Diag |  | 진단 전용 폴더 |  |  | AutomationArgs |
| `Pd.Poc.Shell/Diagnostics/AutomationBridge.Product.cs` | Diag |  | 진단 전용 폴더 | O |  |  |
| `Pd.Poc.Shell/Diagnostics/AutomationBridge.cs` | Diag |  | 진단 전용 폴더 | O |  |  |
| `Pd.Poc.Shell/Diagnostics/AutomationCommandRunner.cs` | Diag |  | 진단 전용 폴더 | O | O | AutomationCommandRunner |
| `Pd.Poc.Shell/Diagnostics/AutomationOptions.cs` | Diag |  | 진단 전용 폴더 |  |  |  |
| `Pd.Poc.Shell/Diagnostics/GridMeasureTrace.cs` | Diag |  | 진단 전용 폴더 | O |  |  |
| `Pd.Poc.Shell/Diagnostics/IDocumentDiagnostics.cs` | Diag |  | 진단 전용 폴더 | O |  |  |
| `Pd.Poc.Shell/Diagnostics/MdiDragFrameDiag.Stages.cs` | Diag |  | 진단 전용 폴더 | O |  |  |
| `Pd.Poc.Shell/Diagnostics/MdiDragFrameDiag.cs` | Diag |  | 진단 전용 폴더 | O |  |  |
| `Pd.Poc.Shell/Diagnostics/MdiDragProbe.cs` | Diag |  | 진단 전용 폴더 | O |  |  |
| `Pd.Poc.Shell/Diagnostics/NotificationPanelProbe.cs` | Diag |  | 진단 전용 폴더 |  |  |  |
| `Pd.Poc.Shell/Diagnostics/PerfCommitLog.cs` | Diag |  | 진단 전용 폴더 | O |  | PerfCommitLog |
| `Pd.Poc.Shell/Diagnostics/PerfFrameLog.cs` | Diag |  | 진단 전용 폴더 |  |  | PerfFrameLog |
| `Pd.Poc.Shell/Diagnostics/PerfLayoutLog.cs` | Diag |  | 진단 전용 폴더 | O |  | PerfLayoutLog |
| `Pd.Poc.Shell/Diagnostics/PerfOpenTracker.cs` | Diag |  | 진단 전용 폴더 | O |  | PerfOpenTracker |
| `Pd.Poc.Shell/Diagnostics/PerfOperations.cs` | Diag |  | 진단 전용 폴더 |  |  | PerfOperations |
| `Pd.Poc.Shell/Diagnostics/PerfProbe.Product.cs` | Diag |  | 진단 전용 폴더 | O |  |  |
| `Pd.Poc.Shell/Diagnostics/PerfProbe.cs` | Diag |  | 진단 전용 폴더 | O |  |  |
| `Pd.Poc.Shell/Diagnostics/PerfSampler.cs` | Diag |  | 진단 전용 폴더 | O |  | PerfSampler |
| `Pd.Poc.Shell/Diagnostics/PerfStats.cs` | Diag |  | 진단 전용 폴더 |  |  | PerfStats |
| `Pd.Poc.Shell/Diagnostics/PerfUiRunner.cs` | Diag |  | 진단 전용 폴더 | O | O | PerfUiRunner |
| `Pd.Poc.Shell/Diagnostics/PerfUiSplit.cs` | Diag |  | 진단 전용 폴더 | O |  | PerfUiSplit |
| `Pd.Poc.Shell/Diagnostics/PinchZoomDiag.cs` | Diag |  | 진단 전용 폴더 | O |  |  |
| `Pd.Poc.Shell/Diagnostics/Remote/ContextMenuInspector.cs` | Diag |  | 진단 전용 폴더 | O |  | ContextMenuInspector |
| `Pd.Poc.Shell/Diagnostics/Remote/MenuHeaderText.cs` | Diag |  | 진단 전용 폴더 | O |  | MenuHeaderText |
| `Pd.Poc.Shell/Diagnostics/Remote/RemoteBackgroundCommands.cs` | Diag |  | 진단 전용 폴더 | O |  | RemoteBackgroundCommands |
| `Pd.Poc.Shell/Diagnostics/Remote/RemoteBackgroundSelfTest.cs` | Diag |  | 진단 전용 폴더 |  |  | RemoteBackgroundSelfTest |
| `Pd.Poc.Shell/Diagnostics/Remote/RemoteBusyCommands.Grid.cs` | Diag |  | 진단 전용 폴더 | O |  | RemoteBusyCommands |
| `Pd.Poc.Shell/Diagnostics/Remote/RemoteBusyCommands.cs` | Diag |  | 진단 전용 폴더 | O |  | RemoteBusyCommands |
| `Pd.Poc.Shell/Diagnostics/Remote/RemoteBusySelfTest.cs` | Diag |  | 진단 전용 폴더 |  |  | RemoteBusySelfTest |
| `Pd.Poc.Shell/Diagnostics/Remote/RemoteChromeCommands.cs` | Diag |  | 진단 전용 폴더 | O |  | RemoteChromeCommands |
| `Pd.Poc.Shell/Diagnostics/Remote/RemoteCommandRouter.cs` | Diag |  | 진단 전용 폴더 | O |  |  |
| `Pd.Poc.Shell/Diagnostics/Remote/RemoteCommands.cs` | Diag |  | 진단 전용 폴더 |  |  |  |
| `Pd.Poc.Shell/Diagnostics/Remote/RemoteControlServer.cs` | Diag |  | 진단 전용 폴더 |  |  |  |
| `Pd.Poc.Shell/Diagnostics/Remote/RemoteDesignThemeCommands.cs` | Diag |  | 진단 전용 폴더 | O |  | RemoteDesignThemeCommands |
| `Pd.Poc.Shell/Diagnostics/Remote/RemoteDockAccess.cs` | Diag |  | 진단 전용 폴더 | O | O | RemoteDockAccess |
| `Pd.Poc.Shell/Diagnostics/Remote/RemoteDockAsDocumentCommands.cs` | Diag |  | 진단 전용 폴더 |  | O | RemoteDockAsDocumentCommands |
| `Pd.Poc.Shell/Diagnostics/Remote/RemoteDockMenuCommands.cs` | Diag |  | 진단 전용 폴더 | O | O | RemoteDockMenuCommands |
| `Pd.Poc.Shell/Diagnostics/Remote/RemoteDockMenuInvokeCommands.cs` | Diag |  | 진단 전용 폴더 | O | O | RemoteDockMenuInvokeCommands |
| `Pd.Poc.Shell/Diagnostics/Remote/RemoteDockMenuOpenCommands.cs` | Diag |  | 진단 전용 폴더 | O | O | RemoteDockMenuOpenCommands |
| `Pd.Poc.Shell/Diagnostics/Remote/RemoteDockTraceCommands.cs` | Diag |  | 진단 전용 폴더 | O | O | RemoteDockTraceCommands |
| `Pd.Poc.Shell/Diagnostics/Remote/RemoteEffectPerfCommands.cs` | Diag |  | 진단 전용 폴더 |  |  | RemoteEffectPerfCommands |
| `Pd.Poc.Shell/Diagnostics/Remote/RemoteHttp.cs` | Diag |  | 진단 전용 폴더 |  |  |  |
| `Pd.Poc.Shell/Diagnostics/Remote/RemoteInfoPath.cs` | Diag |  | 진단 전용 폴더 |  |  |  |
| `Pd.Poc.Shell/Diagnostics/Remote/RemoteLangCommands.cs` | Diag |  | 진단 전용 폴더 | O |  | RemoteLangCommands |
| `Pd.Poc.Shell/Diagnostics/Remote/RemoteLayoutsCommands.cs` | Diag |  | 진단 전용 폴더 |  |  | RemoteLayoutsCommands |
| `Pd.Poc.Shell/Diagnostics/Remote/RemoteLoadSelfTest.cs` | Diag |  | 진단 전용 폴더 | O |  | RemoteLoadSelfTest |
| `Pd.Poc.Shell/Diagnostics/Remote/RemoteMonitorCommands.cs` | Diag |  | 진단 전용 폴더 | O |  | RemoteMonitorCommands |
| `Pd.Poc.Shell/Diagnostics/Remote/RemoteNotifyCommands.cs` | Diag |  | 진단 전용 폴더 |  |  | RemoteNotifyCommands |
| `Pd.Poc.Shell/Diagnostics/Remote/RemotePanelCornerCommands.cs` | Diag |  | 진단 전용 폴더 | O | O | RemotePanelCornerCommands |
| `Pd.Poc.Shell/Diagnostics/Remote/RemotePanelsCommands.cs` | Diag |  | 진단 전용 폴더 | O | O | RemotePanelsCommands |
| `Pd.Poc.Shell/Diagnostics/Remote/RemotePlacementCommands.Groups.cs` | Diag |  | 진단 전용 폴더 | O | O | RemotePlacementCommands |
| `Pd.Poc.Shell/Diagnostics/Remote/RemotePlacementCommands.Ratio.cs` | Diag |  | 진단 전용 폴더 | O |  | RemotePlacementCommands |
| `Pd.Poc.Shell/Diagnostics/Remote/RemotePlacementCommands.Sdi.cs` | Diag |  | 진단 전용 폴더 | O | O | RemotePlacementCommands |
| `Pd.Poc.Shell/Diagnostics/Remote/RemotePlacementCommands.cs` | Diag |  | 진단 전용 폴더 | O | O | RemotePlacementCommands |
| `Pd.Poc.Shell/Diagnostics/Remote/RemotePointerInputCommands.cs` | Diag |  | 진단 전용 폴더 | O |  | RemotePointerInputCommands |
| `Pd.Poc.Shell/Diagnostics/Remote/RemoteProbeCommands.cs` | Diag |  | 진단 전용 폴더 | O | O | RemoteProbeCommands |
| `Pd.Poc.Shell/Diagnostics/Remote/RemoteProductStubs.cs` | Diag |  | 진단 전용 폴더 |  |  |  |
| `Pd.Poc.Shell/Diagnostics/Remote/RemoteProtocol.cs` | Diag |  | 진단 전용 폴더 |  |  |  |
| `Pd.Poc.Shell/Diagnostics/Remote/RemoteRenderOverlayCommands.cs` | Diag |  | 진단 전용 폴더 | O |  | RemoteRenderOverlayCommands |
| `Pd.Poc.Shell/Diagnostics/Remote/RemoteRenderProbeCommands.cs` | Diag |  | 진단 전용 폴더 | O | O | RemoteRenderProbeCommands |
| `Pd.Poc.Shell/Diagnostics/Remote/RemoteScrollCommands.cs` | Diag |  | 진단 전용 폴더 | O | O | RemoteScrollCommands |
| `Pd.Poc.Shell/Diagnostics/Remote/RemoteSdiShadowCommands.cs` | Diag |  | 진단 전용 폴더 | O |  | RemoteSdiShadowCommands |
| `Pd.Poc.Shell/Diagnostics/Remote/RemoteSdiZ.cs` | Diag |  | 진단 전용 폴더 | O |  | RemoteSdiZ |
| `Pd.Poc.Shell/Diagnostics/Remote/RemoteSettingsCommands.cs` | Diag |  | 진단 전용 폴더 | O |  | RemoteSettingsCommands |
| `Pd.Poc.Shell/Diagnostics/Remote/RemoteShellAccess.cs` | Diag |  | 진단 전용 폴더 | O | O | RemoteShellAccess |
| `Pd.Poc.Shell/Diagnostics/Remote/RemoteSimActiveCommands.cs` | Diag |  | 진단 전용 폴더 | O | O | RemoteSimActiveCommands |
| `Pd.Poc.Shell/Diagnostics/Remote/RemoteSnapAssistCommands.cs` | Diag |  | 진단 전용 폴더 |  |  | RemoteSnapAssistCommands |
| `Pd.Poc.Shell/Diagnostics/Remote/RemoteSnapCommands.cs` | Diag |  | 진단 전용 폴더 | O |  | RemoteSnapCommands |
| `Pd.Poc.Shell/Diagnostics/Remote/RemoteSplitCommands.cs` | Diag |  | 진단 전용 폴더 | O | O | RemoteSplitCommands |
| `Pd.Poc.Shell/Diagnostics/Remote/RemoteSystemCommands.cs` | Diag |  | 진단 전용 폴더 | O | O | RemoteSystemCommands |
| `Pd.Poc.Shell/Diagnostics/Remote/RemoteTextRenderCommands.cs` | Diag |  | 진단 전용 폴더 | O |  | RemoteTextRenderCommands |
| `Pd.Poc.Shell/Diagnostics/Remote/RemoteThemeDesignCommands.cs` | Diag |  | 진단 전용 폴더 | O |  | RemoteThemeDesignCommands |
| `Pd.Poc.Shell/Diagnostics/Remote/RemoteTitleMenuCommands.cs` | Diag |  | 진단 전용 폴더 | O | O | RemoteTitleMenuCommands |
| `Pd.Poc.Shell/Diagnostics/Remote/RemoteTourCatalogCommands.cs` | Diag |  | 진단 전용 폴더 | O |  | RemoteTourCatalogCommands |
| `Pd.Poc.Shell/Diagnostics/Remote/RemoteTourCommands.cs` | Diag |  | 진단 전용 폴더 | O |  | RemoteTourCommands |
| `Pd.Poc.Shell/Diagnostics/Remote/RemoteTrayCommands.cs` | Diag |  | 진단 전용 폴더 | O |  | RemoteTrayCommands |
| `Pd.Poc.Shell/Diagnostics/Remote/RemoteUiCommands.cs` | Diag |  | 진단 전용 폴더 | O |  | RemoteUiCommands |
| `Pd.Poc.Shell/Diagnostics/Remote/RemoteUiModeCommands.cs` | Diag |  | 진단 전용 폴더 | O |  |  |
| `Pd.Poc.Shell/Diagnostics/Remote/RemoteUiTargets.cs` | Diag |  | 진단 전용 폴더 | O |  | RemoteUiTargets |
| `Pd.Poc.Shell/Diagnostics/Remote/RemoteWheelCommands.cs` | Diag |  | 진단 전용 폴더 | O |  | RemoteWheelCommands |
| `Pd.Poc.Shell/Diagnostics/Remote/RemoteWindowMenuCommands.cs` | Diag |  | 진단 전용 폴더 | O |  | RemoteWindowMenuCommands |
| `Pd.Poc.Shell/Diagnostics/Remote/RemoteWindowsCommands.MoveLight.cs` | Diag |  | 진단 전용 폴더 | O |  | RemoteWindowsCommands, MdiReleaseTrace |
| `Pd.Poc.Shell/Diagnostics/Remote/RemoteWindowsCommands.cs` | Diag |  | 진단 전용 폴더 | O | O | RemoteWindowsCommands |
| `Pd.Poc.Shell/Diagnostics/Remote/VisualTreeInspector.cs` | Diag |  | 진단 전용 폴더 | O |  |  |
| `Pd.Poc.Shell/Diagnostics/ThemeCensusRunner.cs` | Diag |  | 진단 전용 폴더 | O |  | ThemeCensusRunner |
| `Pd.Poc.Shell/Docking/ActivationOrigin.cs` | Data | Pd.Client.Data/Layout/ActivationOrigin.cs | Avalonia 무참조, 참조 형식도 전부 Data |  |  |  |
| `Pd.Poc.Shell/Docking/CaptionLayout.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `Pd.Poc.Shell/Docking/CenterDropTitleAlign.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O | CenterDropTitleAlign |
| `Pd.Poc.Shell/Docking/CenterDropToMdi.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O | CenterDropToMdi |
| `Pd.Poc.Shell/Docking/ChromeApplyDiag.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  | ChromeApplyDiag |
| `Pd.Poc.Shell/Docking/ChromeInactiveDim.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(WindowChromeStyle.Design.cs) |  |  |  |
| `Pd.Poc.Shell/Docking/ChromePlatform.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(ChromeStyleKind.cs) |  |  |  |
| `Pd.Poc.Shell/Docking/ChromeStyleKind.cs` | Control |  | 창 크롬 상태 열거. 제어층 어휘 |  |  |  |
| `Pd.Poc.Shell/Docking/DockFactoryDiag.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(App.cs) |  | O |  |
| `Pd.Poc.Shell/Docking/DockLayoutBuilder.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(DockFactoryDiag.cs) |  | O | DockLayoutBuilder |
| `Pd.Poc.Shell/Docking/DockLayoutState.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(PanelArea.cs) |  | O | DockLayoutState |
| `Pd.Poc.Shell/Docking/DocumentAutoTitles.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(LayoutSnapshot.cs) |  |  | DocumentAutoTitles |
| `Pd.Poc.Shell/Docking/DocumentChromeService.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O | DocumentChromeService |
| `Pd.Poc.Shell/Docking/DocumentChromeState.cs` | Control |  | 창 크롬 상태. 제어층 어휘 |  |  |  |
| `Pd.Poc.Shell/Docking/DocumentLifecycle.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O | DocumentLifecycle |
| `Pd.Poc.Shell/Docking/DocumentSplitCollapse.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(PanelDocumentDock.cs) |  | O | DocumentSplitCollapse |
| `Pd.Poc.Shell/Docking/DocumentSplitMode.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(PanelDocumentDock.cs) |  | O | DocumentSplitMode |
| `Pd.Poc.Shell/Docking/DocumentTabMenu.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O | DocumentTabMenu |
| `Pd.Poc.Shell/Docking/DocumentZoom.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  | DocumentZoom |
| `Pd.Poc.Shell/Docking/IContentLoadReporter.cs` | Data | Pd.Client.Data/Layout/IContentLoadReporter.cs | Avalonia 무참조, 참조 형식도 전부 Data |  |  |  |
| `Pd.Poc.Shell/Docking/IDocumentViewFactory.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `Pd.Poc.Shell/Docking/IDocumentZoomHost.cs` | Control |  | 문서 래퍼 표식 계약. 데이터가 아니라 화면 경계를 찾는 제어 어휘 |  |  |  |
| `Pd.Poc.Shell/Docking/ILayoutStore.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(Loc.cs) |  | O |  |
| `Pd.Poc.Shell/Docking/IMonitorProvider.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `Pd.Poc.Shell/Docking/IPressTimeSource.cs` | Control |  | 입력 시각 추상화. 제어층 어휘 |  |  |  |
| `Pd.Poc.Shell/Docking/IWindowHostCommands.cs` | Control |  | 창 명령 인터페이스. 제어층 어휘 |  | O |  |
| `Pd.Poc.Shell/Docking/LayoutAutoSaver.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O |  |
| `Pd.Poc.Shell/Docking/LayoutBackupService.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(BusyHooks.Release.cs) |  |  | LayoutBackupService |
| `Pd.Poc.Shell/Docking/LayoutGroupCodec.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(LayoutSnapshot.cs) |  | O |  |
| `Pd.Poc.Shell/Docking/LayoutLibrary.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(BusyHooks.Release.cs) |  |  | LayoutIndexJsonContext |
| `Pd.Poc.Shell/Docking/LayoutModeGate.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O |  |
| `Pd.Poc.Shell/Docking/LayoutRestorer.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O | LayoutRestorer |
| `Pd.Poc.Shell/Docking/LayoutSerializer.cs` | Control |  | 직렬화지만 Data 로 내리지 않았다: Avalonia·Dock.Avalonia·PocDockFactory 를 참조한다. 옮기려면 별도 리팩터링이 필요하다 | O | O |  |
| `Pd.Poc.Shell/Docking/LayoutSnapshot.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O | LayoutJsonContext |
| `Pd.Poc.Shell/Docking/LayoutTemplates.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(ILayoutStore.cs) |  |  |  |
| `Pd.Poc.Shell/Docking/MagnetDriver.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O |  |
| `Pd.Poc.Shell/Docking/MagnetMdiLayoutManager.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O |  |
| `Pd.Poc.Shell/Docking/MagnetSnap.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `Pd.Poc.Shell/Docking/ManagedLayerFirst.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `Pd.Poc.Shell/Docking/ManagedSnapDrag.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O |  |
| `Pd.Poc.Shell/Docking/ManagedWindowHostCommands.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O |  |
| `Pd.Poc.Shell/Docking/MdiActiveSync.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O | MdiActiveSync |
| `Pd.Poc.Shell/Docking/MdiDragLimit.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `Pd.Poc.Shell/Docking/MdiGrabZoneCache.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  | MdiGrabZoneCache, MdiGrabZoneDiag |
| `Pd.Poc.Shell/Docking/MdiGrabZoneReader.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  | MdiGrabZoneReader |
| `Pd.Poc.Shell/Docking/MdiMoveLight.RestoreSpread.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(MdiMoveLight.cs) |  |  |  |
| `Pd.Poc.Shell/Docking/MdiMoveLight.Shadow.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(MdiMoveLight.RestoreSpread.cs) |  |  |  |
| `Pd.Poc.Shell/Docking/MdiMoveLight.Spread.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(MdiMoveLight.RestoreSpread.cs) |  |  |  |
| `Pd.Poc.Shell/Docking/MdiMoveLight.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `Pd.Poc.Shell/Docking/MdiMovePointerCoalesce.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  | MdiMovePointerCoalesce |
| `Pd.Poc.Shell/Docking/MdiSnapDrag.Timing.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O |  |
| `Pd.Poc.Shell/Docking/MdiSnapDrag.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O |  |
| `Pd.Poc.Shell/Docking/MdiStripFold.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  | MdiStripFold |
| `Pd.Poc.Shell/Docking/MdiTabGroup.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(RemoteCommandRouter.cs) |  | O | MdiTabGroupDiag |
| `Pd.Poc.Shell/Docking/MdiTabJoinDiag.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O | MdiTabJoinDiag |
| `Pd.Poc.Shell/Docking/MdiTabJoinDrag.Near.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `Pd.Poc.Shell/Docking/MdiTabJoinDrag.Timing.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `Pd.Poc.Shell/Docking/MdiTabJoinDrag.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O |  |
| `Pd.Poc.Shell/Docking/MdiTileMinSize.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(MdiTabGroup.cs) |  | O |  |
| `Pd.Poc.Shell/Docking/MdiVisibilityGuard.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O |  |
| `Pd.Poc.Shell/Docking/MdiZOrder.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O | MdiZOrder |
| `Pd.Poc.Shell/Docking/PanelArea.cs` | Control |  | 패널 영역 어휘. 제어층 |  |  |  |
| `Pd.Poc.Shell/Docking/PanelAreaNames.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(PanelArea.cs) |  |  |  |
| `Pd.Poc.Shell/Docking/PanelCornerBuild.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O | PanelCornerBuild |
| `Pd.Poc.Shell/Docking/PanelCornerMinSize.cs` | Control |  | Avalonia 무참조지만 저장 데이터가 아니라 패널 끝 확장의 제어 계산이다. PanelCornerBuild(Control)만 쓴다 |  |  | PanelCornerMinSize |
| `Pd.Poc.Shell/Docking/PanelCornerOwnership.cs` | Control |  | 모서리 소유 상태. 판정기 고정 경로(AC-3-06) |  |  |  |
| `Pd.Poc.Shell/Docking/PanelCornerRules.cs` | Control |  | 모서리 규칙 논리. 판정기 고정 경로(AC-3-01·3-05) |  |  |  |
| `Pd.Poc.Shell/Docking/PanelCornerService.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O |  |
| `Pd.Poc.Shell/Docking/PanelDocumentDock.cs` | Control |  | Dock 모델 파생(트리 구성 요소). 저장 DTO 가 아니다 |  | O |  |
| `Pd.Poc.Shell/Docking/PanelDropService.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O | PanelDropService |
| `Pd.Poc.Shell/Docking/PanelEdge.cs` | Control |  | 패널 변 어휘. 제어층 |  |  |  |
| `Pd.Poc.Shell/Docking/PanelEdgeNames.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(PanelEdge.cs) |  |  |  |
| `Pd.Poc.Shell/Docking/PanelFeatureGate.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(PocDockFactory.TabGroups.cs) |  |  |  |
| `Pd.Poc.Shell/Docking/PanelFocusActivation.cs` | Control |  | 포커스 전이 논리 |  | O | PanelFocusActivation |
| `Pd.Poc.Shell/Docking/PanelHideService.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(DockLayoutState.cs) |  | O | PanelHideService |
| `Pd.Poc.Shell/Docking/PanelJoinDiag.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O | PanelJoinDiag |
| `Pd.Poc.Shell/Docking/PanelJoinDrag.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O |  |
| `Pd.Poc.Shell/Docking/PanelJoinMoveCandidates.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  | PanelJoinMoveCandidates |
| `Pd.Poc.Shell/Docking/PanelJoinTargets.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O | PanelJoinTargets |
| `Pd.Poc.Shell/Docking/PanelMoveService.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(DockLayoutState.cs) |  | O | PanelMoveService |
| `Pd.Poc.Shell/Docking/PanelOpenStates.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O | PanelOpenStates |
| `Pd.Poc.Shell/Docking/PanelPinShares.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(PocDockFactory.TabGroups.cs) |  | O | PinShare, PanelPinShares |
| `Pd.Poc.Shell/Docking/PanelPositionOps.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(PanelArea.cs) |  | O |  |
| `Pd.Poc.Shell/Docking/PanelQuery.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(DockLayoutState.cs) |  | O | PanelQuery |
| `Pd.Poc.Shell/Docking/PanelTabStripDrop.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O |  |
| `Pd.Poc.Shell/Docking/PanelTabsService.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O | PanelTabsService |
| `Pd.Poc.Shell/Docking/PlacementRecovery.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O |  |
| `Pd.Poc.Shell/Docking/PocDockFactory.TabGroups.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O |  |
| `Pd.Poc.Shell/Docking/PocDockFactory.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O |  |
| `Pd.Poc.Shell/Docking/PocDocument.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(ILayoutStore.cs) |  | O |  |
| `Pd.Poc.Shell/Docking/RestoreViewDeferral.Idle.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `Pd.Poc.Shell/Docking/RestoreViewDeferral.Load.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(PerfProbe.Product.cs) |  |  |  |
| `Pd.Poc.Shell/Docking/RestoreViewDeferral.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O |  |
| `Pd.Poc.Shell/Docking/SdiPlacement.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O | SdiPlacement |
| `Pd.Poc.Shell/Docking/SdiRedockPolicy.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `Pd.Poc.Shell/Docking/SdiResetLayout.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `Pd.Poc.Shell/Docking/SdiScreenPlacement.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O | SdiScreenPlacement |
| `Pd.Poc.Shell/Docking/SidePanelVisibility.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(PerfProbe.Product.cs) |  |  | SidePanelVisibility |
| `Pd.Poc.Shell/Docking/SnapAssistCandidates.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O | SnapAssistCandidateList, SnapAssistCandidates |
| `Pd.Poc.Shell/Docking/SnapAssistHost.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `Pd.Poc.Shell/Docking/SnapAssistSession.Diag.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(SnapAssistSession.Flow.cs) |  |  |  |
| `Pd.Poc.Shell/Docking/SnapAssistSession.Flow.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `Pd.Poc.Shell/Docking/SnapAssistSession.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `Pd.Poc.Shell/Docking/SnapFlyoutState.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(SnapLayoutCatalog.cs) |  |  |  |
| `Pd.Poc.Shell/Docking/SnapKeyboardCommands.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(SnapLayoutCatalog.cs) |  |  |  |
| `Pd.Poc.Shell/Docking/SnapLayoutCatalog.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `Pd.Poc.Shell/Docking/SnapZoneResolver.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `Pd.Poc.Shell/Docking/SplitAreaCleanup.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(DockLayoutState.cs) |  | O | SplitAreaCleanup |
| `Pd.Poc.Shell/Docking/SplitAreaService.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(DockLayoutState.cs) |  | O | SplitAreaService |
| `Pd.Poc.Shell/Docking/SplitCarryService.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(DockLayoutState.cs) |  | O | SplitCarryService |
| `Pd.Poc.Shell/Docking/SplitLayoutCodec.cs` | Control |  | 직렬화지만 Data 로 내리지 않았다: PocDockFactory·LayoutSerializer 를 참조한다 |  | O | SplitLayoutCodec |
| `Pd.Poc.Shell/Docking/SplitMergeService.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O |  |
| `Pd.Poc.Shell/Docking/SplitPaneInfo.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(PanelArea.cs) |  | O |  |
| `Pd.Poc.Shell/Docking/TabJoinRules.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `Pd.Poc.Shell/Docking/TabSlotMarker.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `Pd.Poc.Shell/Docking/TaskbarTabDrag.cs` | Control |  | 탭 끌기 상태 제어(PanelJoinDrag 연동). axaml 없음, 진단이 상태를 읽는다 | O |  |  |
| `Pd.Poc.Shell/Docking/TitleDragRestore.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O |  |
| `Pd.Poc.Shell/Docking/WindowActivation.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O | WindowActivation |
| `Pd.Poc.Shell/Docking/WindowChromeDiag.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  | WindowChromeDiag |
| `Pd.Poc.Shell/Docking/WindowChromeStyle.Design.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  | WindowChromeStyle |
| `Pd.Poc.Shell/Docking/WindowChromeStyle.Inactive.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  | WindowChromeStyle |
| `Pd.Poc.Shell/Docking/WindowChromeStyle.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  | WindowChromeStyle |
| `Pd.Poc.Shell/Docking/WindowCommands.Origin.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(PocDocument.cs) |  |  |  |
| `Pd.Poc.Shell/Docking/WindowCommands.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O |  |
| `Pd.Poc.Shell/Docking/WindowCommandsPartAttribute.cs` | Control |  | WindowCommands 표식 특성 |  |  | WindowCommandsPartAttribute |
| `Pd.Poc.Shell/Docking/WindowLoadState.Query.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(PocDocument.cs) |  |  |  |
| `Pd.Poc.Shell/Docking/WindowLoadState.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `Pd.Poc.Shell/Docking/WindowMenuOps.Snap.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O | SnapRestoreMemory, ManagedWindowRect |
| `Pd.Poc.Shell/Docking/WindowMenuOps.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O |  |
| `Pd.Poc.Shell/Docking/WindowMinSize.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O |  |
| `Pd.Poc.Shell/Docking/WindowSession.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(ILayoutStore.cs) |  | O | WindowSession |
| `Pd.Poc.Shell/Docking/WindowStateOps.Windows.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(MdiDragProbe.cs) |  | O | WindowStateOps |
| `Pd.Poc.Shell/Docking/WindowStateOps.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O | WindowStateOps |
| `Pd.Poc.Shell/Docking/WindowTabJoinService.DockGuard.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O |  |
| `Pd.Poc.Shell/Docking/WindowTabJoinService.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O |  |
| `Pd.Poc.Shell/IViewRegistry.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `Pd.Poc.Shell/Input/PinchZoom.cs` | Control |  | SettingsInput 이 부르는 입력 처리. 진단이 직접 쓴다 | O |  |  |
| `Pd.Poc.Shell/Input/SettingsInput.cs` | Control |  | 전역 입력 처리기(파일 주석상 컨트롤 계층). App·진단이 직접 부른다 | O |  |  |
| `Pd.Poc.Shell/Input/TouchToolTipGuard.cs` | Control |  | PinchZoom 이 부르는 입력 처리 | O |  |  |
| `Pd.Poc.Shell/Lifecycle/ShellExitSave.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `Pd.Poc.Shell/Lifecycle/ShellStartup.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `Pd.Poc.Shell/Localization/DockStrings.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `Pd.Poc.Shell/Localization/Loc.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `Pd.Poc.Shell/Localization/MenuLabel.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(Loc.cs) |  |  |  |
| `Pd.Poc.Shell/Localization/TrExtension.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `Pd.Poc.Shell/Platform/BackDismissal.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `Pd.Poc.Shell/Platform/DeviceTraits.cs` | Data | Pd.Client.Data/Platform/DeviceTraits.cs | Avalonia 무참조, 참조 형식도 전부 Data |  |  |  |
| `Pd.Poc.Shell/Platform/FittedFlyout.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `Pd.Poc.Shell/Platform/InputFormFactor.cs` | Control |  | 폼팩터 판정 논리. 데이터가 아니고 화면도 아니다 |  |  |  |
| `Pd.Poc.Shell/Platform/ShellPlatform.cs` | Control |  | 트레이·플랫폼 서비스. axaml 없음. 호스트 2곳·진단 2곳이 직접 쓴다 | O |  |  |
| `Pd.Poc.Shell/Platform/SystemDialogHost.cs` | Control |  | 창 수명 서비스(내용은 Control 로 받음, 화면 형식 무참조). 도킹·호스트·진단이 BackgroundMode 를 읽는다 | O |  |  |
| `Pd.Poc.Shell/Platform/TrayMenuModel.cs` | Control |  | 트레이 메뉴 뷰모델. ShellPlatform 과 한 묶음 | O |  |  |
| `Pd.Poc.Shell/Platform/UiDeferred.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `Pd.Poc.Shell/Rendering/RenderInfo.cs` | Control |  | Avalonia 내부를 리플렉션으로 읽는다. 문자열 Avalonia 때문에 Data 규칙도 위반한다 | O |  |  |
| `Pd.Poc.Shell/Theming/BuiltinThemes.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(Loc.cs) |  |  |  |
| `Pd.Poc.Shell/Theming/CellFontDiag.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  | CellFontDiag |
| `Pd.Poc.Shell/Theming/DepthProbeHost.cs` | Data | Pd.Client.Data/Theming/DepthProbeHost.cs | Avalonia 무참조, 참조 형식도 전부 Data |  |  |  |
| `Pd.Poc.Shell/Theming/DesignEffectsApplier.Brushes.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `Pd.Poc.Shell/Theming/DesignEffectsApplier.Image.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `Pd.Poc.Shell/Theming/DesignEffectsApplier.ImageTiming.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `Pd.Poc.Shell/Theming/DesignEffectsApplier.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `Pd.Poc.Shell/Theming/DesignThemeDefinition.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(Loc.cs) |  |  |  |
| `Pd.Poc.Shell/Theming/DesignThemeResolver.Elements.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(ThemeDefinition.cs) |  |  |  |
| `Pd.Poc.Shell/Theming/DesignThemeResolver.Motion.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(DesignThemeDefinition.cs) |  |  |  |
| `Pd.Poc.Shell/Theming/DesignThemeResolver.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(DesignThemeDefinition.cs) |  |  |  |
| `Pd.Poc.Shell/Theming/DesignThemes.Levels.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(DesignThemeDefinition.cs) |  |  |  |
| `Pd.Poc.Shell/Theming/DesignThemes.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(BuiltinThemes.cs) |  |  |  |
| `Pd.Poc.Shell/Theming/FluentDynamicSync.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  | FluentDynamicSync |
| `Pd.Poc.Shell/Theming/MotionEffects.cs` | Data | Pd.Client.Data/Theming/MotionEffects.cs | Avalonia 무참조, 참조 형식도 전부 Data |  |  |  |
| `Pd.Poc.Shell/Theming/PanelFeatureRules.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(UiStyleRules.cs) |  |  |  |
| `Pd.Poc.Shell/Theming/SingleWindowPlatformFix.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `Pd.Poc.Shell/Theming/ThemeApplier.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `Pd.Poc.Shell/Theming/ThemeApplierDiag.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  | ThemeApplierDiag |
| `Pd.Poc.Shell/Theming/ThemeAutomation.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(DesignThemes.Levels.cs) |  |  |  |
| `Pd.Poc.Shell/Theming/ThemeCatalog.Custom.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(Loc.cs) |  |  |  |
| `Pd.Poc.Shell/Theming/ThemeCatalog.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `Pd.Poc.Shell/Theming/ThemeDefinition.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  | ThemeJsonContext |
| `Pd.Poc.Shell/Theming/ThemeDesignApplier.Diag.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `Pd.Poc.Shell/Theming/ThemeDesignApplier.Switch.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `Pd.Poc.Shell/Theming/ThemeDesignApplier.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `Pd.Poc.Shell/Theming/ThemeDesignStyles.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  | ThemeDesignStyles |
| `Pd.Poc.Shell/Theming/ThemeElements.Targets.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(ThemeElements.cs) |  |  |  |
| `Pd.Poc.Shell/Theming/ThemeElements.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `Pd.Poc.Shell/Theming/UiLanguageSettings.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  | UiLanguageSettings |
| `Pd.Poc.Shell/Theming/UiSettings.Parts.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(ChromeInactiveDim.cs) |  |  |  |
| `Pd.Poc.Shell/Theming/UiSettings.Shell.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(MdiMoveLight.Shadow.cs) |  |  |  |
| `Pd.Poc.Shell/Theming/UiSettings.Snap.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `Pd.Poc.Shell/Theming/UiSettings.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `Pd.Poc.Shell/Theming/UiSettingsApplier.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  | UiSettingsApplier |
| `Pd.Poc.Shell/Theming/UiSettingsDto.Motion.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(UiSettingsDto.cs) |  |  |  |
| `Pd.Poc.Shell/Theming/UiSettingsDto.cs` | Control |  | 저장 DTO 지만 Data 로 내리지 않았다: UiSettings 기본값과 제어층 어휘(ChromeStyleKind·UiStyle)를 참조한다. 옮기려면 별도 리팩터링이 필요하다 |  |  | UiSettingsJsonContext |
| `Pd.Poc.Shell/Theming/UiSettingsStore.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  | UiSettingsStore |
| `Pd.Poc.Shell/Theming/UiStyleRules.cs` | Control |  | UI 형 판정 논리 |  |  |  |
| `Pd.Poc.Shell/Theming/UiSystemSettings.Args.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(UiSettings.Parts.cs) |  |  | UiSystemSettings |
| `Pd.Poc.Shell/Theming/UiSystemSettings.Grid.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(UiSettingsDto.Motion.cs) |  |  | UiSystemSettings |
| `Pd.Poc.Shell/Theming/UiSystemSettings.Mdi.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(UiSettingsDto.Motion.cs) |  |  | UiSystemSettings |
| `Pd.Poc.Shell/Theming/UiSystemSettings.Restore.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(UiSettingsDto.Motion.cs) |  |  | UiSystemSettings |
| `Pd.Poc.Shell/Theming/UiSystemSettings.Spread.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(MdiMoveLight.RestoreSpread.cs) |  |  | UiSystemSettings |
| `Pd.Poc.Shell/Theming/UiSystemSettings.Timing.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(ChromeInactiveDim.cs) |  |  | UiSystemSettings |
| `Pd.Poc.Shell/Theming/UiSystemSettings.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(PanelFeatureRules.cs) |  |  | UiSystemSettings |
| `Pd.Poc.Shell/Theming/UiThemeSettings.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  | UiThemeSettings |
| `Pd.Poc.Shell/Theming/UiWindowSettings.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  | UiWindowSettings |
| `Pd.Poc.Shell/Tour/ShortcutCatalog.cs` | Control |  | 단축키 표. 판정기 고정 경로(AC-6-05·6-09) |  |  |  |
| `Pd.Poc.Shell/Tour/TourCatalog.Nav.cs` | Control |  | 투어 단계 표. 판정기 고정 경로(AC-6-08) |  |  | TourNavSteps |
| `Pd.Poc.Shell/Tour/TourCatalog.cs` | Control |  | 투어 단계 표. 판정기 고정 경로(AC-6-01) |  |  |  |
| `Pd.Poc.Shell/Tour/TourCatalogParser.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(ShortcutCatalog.cs) |  |  | TourCatalogParser |
| `Pd.Poc.Shell/Tour/TourConditions.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(App.cs) |  |  |  |
| `Pd.Poc.Shell/Tour/TourController.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `Pd.Poc.Shell/Tour/TourInputGate.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `Pd.Poc.Shell/Tour/TourLayerContent.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(Loc.cs) |  |  | TourLayerContent |
| `Pd.Poc.Shell/Tour/TourScopeResolver.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  | TourScopeResolver |
| `Pd.Poc.Shell/Tour/TourSkipLog.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(TourStep.cs) |  |  | TourSkipLog |
| `Pd.Poc.Shell/Tour/TourStep.cs` | Control |  | TourCatalog 의 레코드. 카탈로그와 같은 층 |  |  |  |
| `Pd.Poc.Shell/Tour/TourTargets.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `Pd.Poc.Shell/ViewModels/BackgroundFallbackNotifier.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  | BackgroundFallbackNotifier |
| `Pd.Poc.Shell/ViewModels/CompactModeViewModel.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(PocDocument.cs) |  |  |  |
| `Pd.Poc.Shell/ViewModels/DesignThemeViewModel.Levels.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(Loc.cs) |  |  |  |
| `Pd.Poc.Shell/ViewModels/DesignThemeViewModel.Motion.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(Loc.cs) |  |  |  |
| `Pd.Poc.Shell/ViewModels/DesignThemeViewModel.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `Pd.Poc.Shell/ViewModels/ExplorerTool.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(MainViewModel.cs) |  | O |  |
| `Pd.Poc.Shell/ViewModels/FavoriteBarViewModel.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `Pd.Poc.Shell/ViewModels/LayoutBackupViewModel.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(Loc.cs) |  |  |  |
| `Pd.Poc.Shell/ViewModels/LayoutManagerViewModel.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(Loc.cs) |  |  |  |
| `Pd.Poc.Shell/ViewModels/LayoutSaveStatusText.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(Loc.cs) |  |  | LayoutSaveStatusText |
| `Pd.Poc.Shell/ViewModels/LayoutTemplatePickerViewModel.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(Loc.cs) |  |  |  |
| `Pd.Poc.Shell/ViewModels/LocalizedOption.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(Loc.cs) |  |  |  |
| `Pd.Poc.Shell/ViewModels/LogTool.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(MainViewModel.cs) |  | O |  |
| `Pd.Poc.Shell/ViewModels/MainViewModel.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(App.cs) |  | O |  |
| `Pd.Poc.Shell/ViewModels/MobileNavViewModel.Selection.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(PocDocument.cs) |  |  |  |
| `Pd.Poc.Shell/ViewModels/MobileNavViewModel.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(Loc.cs) |  |  |  |
| `Pd.Poc.Shell/ViewModels/MonitorMenuViewModel.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(IMonitorProvider.cs) |  |  |  |
| `Pd.Poc.Shell/ViewModels/NotificationPanelViewModel.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(Loc.cs) |  | O |  |
| `Pd.Poc.Shell/ViewModels/NotifyBadgeViewModel.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O |  |
| `Pd.Poc.Shell/ViewModels/OpenWindowListViewModel.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(WindowCommands.Origin.cs) |  |  |  |
| `Pd.Poc.Shell/ViewModels/OpenWindowSlot.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(PocDocument.cs) |  |  |  |
| `Pd.Poc.Shell/ViewModels/Settings/AppearancePageViewModel.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(BuiltinThemes.cs) |  |  |  |
| `Pd.Poc.Shell/ViewModels/Settings/ISettingsRootView.cs` | Control |  | 설정 화면 표식 계약 |  |  |  |
| `Pd.Poc.Shell/ViewModels/Settings/LanguagePageViewModel.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(Loc.cs) |  |  |  |
| `Pd.Poc.Shell/ViewModels/Settings/LayoutPageViewModel.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(MainViewModel.cs) |  |  |  |
| `Pd.Poc.Shell/ViewModels/Settings/SettingsInventory.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(Loc.cs) |  |  |  |
| `Pd.Poc.Shell/ViewModels/Settings/SettingsWindowViewModel.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(Loc.cs) |  |  |  |
| `Pd.Poc.Shell/ViewModels/Settings/SystemPageViewModel.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(UiSettings.Parts.cs) |  |  |  |
| `Pd.Poc.Shell/ViewModels/Settings/WindowPageViewModel.Shell.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(MdiMoveLight.RestoreSpread.cs) |  |  |  |
| `Pd.Poc.Shell/ViewModels/Settings/WindowPageViewModel.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(App.cs) |  |  |  |
| `Pd.Poc.Shell/ViewModels/SettingsPanelViewModel.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(Loc.cs) |  |  |  |
| `Pd.Poc.Shell/ViewModels/ShellContext.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(PocDockFactory.TabGroups.cs) |  |  |  |
| `Pd.Poc.Shell/ViewModels/ShellExtrasViewModel.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `Pd.Poc.Shell/ViewModels/SystemMenu/AboutViewModel.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `Pd.Poc.Shell/ViewModels/SystemMenu/AppMonitorViewModel.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `Pd.Poc.Shell/ViewModels/SystemMenu/InquiryListViewModel.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(Loc.cs) |  |  |  |
| `Pd.Poc.Shell/ViewModels/SystemMenu/InquiryStore.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(App.cs) |  |  |  |
| `Pd.Poc.Shell/ViewModels/SystemMenu/InquiryViewModel.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(Loc.cs) |  |  |  |
| `Pd.Poc.Shell/ViewModels/SystemMenu/LogViewerViewModel.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `Pd.Poc.Shell/ViewModels/SystemMenu/SystemInfo.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `Pd.Poc.Shell/ViewModels/SystemMenuViewModel.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(App.cs) |  |  |  |
| `Pd.Poc.Shell/ViewModels/SystemThemePairViewModel.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(BuiltinThemes.cs) |  |  |  |
| `Pd.Poc.Shell/ViewModels/ThemeDesignRow.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `Pd.Poc.Shell/ViewModels/ThemeDesignViewModel.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `Pd.Poc.Shell/ViewModels/ThemeEditorViewModel.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(Loc.cs) |  |  |  |
| `Pd.Poc.Shell/ViewModels/ThemeTokenRow.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `Pd.Poc.Shell/ViewModels/TitleStripViewModel.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O | O |  |
| `Pd.Poc.Shell/ViewModels/UiStyleViewModel.cs` | Control |  | Avalonia 를 쓰는 비화면 코드: 명령·상태·서비스·도킹 | O |  |  |
| `Pd.Poc.Shell/ViewModels/WindowMenuViewModel.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(App.cs) |  |  |  |
| `Pd.Poc.Shell/ViewRegistry.cs` | Control |  | Avalonia 무참조지만 Data 밖 참조로 Data 불가(IViewRegistry.cs) |  |  |  |
| `Pd.Ui.Views/CaptionClickTiming.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  | CaptionClickTiming |
| `Pd.Ui.Views/ChromeButtons.axaml` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/ChromeButtons.axaml.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/ChromeLogo.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/CrossRootLayoutFlush.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  | CrossRootLayoutFlush |
| `Pd.Ui.Views/DefaultDocumentViewFactory.cs` | Views |  | Pd.Ui.Views 프로젝트 | O | O |  |
| `Pd.Ui.Views/DeferredContentLoader.cs` | Views |  | Pd.Ui.Views 프로젝트 | O | O | DeferredContentLoader |
| `Pd.Ui.Views/Depth/DepthPrewarm.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Depth/DepthTransitions.Open.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Depth/DepthTransitions.cs` | Views |  | Pd.Ui.Views 프로젝트 | O | O |  |
| `Pd.Ui.Views/Depth/SnapshotBackdrop.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  | BackdropLayer, SnapshotBackdrop |
| `Pd.Ui.Views/Depth/SnapshotGeometry.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  | SnapshotGeometry |
| `Pd.Ui.Views/Depth/SnapshotPicture.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  | SnapshotPicture |
| `Pd.Ui.Views/Depth/SnapshotRenderer.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  | SnapshotRenderer |
| `Pd.Ui.Views/Depth/WindowOverview3D.Layout.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Depth/WindowOverview3D.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Depth/WindowTransitions.AfterLayout.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Depth/WindowTransitions.Backdrop.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Depth/WindowTransitions.Clip.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Depth/WindowTransitions.Close.cs` | Views |  | Pd.Ui.Views 프로젝트 | O | O |  |
| `Pd.Ui.Views/Depth/WindowTransitions.CloseCover.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Depth/WindowTransitions.NavShown.cs` | Views |  | Pd.Ui.Views 프로젝트 | O | O |  |
| `Pd.Ui.Views/Depth/WindowTransitions.Probe.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Depth/WindowTransitions.Switch.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Depth/WindowTransitions.cs` | Views |  | Pd.Ui.Views 프로젝트 | O | O |  |
| `Pd.Ui.Views/DesignThemePanel.axaml` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/DesignThemePanel.axaml.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Diagnostics/Gallery/ControlGallery.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Diagnostics/Gallery/GalleryView.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Diagnostics/NotificationPanelDiag.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  | NotificationPanelDiag |
| `Pd.Ui.Views/Diagnostics/PanelPositionMenuDiag.cs` | Views |  | Pd.Ui.Views 프로젝트 | O | O | PanelPositionMenuDiag |
| `Pd.Ui.Views/DocumentPrewarm.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/DocumentTabKeepAlive.cs` | Views |  | Pd.Ui.Views 프로젝트 | O | O |  |
| `Pd.Ui.Views/DocumentTabMenu.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  | DocumentTabMenu |
| `Pd.Ui.Views/DocumentTabSwitchProbe.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  | DocumentTabSwitchProbe |
| `Pd.Ui.Views/DocumentViewLocator.cs` | Views |  | Pd.Ui.Views 프로젝트 | O | O |  |
| `Pd.Ui.Views/Documents/Bench/AllocMark.cs` | Views |  | Pd.Ui.Views 프로젝트 |  |  | AllocMark |
| `Pd.Ui.Views/Documents/Bench/BenchDocumentView.axaml` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Documents/Bench/BenchDocumentView.axaml.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Documents/Bench/BenchFonts.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Documents/Bench/BenchProbe.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Documents/Bench/BenchRemote.cs` | Views |  | Pd.Ui.Views 프로젝트 |  |  |  |
| `Pd.Ui.Views/Documents/Bench/BenchRow.cs` | Views |  | Pd.Ui.Views 프로젝트 |  |  |  |
| `Pd.Ui.Views/Documents/Bench/BenchSignal.cs` | Views |  | Pd.Ui.Views 프로젝트 |  |  |  |
| `Pd.Ui.Views/Documents/Bench/ChartAdapter.LiveCharts.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Documents/Bench/ChartAdapter.OxyPlot.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Documents/Bench/ChartAdapter.ProCharts.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Documents/Bench/ChartAdapter.ScottPlot.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Documents/Bench/ChartAdapters.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Documents/Bench/ChartBenchView.axaml` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Documents/Bench/ChartBenchView.axaml.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Documents/Bench/Dash.Baseline.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Documents/Bench/Dash.LiveCharts.DragGate.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Documents/Bench/Dash.LiveCharts.DragSnapshot.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Documents/Bench/Dash.LiveCharts.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Documents/Bench/Dash.PdTokens.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Documents/Bench/Dash.ScottPlot.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Documents/Bench/Dash.TheOne.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Documents/Bench/DashboardBench.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Documents/Bench/DashboardBenchView.axaml` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Documents/Bench/DashboardBenchView.axaml.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Documents/Bench/DashboardRemote.cs` | Views |  | Pd.Ui.Views 프로젝트 |  |  |  |
| `Pd.Ui.Views/Documents/Bench/DashboardSpec.cs` | Views |  | Pd.Ui.Views 프로젝트 |  |  |  |
| `Pd.Ui.Views/Documents/Bench/GridBenchBands.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  | GridBenchBands |
| `Pd.Ui.Views/Documents/Bench/GridBenchHighlight.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  | GridBenchHighlight |
| `Pd.Ui.Views/Documents/Bench/GridBenchModel.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Documents/Bench/GridBenchPro.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  | GridBenchPro |
| `Pd.Ui.Views/Documents/Bench/GridBenchView.axaml` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Documents/Bench/GridBenchView.axaml.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Documents/Bench/HangulFirstComparer.cs` | Views |  | Pd.Ui.Views 프로젝트 |  |  |  |
| `Pd.Ui.Views/Documents/Bench/IDashboardAdapter.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Documents/BenchEntry.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Documents/FormDocumentView.axaml` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Documents/FormDocumentView.axaml.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Documents/GanttCanvas.Dense.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Documents/GanttCanvas.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Documents/GanttDocumentView.axaml` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Documents/GanttDocumentView.axaml.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Documents/GridCellStrokes.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  | GridCellStrokes |
| `Pd.Ui.Views/Documents/GridColumnDrafts.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  | GridColumnDrafts |
| `Pd.Ui.Views/Documents/GridColumnScaler.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  | GridColumnScaler |
| `Pd.Ui.Views/Documents/GridDenseTarget.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  | GridDenseTarget |
| `Pd.Ui.Views/Documents/GridDocumentView.axaml` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Documents/GridDocumentView.axaml.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Documents/GridDocumentViewModel.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Documents/GridLayoutProbe.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  | GridLayoutProbe |
| `Pd.Ui.Views/Documents/GridOpenBreakdown.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  | GridOpenBreakdown, CellText |
| `Pd.Ui.Views/Documents/GridRouteProbe.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  | GridRouteProbe |
| `Pd.Ui.Views/Documents/GridRow.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  | GridSeed |
| `Pd.Ui.Views/Documents/GridScrollCoalescer.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  | GridScrollCoalescer |
| `Pd.Ui.Views/Documents/GridScrollProbe.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  | GridScrollProbe |
| `Pd.Ui.Views/Documents/GridTouchSelection.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  | GridTouchSelection |
| `Pd.Ui.Views/Documents/PdCellChrome.Text.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  | PdCellChrome, PdCellTextLook |
| `Pd.Ui.Views/Documents/PdCellChrome.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  | PdCellChrome |
| `Pd.Ui.Views/Documents/PdCellTemplate.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  | PdCellTemplate |
| `Pd.Ui.Views/Documents/PdDataGrid.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Documents/PdDataGridStyles.axaml` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Documents/PdDataGridStyles.axaml.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Documents/PdRowTemplate.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  | PdRowTemplate |
| `Pd.Ui.Views/Documents/PdTextColumn.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/EscCloseInput.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/FavoriteBarView.axaml` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/FavoriteBarView.axaml.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/FavoriteListView.axaml` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/FavoriteListView.axaml.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/GridViewPool.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Input/ShortcutBindings.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Input/TabFocusScope.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Input/WindowKeyGestures.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/LayoutBackupView.axaml` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/LayoutBackupView.axaml.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/LayoutManagerView.axaml` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/LayoutManagerView.axaml.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/MacNativeCaption.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  | MacNativeCaption |
| `Pd.Ui.Views/MainTitleBar.axaml` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/MainTitleBar.axaml.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/MainView.FavoriteBar.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/MainView.RecyclePrune.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/MainView.TabClose.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/MainView.TitleOrigin.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/MainView.axaml` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/MainView.axaml.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/MdiLockDragGuard.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  | MdiLockDragGuard |
| `Pd.Ui.Views/MdiMoveHitTest.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  | MdiMoveHitTest |
| `Pd.Ui.Views/MdiMoveLightener.Shadow.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/MdiMoveLightener.cs` | Views |  | Pd.Ui.Views 프로젝트 | O | O |  |
| `Pd.Ui.Views/MdiStaticCache.cs` | Views |  | Pd.Ui.Views 프로젝트 | O | O | MdiStaticCache |
| `Pd.Ui.Views/MdiStaticLight.Restore.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  | MdiStaticLight |
| `Pd.Ui.Views/MdiStaticLight.cs` | Views |  | Pd.Ui.Views 프로젝트 | O | O | MdiStaticLight |
| `Pd.Ui.Views/Menus/FileMenu.axaml` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Menus/FileMenu.axaml.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Menus/SystemMenuItem.axaml` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Menus/SystemMenuItem.axaml.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Menus/ViewMenu.axaml` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Menus/ViewMenu.axaml.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Menus/WindowMenu.axaml` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Menus/WindowMenu.axaml.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/MobileNavView.axaml` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/MobileNavView.axaml.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/MoreWindowsView.axaml` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/MoreWindowsView.axaml.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/NarrowToolChrome.cs` | Views |  | Pd.Ui.Views 프로젝트 | O | O |  |
| `Pd.Ui.Views/NotificationPanelView.axaml` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/NotificationPanelView.axaml.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/OptionComboWidth.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/PanelCornerChrome.cs` | Views |  | Pd.Ui.Views 프로젝트 | O | O | PanelCornerChrome |
| `Pd.Ui.Views/PanelPositionMenu.cs` | Views |  | Pd.Ui.Views 프로젝트 | O | O |  |
| `Pd.Ui.Views/PanelViewState.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  | PanelViewState |
| `Pd.Ui.Views/PdApp.axaml` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/PdApp.axaml.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/PdViewRegistry.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/RootSafeRecyclingDataTemplate.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/SdiDocumentWatch.cs` | Views |  | Pd.Ui.Views 프로젝트 | O | O | SdiDocumentWatch |
| `Pd.Ui.Views/SdiTitleBar.TabJoin.cs` | Views |  | Pd.Ui.Views 프로젝트 | O | O | ManagedSdiTabJoin |
| `Pd.Ui.Views/SdiTitleBar.axaml` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/SdiTitleBar.axaml.cs` | Views |  | Pd.Ui.Views 프로젝트 | O | O |  |
| `Pd.Ui.Views/Settings/AppearancePage.axaml` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Settings/AppearancePage.axaml.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Settings/LanguagePage.axaml` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Settings/LanguagePage.axaml.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Settings/LayoutPage.axaml` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Settings/LayoutPage.axaml.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Settings/SettingsView.axaml` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Settings/SettingsView.axaml.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Settings/SystemPage.axaml` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Settings/SystemPage.axaml.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Settings/WindowPage.axaml` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Settings/WindowPage.axaml.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/ShellStatusBar.axaml` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/ShellStatusBar.axaml.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Snap/SnapAssistLayer.Input.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Snap/SnapAssistLayer.Visuals.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Snap/SnapAssistLayer.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Snap/SnapAssistPicker.axaml` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Snap/SnapAssistPicker.axaml.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Snap/SnapAssistThumbnail.Diag.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Snap/SnapAssistThumbnail.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Snap/SnapFlyoutHover.Pointer.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Snap/SnapFlyoutHover.Target.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Snap/SnapFlyoutHover.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Snap/SnapLayoutBar.axaml` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Snap/SnapLayoutBar.axaml.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Snap/SnapLayoutFlyout.axaml` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Snap/SnapLayoutFlyout.axaml.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Snap/SnapOverlayLayer.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/SystemMenu/AboutView.axaml` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/SystemMenu/AboutView.axaml.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/SystemMenu/AppMonitorView.axaml` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/SystemMenu/AppMonitorView.axaml.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/SystemMenu/ChangelogView.axaml` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/SystemMenu/ChangelogView.axaml.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/SystemMenu/InquiryListView.axaml` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/SystemMenu/InquiryListView.axaml.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/SystemMenu/InquiryView.axaml` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/SystemMenu/InquiryView.axaml.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/SystemMenu/LogViewerView.axaml` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/SystemMenu/LogViewerView.axaml.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/SystemMenu/RestartRequiredView.axaml` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/SystemMenu/RestartRequiredView.axaml.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/SystemMenu/SystemViews.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/SystemMenu/UpdateCheckView.axaml` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/SystemMenu/UpdateCheckView.axaml.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/ThemeDesignPanel.axaml` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/ThemeDesignPanel.axaml.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/ThemeEditorView.axaml` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/ThemeEditorView.axaml.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Themes/CaptionButtons.axaml` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Themes/ChromeButtonStyles.axaml` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Themes/ChromeIcons.axaml` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Themes/ChromeInactive.axaml` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Themes/ChromeInactiveStyles.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Themes/ChromeResources.axaml` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Themes/DataGridTheme.Builtin.axaml` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Themes/DataGridTheme.Pro.axaml` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Themes/DesignEffects.axaml` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Themes/DockJoinTarget.axaml` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Themes/DocumentDockMode.axaml` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Themes/FocusVisual.axaml` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Themes/MacCaptionResources.axaml` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Themes/MdiChrome.axaml` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Themes/MdiDocumentWindowTheme.axaml` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Themes/MdiWindowMenu.axaml` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Themes/MenuIcons.axaml` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Themes/NarrowToolChrome.axaml` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Themes/PinnedDockNoWindow.axaml` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Themes/PinnedDockNoWindowTheme.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Themes/SdiHostWindowTheme.axaml` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/Themes/ThinScrollBar.axaml` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/UiStyleTransition.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/WindowIcon.Bindings.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/WindowIcon.State.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/WindowIcon.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/WindowSystemMenu.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/WindowTitleMenu.cs` | Views |  | Pd.Ui.Views 프로젝트 | O |  |  |
| `Pd.Ui.Views/ZoomDecorator.cs` | Views |  | Pd.Ui.Views 프로젝트 | O | O |  |
| `Pd.Ui/Controls/BusyOverlay.cs` | Ui |  | Pd.Ui 프로젝트 | O |  |  |
| `Pd.Ui/Controls/ClipBorder.cs` | Ui |  | Pd.Ui 프로젝트 | O |  |  |
| `Pd.Ui/Controls/CursorSpinnerFrames.cs` | Ui |  | Pd.Ui 프로젝트 | O |  |  |
| `Pd.Ui/Controls/GanttSpinner.cs` | Ui |  | Pd.Ui 프로젝트 | O |  |  |
| `Pd.Ui/Controls/GanttSpinnerVisual.cs` | Ui |  | Pd.Ui 프로젝트 | O |  | GanttSpinnerMessage, GanttSpinnerVisual |
| `Pd.Ui/Controls/IconSpinnerVisual.cs` | Ui |  | Pd.Ui 프로젝트 | O |  | IconSpinnerMessage, IconSpinnerVisual |
| `Pd.Ui/Controls/PanelCornerButton.cs` | Ui |  | Pd.Ui 프로젝트 | O |  |  |
| `Pd.Ui/Controls/PanelCornerProximity.cs` | Ui |  | Pd.Ui 프로젝트 | O |  |  |
| `Pd.Ui/Controls/PointerFollower.cs` | Ui |  | Pd.Ui 프로젝트 | O |  |  |
| `Pd.Ui/Controls/SettingsRow.cs` | Ui |  | Pd.Ui 프로젝트 | O |  |  |
| `Pd.Ui/Controls/SettingsShell.axaml` | Ui |  | Pd.Ui 프로젝트 | O |  |  |
| `Pd.Ui/Controls/SettingsShell.axaml.cs` | Ui |  | Pd.Ui 프로젝트 | O |  |  |
| `Pd.Ui/Controls/ShortcutLayer.axaml` | Ui |  | Pd.Ui 프로젝트 | O |  |  |
| `Pd.Ui/Controls/ShortcutLayer.axaml.cs` | Ui |  | Pd.Ui 프로젝트 | O |  |  |
| `Pd.Ui/Controls/TemplateParts.cs` | Ui |  | Pd.Ui 프로젝트 |  |  |  |
| `Pd.Ui/Controls/TitleTabPanel.cs` | Ui |  | Pd.Ui 프로젝트 | O |  |  |
| `Pd.Ui/Controls/TitleTabWidth.cs` | Ui |  | Pd.Ui 프로젝트 |  |  |  |
| `Pd.Ui/Controls/TourCard.axaml` | Ui |  | Pd.Ui 프로젝트 | O |  |  |
| `Pd.Ui/Controls/TourCard.axaml.cs` | Ui |  | Pd.Ui 프로젝트 | O |  |  |
| `Pd.Ui/Controls/TourOverlay.axaml` | Ui |  | Pd.Ui 프로젝트 | O |  |  |
| `Pd.Ui/Controls/TourOverlay.axaml.cs` | Ui |  | Pd.Ui 프로젝트 | O |  |  |
| `Pd.Ui/Input/DenseList.cs` | Ui |  | Pd.Ui 프로젝트 | O |  | ListDenseTarget |
| `Pd.Ui/Input/DenseWheel.cs` | Ui |  | Pd.Ui 프로젝트 | O |  |  |
| `Pd.Ui/Motion/MotionPreference.cs` | Ui |  | Pd.Ui 프로젝트 |  |  |  |
| `Pd.Ui/Styles/PdUiStyles.axaml` | Ui |  | Pd.Ui 프로젝트 | O |  |  |
| `Pd.Ui/Text/UiText.cs` | Ui |  | Pd.Ui 프로젝트 |  |  |  |
