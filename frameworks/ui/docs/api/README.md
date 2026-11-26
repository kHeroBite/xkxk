<!-- 생성물 — tools/gen_api_docs.py 가 만든다. 손으로 고치지 않는다 -->

# 별별UI 공개 API 명세

이 폴더는 `tools/gen_api_docs.py` 가 PublicAPI txt(공개 표면 정본)와 소스의 `///` 주석으로 만든다.
손으로 고치지 않는다. 공개 API 가 바뀌면 `python3 -B tools/gen_api_docs.py` 로 다시 만들고 `--check` 로 확인한다.

## 범위

- 공개 계약은 PublicAPI txt 에 실린 형식뿐이다. 현재 네임스페이스: `xkxk.frameworks.ui.shell.Framework`.
- 그 밖의 public 형식(셸 내부 public 등)은 공개 계약이 아니며 예고 없이 바뀔 수 있다. 공개 범위는 B16 에서 다른 라이브러리로 넓힌다.
- 시그니처는 PublicAPI 분석기 표기다. `!` 는 null 아님, `?` 는 null 허용, `-> T` 는 반환·속성 형식이다. 위 네임스페이스 접두는 줄였다.
- 입력: `src/xkxk.frameworks.ui.shell/PublicAPI.Shipped.txt`·`src/xkxk.frameworks.ui.shell/PublicAPI.Unshipped.txt`. 셸 메뉴 ID 표의 부모·종류·순서·명령 ID 열은 `src/xkxk.frameworks.ui.data/Navigation/ShellMenuTable.cs` 에서 읽는다.

## Shipped 와 Unshipped

- Shipped 는 출시된 판에 실린 API 다. 지우거나 서명을 바꾸면 호환이 깨진다.
- Unshipped 는 다음 판에 실릴 API 다. 사전판(0.x 전) 동안은 전부 Unshipped 이며 바뀔 수 있으니 고정 계약으로 가정하지 않는다.
- 현재 줄 수: Shipped 0 · Unshipped 556.

## 호환 규칙 (UADR-0009 8원칙)

1. 확장 입력은 옵션 객체로 받는다
2. 기본값 선택 파라미터를 공개 API 에 두지 않는다
3. 확장점을 미리 연다
4. 소비자가 구현하는 계약은 추상 기반 클래스로 받는다
5. 식별자는 문자열 ID 상수로 둔다
6. 저장 형식에는 버전을 둔다
7. 깨짐은 자동으로 검출한다
8. 폐기는 한 부 버전 이상 예고한다

상세와 자동 검출 방법은 [공개 API 호환 원칙](../../../../docs/standards/02_공개API_호환원칙.md) 에 있다.

## 진단 코드

대역: XKUI00xx 모듈 · XKUI01xx 서비스 · XKUI02xx 아이콘 · XKUI03xx 명령 · XKUI04xx 메뉴. 한 번 쓴 번호는 재사용하지 않는다.

진단 모음은 둘이며 서로 합치지 않는다. 빌드 중 진단은 `XkxkUiApplication.Diagnostics`(빌더 `Build` 결과), 빌드 뒤 실행 중 진단은 `IUiAppContext.Diagnostics`(`XkxkUi.Current.Diagnostics`)에 쌓인다.

