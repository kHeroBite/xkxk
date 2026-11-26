<!-- 저장소 공유 라이브러리의 공개 API 호환 원칙 8개와 자동 검출 방식을 정하는 표준 문서 -->
# 공개 API 호환 원칙

> 상태: 확정 · 최종 갱신일: 2026-11-26
> 역할: 저장소의 모든 공유 라이브러리가 공개 API 를 만들고 바꿀 때 따르는 호환 원칙과 깨짐 검출 방식을 정한다.
> 위치 근거: 공개 API 호환은 별별UI 만의 문제가 아니라 `libraries/`·`modules/` 등 저장소의 모든 공유 라이브러리에 같은 규칙이 필요하다. 소스 구조 원칙을 같은 자리로 올린 선례(커밋 25c31b6)를 따른다. 별별UI 고유 적용(분석기 범위·네임스페이스)은 `/mnt/c/DATA/Project/xkxk/frameworks/ui/plan/05_아키텍처/02_별별UI_프레임워크화_계획.md` §4.2 가 맡는다.
> 결정 근거: `/mnt/c/DATA/Project/xkxk/frameworks/ui/plan/90_결정관리/02_결정기록_ADR.md` 의 UADR-0009, 요구 REQ-122.

## 1. 목적과 사용자 원문
- 공개 API 는 한 번 내보내면 소비자 코드와 이미 빌드된 어셈블리가 의존한다. 그래서 처음부터 바꿀 일이 적게 만들고, 바꿔야 할 때도 이전 사용법이 그대로 동작하게 한다.
- 사용자 원문(2026-11-26, RAW-004)은 다음과 같다.

> 표준api를 만들때 나중에 최대한 변경이 없어도 될 정도로 완벽하고 모든 경우의수에 대해 미리 확장을 한 개념으로 만들어달라. 그리고 확장하더라도 신규기능이나 파라미터는 기본값을 지정해서 이전 api와의 호환성도 100%확보하는 방향으로 구현하라.

- 이 문서는 위 요구를 8원칙으로 옮긴다. 원칙을 어기면 검출 장치(4절)가 먼저 막는다.

## 2. 용어
| 용어 | 뜻 |
|---|---|
| 소스 호환 | 이전 버전에 맞춰 쓴 소비자 소스가 새 버전에서 수정 없이 다시 컴파일된다. |
| 바이너리 호환 | 이전 버전에 맞춰 빌드된 소비자 어셈블리가 새 버전 라이브러리와 다시 빌드하지 않고 로드·실행된다. |
| 동작 호환 | 같은 입력에 같은 결과가 나온다. 시험으로만 보증한다(6절). |
| Shipped | 게시를 확정해 더는 줄이거나 바꾸지 않는 공개 API 목록(`PublicAPI.Shipped.txt`). |
| Unshipped | 아직 게시 전이라 바뀔 수 있는 공개 API 목록(`PublicAPI.Unshipped.txt`). |

- 100% 호환의 목표는 소스·바이너리·동작 셋이다. 소스만 맞고 바이너리가 깨지는 변경도 위반으로 본다.

## 3. 원칙

### 원칙 1 확장 입력은 옵션 객체로 받는다
- 여러 설정을 받는 진입점은 속성에 기본값이 있는 옵션 클래스 하나를 받는다. 새 설정은 속성만 더한다.
- 옵션 클래스는 `sealed` 로 두고 생성자는 인자 없는 것 하나만 둔다. 속성 대입에 null 이 오면 안전한 기본값으로 바꾼다.
```csharp
// 하지 말 것 — 설정이 늘 때마다 시그니처가 바뀐다.
public static Builder Create(string id, bool strict, int retry);
// 할 것 — 새 설정은 옵션 속성으로만 더한다.
public static Builder Create(BuilderOptions options);
public sealed class BuilderOptions { public bool Strict { get; set; } /* 기본 false */ }
```