| 코드 | 상수 | 대역 | 의미 |
|---|---|---|---|
| `XKUI0001` | `UiDiagnosticIds.ModuleIdEmpty` | 모듈 | 모듈 ID 가 null·공백(Error, 그 모듈은 건너뜀). |
| `XKUI0002` | `UiDiagnosticIds.ModuleDuplicate` | 모듈 | 같은 모듈 ID 가 두 번 등록됨(Error, 뒤 것은 건너뜀). |
| `XKUI0003` | `UiDiagnosticIds.ModuleConfigureFailed` | 모듈 | 모듈 Configure 중 예외(Error, 다음 모듈은 계속). |
| `XKUI0101` | `UiDiagnosticIds.ServiceDuplicate` | 서비스 | 같은 서비스 형식이 두 번 Add 됨(Warning, 첫 등록 유지). |
| `XKUI0201` | `UiDiagnosticIds.IconKeyEmpty` | 아이콘 | 아이콘 키가 null·빈 값·공백(Error, 그 등록은 건너뜀). |
| `XKUI0202` | `UiDiagnosticIds.IconDuplicate` | 아이콘 | 같은 키의 아이콘을 Replace 없이 다시 명시 등록함(Error, 첫 등록 유지). |
| `XKUI0203` | `UiDiagnosticIds.IconSourceInvalid` | 아이콘 | 아이콘 옵션의 PathData·ResourceKey 가 0개 또는 2개(Error, 등록 안 함). |
| `XKUI0204` | `UiDiagnosticIds.IconRenderFailed` | 아이콘 | 해석 시 PathData 그리기 실패(Warning, 키당 1회, 실행 중 진단). |
| `XKUI0205` | `UiDiagnosticIds.IconProviderRejected` | 아이콘 | 아이콘 공급자 Id 가 빈 값이거나 이미 같은 Id 공급자가 있음(Error, 등록 안 함). |
| `XKUI0206` | `UiDiagnosticIds.IconProviderIdReserved` | 아이콘 | 프레임워크가 아닌 주체가 예약 접두어(xkxk.) 공급자 Id 를 등록함(Error, 등록 안 함). |
| `XKUI0207` | `UiDiagnosticIds.IconRemoveDenied` | 아이콘 | 다른 주체가 등록한 명시 아이콘·공급자를 지우려 함(Error, 지우지 않음). |
| `XKUI0301` | `UiDiagnosticIds.CommandIdEmpty` | 명령 | 명령 ID 가 null·빈 값·공백(Error, 그 등록은 건너뜀). |
| `XKUI0302` | `UiDiagnosticIds.CommandDuplicate` | 명령 | 같은 ID 의 명령을 Replace 없이 다시 등록함(Error, 첫 등록 유지). |
| `XKUI0303` | `UiDiagnosticIds.CommandExecuteMissing` | 명령 | 명령 옵션에 Execute 가 없음(Error, 등록 안 함). |
| `XKUI0304` | `UiDiagnosticIds.CommandIconMissing` | 명령 | 명령의 아이콘 키를 해석하지 못함(Error, 명령은 유지하고 아이콘 없이 표시). |
| `XKUI0305` | `UiDiagnosticIds.CommandGestureInvalid` | 명령 | 명령 단축키가 별별UI 단축키 문법에 맞지 않음(Error, 단축키만 버리고 명령 유지). |
| `XKUI0306` | `UiDiagnosticIds.CommandFailed` | 명령 | 명령 실행 중 Execute·CanExecute 대리자 예외(Error, 실행 결과 false). |
| `XKUI0307` | `UiDiagnosticIds.CommandNotFound` | 명령 | 없는 ID 의 명령을 실행하려 함(Warning, 실행 중 진단). |
| `XKUI0308` | `UiDiagnosticIds.CommandIdReserved` | 명령 | 프레임워크가 아닌 주체가 아직 없는 예약 접두어(xkxk.) 명령 ID 를 새로 만들려 함(Error, 등록 안 함). |
| `XKUI0309` | `UiDiagnosticIds.CommandRemoveDenied` | 명령 | 다른 주체가 등록한 명령을 지우려 함(Error, 지우지 않음). |
| `XKUI0310` | `UiDiagnosticIds.CommandGestureConflict` | 명령 | 프레임워크가 아닌 주체 명령의 단축키가 이미 있는 명령의 단축키와 같음(Warning, 명령은 등록하고 먼저 등록한 쪽이 키를 받음). |
| `XKUI0401` | `UiDiagnosticIds.MenuIdEmpty` | 메뉴 | 메뉴 ID 가 null·빈 값·공백(Error, 그 등록은 건너뜀). |
| `XKUI0402` | `UiDiagnosticIds.MenuDuplicate` | 메뉴 | 같은 ID 의 메뉴 항목을 Replace 없이 다시 등록함(Error, 첫 등록 유지). |
| `XKUI0403` | `UiDiagnosticIds.MenuParentMissing` | 메뉴 | 부모 메뉴가 없거나 하위 메뉴 종류가 아님(Error, 등록 안 함). |
| `XKUI0404` | `UiDiagnosticIds.MenuAnchorMissing` | 메뉴 | 앞·뒤 삽입 기준 항목이 같은 부모 아래에 없음(Warning, 맨 뒤에 붙임). |
| `XKUI0405` | `UiDiagnosticIds.MenuCommandMissing` | 메뉴 | 명령 메뉴 항목의 명령 ID 가 명령 레지스트리에 없음(Error, 항목은 유지하고 비활성으로 표시). |
| `XKUI0406` | `UiDiagnosticIds.MenuKindInvalid` | 메뉴 | 항목 종류와 옵션이 맞지 않음 — 명령 항목에 명령 ID 없음·섹션에 섹션 키 없음·구분선에 명령 ID 있음 등(Error, 등록 안 함). |
| `XKUI0407` | `UiDiagnosticIds.MenuIdReserved` | 메뉴 | 프레임워크가 아닌 주체가 아직 없는 예약 접두어(xkxk.) 메뉴 ID 를 새로 만들려 함(Error, 등록 안 함). |
| `XKUI0408` | `UiDiagnosticIds.MenuRemoveDenied` | 메뉴 | 다른 주체의 메뉴 항목(또는 다른 주체의 자식이 있는 하위 메뉴)을 지우려 함(Error, 지우지 않음). |
| `XKUI0409` | `UiDiagnosticIds.MenuSectionUnknown` | 메뉴 | 화면이 모르는 섹션 키(Warning, 화면당 키별 1회, 그 자리는 비움). |
| `XKUI0410` | `UiDiagnosticIds.MenuMoveInvalid` | 메뉴 | 메뉴 이동 대상이 없거나 순환이 생기거나 새 부모가 하위 메뉴가 아님(Error, 옮기지 않음). |
| `XKUI0411` | `UiDiagnosticIds.MenuConditionFailed` | 메뉴 | 메뉴의 표시 조건·툴팁 대리자 예외(Warning, 항목·키당 1회, 조건 예외는 숨김·툴팁 예외는 툴팁 없음). |

## 그룹 파일

| 파일 | 형식 |
|---|---|
| [01 진입·빌더·모듈](01_진입_빌더_모듈.md) | `XkxkUi`·`XkxkUiBuilder`·`XkxkUiBuilderOptions`·`XkxkUiApplication`·`XkxkUiAppBuilderExtensions`·`UiHostOptions`·`UiHostKinds`·`UiModule`·`IUiModule`·`IUiModuleContext`·`UiServiceRegistry`·`IUiServiceProvider` |
| [02 앱컨텍스트·문구](02_앱컨텍스트_문구.md) | `IUiAppContext`·`IUiLocalizer`·`UiLanguage` |
| [03 아이콘](03_아이콘.md) | `IUiIconRegistry`·`UiIconProvider`·`UiIconDefinition`·`UiIconOptions`·`UiIconKind` |
| [04 명령](04_명령.md) | `IUiCommandRegistry`·`UiCommandOptions`·`UiCommandDefinition`·`UiCommandContext` |
| [05 메뉴](05_메뉴.md) | `IUiMenuRegistry`·`UiMenuItemOptions`·`UiMenuItemDefinition`·`UiMenuItemKind`·`UiMenuItemState`·`UiMenuMobileOptions`·`UiMenuMobileDefinition`·`UiMenuPlacementOptions`·`UiMenuContext`·`UiMenuSurface` |
| [06 진단·공통](06_진단_공통.md) | `UiDiagnostics`·`UiDiagnostic`·`UiDiagnosticSeverity`·`UiDiagnosticIds`·`UiErrorPolicy`·`UiConfigurationException`·`UiRegistryOwners`·`UiRegistryChangeKind`·`UiRegistryChangedEventArgs` |
| [07 셸명령ID](07_셸명령ID.md) | `ShellCommandIds` |
| [08 셸메뉴ID](08_셸메뉴ID.md) | `ShellMenuIds` |

## 통계

- PublicAPI 줄(`#` 줄 제외) 556 · 형식 45 · 멤버 511 · 설명 없는 멤버 0.