### 원칙 2 기본값 선택 파라미터를 공개 API 에 두지 않는다
- 새 입력은 옵션 속성 또는 오버로드로만 더한다. 기존 메서드에 기본값 있는 파라미터를 붙이지 않는다.
- 근거 1. Microsoft 문서 원문은 "changing a method's signature by adding a new parameter will cause assemblies compiled against the older version of the library to throw a MissingMethodException." 이다(출처 7절 1번).
- 근거 2. C# 은 기본값을 호출부 컴파일 때 호출 코드에 박는다. 그래서 라이브러리의 기본값만 바꿔도 다시 빌드하지 않은 소비자는 옛 값을 계속 쓰고, 파라미터를 더하면 옛 시그니처 호출이 사라진다(출처 7절 2번).
```csharp
// 하지 말 것 — 호출부에 기본값이 컴파일되어 들어가고, 파라미터 추가가 바이너리를 깨뜨린다.
public void Open(string name, bool readOnly = false);
// 할 것 — 오버로드는 그대로 두고 새 입력은 옵션 객체 속성으로 더한다.
public void Open(string name);
public void Open(string name, OpenOptions options);
```
- 선택 파라미터 0건의 강제 수단은 계약 시험의 리플렉션 `IsOptional` 검사다(4절).

### 원칙 3 확장점을 미리 연다
- 앞으로 기능이 붙을 자리를 처음 공개 API 에 만들어 둔다. 서비스 등록부, 옵션의 속성 모음(`IDictionary<string, object?>`), 기본 구현이 있는 `virtual` 멤버 자리가 그것이다.
- 자리를 열되 쓰이지 않는 기능을 미리 구현하지 않는다(6절).
```csharp
public sealed class UiServiceRegistry { public UiServiceRegistry Add<T>(T instance) where T : class; }
public sealed class BuilderOptions { public IDictionary<string, object?> Properties { get; } }
```

### 원칙 4 소비자가 구현하는 계약은 추상 기반 클래스로 받는다
- 인터페이스에 멤버를 더하면 그것을 구현한 소비자 형식이 깨진다. Microsoft 문서 원문은 "Adding anything to an interface will cause existing types that implement it to fail." 이고 "CONSIDER using abstract base classes instead of interfaces." 를 권한다(출처 7절 1번).
- 소비자가 구현하는 계약은 추상 기반 클래스로 입력받는다. 같은 이름의 인터페이스가 필요하면 형식만 남기고 **동결**(멤버 추가 금지)한다. 확장은 기반 클래스의 기본 구현 있는 `virtual` 멤버나 별도 선택 인터페이스(`is` 검사)로만 한다.
- 별별UI 예. `AddModule(UiModule)` 은 추상 기반 `UiModule` 만 받고, `IUiModule`(`Id`·`Configure` 2개)은 동결한다.
- 프레임워크만 구현하는 인터페이스(`IUiModuleContext`·`IUiServiceProvider`)는 멤버 추가를 허용한다. 대신 XML 문서에 "프레임워크만 구현, 소비자 구현 금지" 를 적는다.
- 기본 인터페이스 멤버(C# 8)는 Microsoft 해당 페이지에 언급이 없고 대상 런타임별 지원을 이 저장소에서 실측하지 않았다. 따라서 보조 수단이며 3호스트 런타임 지원을 실측한 뒤에만 쓴다.
```csharp
// 하지 말 것 — 소비자가 구현하는 인터페이스에 멤버를 더한다.
public interface IModule { string Id { get; } int Order { get; } /* 추가 → 기존 구현체가 깨진다 */ }
// 할 것 — 추상 기반에 기본 구현 있는 virtual 멤버를 더한다.
public abstract class ModuleBase { public abstract string Id { get; } public virtual int Order => 0; }
```

### 원칙 5 식별자는 문자열 ID 상수로 둔다
- 모듈 ID·진단 코드·종류 이름은 문자열 상수로 공개한다. 번호는 한 번 쓰면 재사용하지 않는다.
- 열거형은 알 수 없는 값(옛 소비자가 모르는 새 값, 정의 밖 캐스트)이 와도 안전하게 처리한다. 값 추가는 허용하되 기존 값의 숫자를 바꾸지 않는다.
```csharp
public static class DiagnosticIds { public const string ModuleDuplicate = "XKUI0002"; }
public enum ErrorPolicy { Collect = 0, Throw = 1 } // 정의 밖 값은 Collect 로 취급
```

### 원칙 6 저장 형식에는 버전을 둔다
- 파일·설정·네트워크로 내보내는 형식에는 형식 버전 값을 넣고, 읽는 쪽은 옛 버전을 계속 읽는다. 알 수 없는 새 필드는 무시한다.
- 새 저장 형식이 없는 변경에는 해당 없음이라고 문서에 명시한다.
```csharp
public sealed class LayoutFile { public int Version { get; set; } = 1; }
```

### 원칙 7 깨짐은 자동으로 검출한다
- 사람이 보고 판단하지 않는다. PublicAPI 분석기, 표면 검사 스크립트, 계약 시험, 일괄 실행기가 깨짐을 막는다(4절).
- 공개 API 를 더하면 `PublicAPI.Unshipped.txt` 갱신이 빌드 오류로 요구된다. 그래서 의도하지 않은 공개와 삭제가 드러난다.
```csharp
// 새 공개 멤버를 추가하면 PublicAPI.Unshipped.txt 에 선언하지 않는 한 RS0016 이 빌드를 막는다.
public int Count { get; }
```

### 원칙 8 폐기는 한 부 버전 이상 예고한다
- 없애려는 API 는 `[Obsolete("대체 API 이름과 제거 예정 주 버전")]` 로 표시하고 최소 한 부 버전 동안 유지한다. 제거는 주 버전에서만 한다.
- 지원 범위는 최신과 직전 부 버전이다(UADR-0007 ⑬). 예고 기간 안에도 옛 API 의 동작은 바꾸지 않는다.
```csharp
[Obsolete("OpenAsync 를 쓰라. 3.0 에서 제거한다.")]
public void Open(string name);
```

## 4. 자동 검출
### 4.1 PublicAPI 분석기
- 패키지 `Microsoft.CodeAnalysis.PublicApiAnalyzers` 를 쓴다. 규칙은 다음과 같다(출처 7절 3번).

| 규칙 | 막는 것 | 보고 위치 | 걸어 두는 방식 |
|---|---|---|---|
| RS0016 | 선언 안 된 공개 API 추가 | 소스(.cs) | `.editorconfig` 의 경로 한정 error |
| RS0017 | 선언된 공개 API 삭제 | PublicAPI txt(추정) | csproj `WarningsAsErrors` |
| RS0026 | 선택 파라미터를 가진 공개 오버로드가 여럿 | 소스 | 경로 한정 error |
| RS0027 | 선택 파라미터 API 가 오버로드 중 파라미터가 가장 많지 않음 | 소스 | 경로 한정 error |
| RS0036 | nullable 주석 없는 공개 API | 소스 | 경로 한정 error |
| RS0037 | txt 에 `#nullable enable` 없음 | PublicAPI txt(추정) | csproj `WarningsAsErrors` |

- RS0016·RS0026·RS0027·RS0036 은 소스 위치에 보고되므로 `.editorconfig` 에서 공개 계약 폴더에만 error 로 건다.
- RS0017·RS0037 은 txt 위치에 보고될 가능성이 높아(추정, 첫 빌드에서 실증) 경로 한정이 듣지 않을 수 있다. 그래서 프로젝트 단위 `WarningsAsErrors` 로 건다. txt 에 계약만 선언해 두면 사실상 계약 한정으로 동작한다. 실증에 실패하면 이 방식을 빼고 4.3 표면 검사만으로 판정하며 결정권자에게 보고한다.
- RS0026·RS0027 의 정확한 역할은 위 표와 같다. 선택 파라미터 메서드가 **하나**뿐이면 두 규칙 모두 통과시킨다. 따라서 "선택 파라미터 0건" 은 분석기가 아니라 계약 시험이 강제한다.

### 4.2 계약 시험
- 공개 형식 정보만 읽는 콘솔 시험 프로젝트가 다음을 확인한다. 이 프로젝트는 `InternalsVisibleTo` 와 `BindingFlags.NonPublic` 을 쓰지 않는다.
  - 공개 형식·메서드·생성자 파라미터 중 `IsOptional` 이 0건이다(원칙 2).
  - 동결 인터페이스의 공개 멤버 수가 고정이다(원칙 4).
  - 소비자가 만들면 안 되는 형식의 공개 생성자가 0개이다(생성자를 명시하지 않으면 암묵 공개 기본 생성자가 생긴다).
  - 기본 옵션 값, 진단 코드, 알 수 없는 열거 값 처리 같은 동작(원칙 1·3·5).

### 4.3 표면 검사 스크립트
- `PublicAPI.Shipped.txt`·`PublicAPI.Unshipped.txt` 를 기준 커밋(기본 HEAD)의 같은 파일과 비교한다. 기준선에 있던 줄이 사라지면 실패한다. 변경은 삭제와 추가로 잡힌다.
- Unshipped 변경은 `--allow-unshipped-change`, Shipped 삭제는 `--allow-major` 명시 플래그가 있을 때만 경고로 낮춘다. 기준에 파일이 없으면(첫 도입) 비교 대상 없음으로 통과하고 그 사실을 출력한다.
- 빌드 로그를 주면 `RS0016`·`RS0017`·`RS0026`·`RS0027`·`RS0036`·`RS0037` 진단이 0건인지도 본다.

### 4.4 Shipped·Unshipped 운용과 패키지 검증
- 새 공개 API 는 Unshipped 에 적는다. 게시를 확정(Shipped 확정)하면 Unshipped 줄을 Shipped 로 옮기고 그 뒤로는 줄이거나 바꾸지 않는다.
- 패키지 검증(`EnablePackageValidation` + baseline)은 이전에 게시한 nupkg 가 있어야 비교된다. 따라서 baseline 비교는 첫 pack 부터 시작한다.
- 분석기 저장소 `dotnet/roslyn-analyzers` 는 2026-11-12 보관되었다. 후속 위치는 확인 필요다.

## 5. 버전과 폐기
- 버전은 주.부.패치다. 주 버전만 호환을 깨는 변경(제거·시그니처 변경)을 담는다. 부 버전은 추가만, 패치는 동작 수정만 담는다.
- 폐기 절차는 원칙 8 을 따른다. 제거 전 최소 한 부 버전 동안 `[Obsolete]` 로 예고한다.
- 지원 범위는 최신과 직전 부 버전이다(UADR-0007 ⑬).
- 호환을 깨야 하면 먼저 새 이름의 API 를 더하고 옛 API 를 폐기 표시한다. 같은 이름의 의미를 바꾸지 않는다.

## 6. 한계
- "모든 경우의 수에 대해 미리 확장" 은 확장 **자리**를 미리 만든다는 뜻이다. 기능을 미리 다 만드는 것이 아니다. 쓰이지 않는 기능도 공개되면 바꾸기 어려운 공개 API 가 되어 오히려 변경 부담을 키운다.
- 동작 호환은 시험으로만 보증한다. 분석기와 표면 검사는 시그니처 호환만 본다.
- 검출 한계가 있다.
  - 모든 API 가 Unshipped 인 동안은 삭제·변경이 PublicAPI txt 수정과 명시 허용 플래그로 드러나는 수준이다.
  - 기준 커밋(HEAD) 비교는 커밋 전 작업 트리 변경만 잡는다. 허용 플래그로 커밋된 변경은 뒤에서 다시 잡지 못하고 git 이력으로만 드러난다.
  - Shipped 기준 차단은 Shipped 확정 뒤에, 패키지 바이너리 비교는 첫 pack 뒤에 성립한다.
- 리플렉션·동적 코드로 공개 API 를 우회하는 소비자 코드는 이 원칙의 보증 밖이다.

## 7. 출처
1. Microsoft, Breaking changes and .NET libraries — https://learn.microsoft.com/en-us/dotnet/standard/library-guidance/breaking-changes (2026-11-26 확인)
2. dotnet/roslyn, Adding Optional Parameters in Public API — https://github.com/dotnet/roslyn/blob/main/docs/Adding%20Optional%20Parameters%20in%20Public%20API.md
3. PublicApiAnalyzers 규칙 문서 — https://raw.githubusercontent.com/dotnet/roslyn-analyzers/main/src/PublicApiAnalyzers/Microsoft.CodeAnalysis.PublicApiAnalyzers.md
4. NuGet `Microsoft.CodeAnalysis.PublicApiAnalyzers` 5.6.0 — https://www.nuget.org/packages/Microsoft.CodeAnalysis.PublicApiAnalyzers (2026-11-26 v3 API 로 최신 안정판 확인)
5. Microsoft, RuntimeFeature.DefaultImplementationsOfInterfaces — https://learn.microsoft.com/en-us/dotnet/api/system.runtime.compilerservices.runtimefeature.defaultimplementationsofinterfaces
