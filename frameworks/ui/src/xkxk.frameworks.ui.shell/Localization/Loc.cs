// 다국어 서비스(Q17) — 의미 단위 키 → 현재 언어 문자열. 내장 JSON 사전(영역별 <area>.<lang>.json)을 읽고, 언어를 바꾸면 모든 화면이 즉시 갱신된다
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace xkxk.frameworks.ui.shell.Localization;

/// <summary>
/// 언어 사전 서비스(싱글턴). 사용법:
/// <list type="bullet">
/// <item>XAML: <c>xmlns:loc="using:xkxk.frameworks.ui.shell.Localization"</c> 뒤 <c>Text="{loc:Tr menu.window.autosave}"</c> — 언어를 바꾸면 즉시 다시 그린다.</item>
/// <item>코드: <c>Loc.Tr("menu.window.autosave")</c>, 형식 문자열은 <c>Loc.Tr("status.docCount", n)</c>(사전 값의 {0}).</item>
/// <item>뷰모델 바인딩: <c>Loc.Instance[key]</c> 인덱서 — 언어가 바뀌면 PropertyChanged("Item[]")·("Item") 통지.</item>
/// </list>
/// 사전: 셸 어셈블리에 내장된 <c>Localization/&lt;영역&gt;.&lt;언어&gt;.json</c>(평면 {"키":"값"}). 언어 추가 = 영역 파일을 그 언어 코드로 더하는 것뿐이다(zh·ja 등).
/// 누락: 현재 언어에 없으면 기본 언어(ko) 값을 쓰고 경고를 1회 남긴다. ko 에도 없으면 빈 문자열(키 문자열은 화면에 내보내지 않는다).
/// 모든 호출은 UI 스레드에서 한다(언어 변경 통지가 UI 바인딩을 갱신한다).
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    /// <summary>기본·대체 언어(사용자 원본 언어).</summary>
    public const string DefaultLanguage = "ko";

    private static readonly Dictionary<string, string> Cultures = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ko"] = "ko-KR", ["en"] = "en-US", ["zh"] = "zh-CN", ["ja"] = "ja-JP",
    };

    private readonly Dictionary<string, Dictionary<string, string>> _dict = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _warned = new(StringComparer.Ordinal);
    private string _language = DefaultLanguage;
    private bool _notifying; // Language 변경 통지 중(재진입 쓰기 차단용)

    private Loc()
    {
        LoadEmbedded(typeof(Loc).Assembly);
        // 기동 언어(ko)의 문화권을 처음부터 적용한다(D-CULTURE: 이전엔 Language 를 바꿀 때만 적용해 OS·브라우저 로캘 형식이 남았다).
        // 생성 시점의 스레드가 UI 스레드라는 보장이 없어 디스패처는 건드리지 않는다 — 기본 스레드 문화권(DefaultThread*)이 흐름 밖 값을 맡는다.
        ApplyCulture(_language);
    }

    // ★ 정적 필드 초기화는 선언 순서대로 돈다 — Warn 은 반드시 Instance 보다 먼저 선언(생성자 안 LoadEmbedded 가 Warn 을 부른다).
    //   00:27 기동 실패(TypeInitializationException ← NRE, odev-4b 실측)의 원인이었다.

    /// <summary>경고 기록(누락 키·중복 키). 기본은 Console(호스트 로그로 들어간다).</summary>
    public static Action<string> Warn { get; set; } = m => Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [loc] {m}");

    public static Loc Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>언어가 바뀐 뒤(새 값 반영 후) 알림 — 코드로 그리는 화면·형식 문자열 갱신용.</summary>
    public event Action? LanguageChanged;

    /// <summary>사전이 있는 언어(코드·자기 언어 이름 "lang.self"). 기본 언어가 먼저.</summary>
    public IReadOnlyList<(string Code, string Name)> Languages =>
        _dict.Keys.OrderBy(k => k == DefaultLanguage ? 0 : 1).ThenBy(k => k, StringComparer.Ordinal)
            .Select(k => (k, _dict[k].GetValueOrDefault("lang.self", k))).ToList();

    /// <summary>기본 언어 사전의 전체 키(검사·원격 진단용).</summary>
    public IReadOnlyCollection<string> AllKeys => _dict.GetValueOrDefault(DefaultLanguage)?.Keys ?? (IReadOnlyCollection<string>)Array.Empty<string>();

    /// <summary>현재 언어 코드(ko·en …). 사전이 없는 코드는 기본 언어로 바뀐다. 바꾸면 문화권(날짜·숫자 형식)도 함께 바뀐다.</summary>
    public string Language
    {
        get => _language;
        set
        {
            var code = Normalize(value);
            if (string.Equals(code, _language, StringComparison.OrdinalIgnoreCase))
            {
                // 같은 언어라도 문화권은 다시 맞춘다(D-CULTURE: 기동 언어 ko 를 다시 골라도 숫자 형식이 OS·브라우저 로캘로 남았다).
                FixCulture(code);
                return;
            }

            // 변경 통지 도중 다시 들어온 쓰기는 무시한다(Q17 웹·Android 실측: lang.set en → ko 로 되돌아감).
            // 통지가 언어 콤보의 선택지 목록(LanguageNames)을 바꾸면, 콤보가 같은 문자열 "한국어" 선택을 유지한 채
            // 옛 순번 0 을 양방향 바인딩으로 되써서 언어를 ko 로 되돌린다. 순번 갱신은 통지 끝(LanguageIndex)에서 따로 온다.
            if (_notifying)
            {
                WarnOnce("reentrant:" + code, $"언어 변경 통지 중 재진입 쓰기 무시 요청={code} 현재={_language}");
                return;
            }

            _language = code;
            FixCulture(code);
            _notifying = true;
            try
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]")); // 인덱서 바인딩 전체 갱신(Avalonia·WPF 관례 둘 다)
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item"));
                LanguageChanged?.Invoke();
            }
            finally
            {
                _notifying = false;
            }
        }
    }

    /// <summary>현재 언어 문자열(뷰모델·코드 바인딩용 인덱서).</summary>
    public string this[string key] => Get(key);

    /// <summary>현재 언어 문자열.</summary>
    public static string Tr(string key) => Instance.Get(key);

    /// <summary>현재 언어 형식 문자열(사전 값의 {0}·{1} …를 현재 문화권으로 채운다).</summary>
    public static string Tr(string key, params object?[] args)
    {
        var format = Instance.Get(key);
        try
        {
            return string.Format(CultureInfo.CurrentCulture, format, args);
        }
        catch (FormatException)
        {
            Instance.WarnOnce("format:" + key, $"형식 오류 키={key} 값={format}");
            return format;
        }
    }

    /// <summary>키가 기본 언어 사전에 있는가.</summary>
    public bool HasKey(string key) => _dict.TryGetValue(DefaultLanguage, out var d) && d.ContainsKey(key);

    /// <summary>기본 언어(ko)에는 있는데 lang 에는 없는 키(번역 누락). lang 사전이 없으면 전체 키.</summary>
    public IReadOnlyList<string> MissingKeys(string lang)
    {
        var target = _dict.GetValueOrDefault(Normalize(lang, keepUnknown: true));
        return AllKeys.Where(k => target is null || !target.ContainsKey(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();
    }

    /// <summary>키 1개를 관찰: 구독 즉시 현재 값, 이후 언어가 바뀔 때마다 새 값(TrExtension 이 바인딩으로 쓴다).</summary>
    public IObservable<string> Observe(string key) => new KeyObservable(this, key);

    private string Get(string key)
    {
        if (_dict.TryGetValue(_language, out var cur) && cur.TryGetValue(key, out var v))
        {
            return v;
        }

        if (_dict.TryGetValue(DefaultLanguage, out var def) && def.TryGetValue(key, out var d))
        {
            WarnOnce(_language + ":" + key, $"번역 누락 lang={_language} key={key} → {DefaultLanguage} 값 사용");
            return d;
        }

        WarnOnce("none:" + key, $"사전에 없는 키 key={key} → 빈 문자열");
        return "";
    }

    private void WarnOnce(string id, string message)
    {
        if (_warned.Add(id))
        {
            Log(message);
        }
    }

    private string Normalize(string? code, bool keepUnknown = false)
    {
        var c = (code ?? "").Trim().ToLowerInvariant();
        var dash = c.IndexOfAny(new[] { '-', '_' });
        if (dash > 0) { c = c[..dash]; } // ko-KR → ko
        return _dict.ContainsKey(c) || keepUnknown ? c : DefaultLanguage;
    }

    /// <summary>
    /// 문화권을 지금 적용하고 UI 디스패처 작업으로 한 번 더 적용한다.
    /// 문화권은 실행 흐름(ExecutionContext)을 따라 되돌아간다: async 메서드 안에서 바꾸면 그 메서드가 끝날 때 호출 쪽 값으로 복원된다.
    /// 기동 시 설정 복원(UiSettings.LoadAsync → await 뒤 Language=dto)이 그 경우라, 재시작하면 language=en 인데 culture=ko-KR 로 남았다
    /// (i0136_diag 01:53 데스크톱 실측). 디스패처 작업은 async 흐름 밖에서 고정한다. 그사이 언어가 또 바뀌었으면 건너뛴다.
    /// </summary>
    private void FixCulture(string code)
    {
        ApplyCulture(code);
        if (Avalonia.Threading.Dispatcher.UIThread is { } ui)
        {
            ui.Post(() =>
            {
                if (string.Equals(_language, code, StringComparison.OrdinalIgnoreCase)) { ApplyCulture(code); }
            });
        }
    }

    private static void ApplyCulture(string code)
    {
        try
        {
            var culture = CultureInfo.GetCultureInfo(Cultures.GetValueOrDefault(code, code));
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = culture;
        }
        catch (CultureNotFoundException)
        {
            Log($"문화권 {code} 를 쓸 수 없음(불변 세계화 모드 등) — 문자열만 바꾸고 날짜·숫자 형식은 유지");
        }
    }

    /// <summary>
    /// 사전 로딩(실패 안전): 어떤 예외도 밖으로 내보내지 않는다 — 경고만 남기고 빈 사전으로 계속 간다(앱 기동을 막지 않는다).
    /// ① 주 어셈블리의 ".Localization.&lt;영역&gt;.&lt;언어&gt;.json" 내장 리소스(정상 경로 — csproj EmbeddedResource WithCulture="false").
    /// ② ①에서 기본 언어를 못 찾으면 위성 어셈블리(ko/·en/… xkxk.frameworks.ui.shell.resources.dll)도 본다 — MSBuild 가 "*.ko.json" 을 문화권 리소스로
    ///    오인해 위성으로 빼낸 빌드(00:20 판본 실측) 대비. 위성 리소스의 언어는 위성 문화권으로 정한다. 브라우저 등 위성 로딩이 안 되는 곳은 건너뛴다.
    /// </summary>
    private void LoadEmbedded(Assembly asm)
    {
        try
        {
            LoadFrom(asm, null);
            if (!_dict.ContainsKey(DefaultLanguage))
            {
                foreach (var code in Cultures.Keys)
                {
                    try
                    {
                        LoadFrom(asm.GetSatelliteAssembly(CultureInfo.GetCultureInfo(code)), code);
                    }
                    catch (Exception) { /* 그 언어 위성 없음·위성 로딩 미지원(WASM 등) — 건너뜀 */ }
                }

                if (_dict.Count > 0) { Log($"사전을 위성 어셈블리에서 읽음({string.Join(",", _dict.Keys)}) — csproj EmbeddedResource 에 WithCulture=\"false\" 가 필요하다"); }
            }
        }
        catch (Exception ex)
        {
            Log("사전 로딩 실패(빈 사전으로 계속): " + ex.Message);
        }

        if (!_dict.ContainsKey(DefaultLanguage)) { Log("기본 언어(ko) 사전이 없음 — 모든 문자열이 빈 값이 된다(내장 리소스 설정 확인)"); }
    }

    /// <summary>어셈블리 1개의 사전 리소스를 읽는다. lang=null 이면 이름의 끝에서 두 번째 마디(<영역>.<언어>.json)를 언어로 쓴다.</summary>
    private void LoadFrom(Assembly asm, string? lang)
    {
        foreach (var name in asm.GetManifestResourceNames().Where(n => n.Contains(".Localization.", StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
        {
            var parts = name.Split('.');
            var code = lang ?? (parts.Length >= 3 ? parts[^2].ToLowerInvariant() : "");
            try
            {
                using var stream = asm.GetManifestResourceStream(name);
                if (stream is null || code.Length == 0) { continue; }
                using var doc = JsonDocument.Parse(stream, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                var target = _dict.TryGetValue(code, out var t) ? t : _dict[code] = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var p in doc.RootElement.EnumerateObject())
                {
                    if (p.Value.ValueKind != JsonValueKind.String) { continue; }
                    if (!target.TryAdd(p.Name, p.Value.GetString() ?? "")) { Log($"중복 키 lang={code} key={p.Name} ({name}) — 먼저 읽은 값 사용"); }
                }
            }
            catch (Exception ex)
            {
                Log($"사전 읽기 실패 {name}: {ex.Message}");
            }
        }
    }

    /// <summary>경고 출력(실패 안전) — Warn 이 비었거나 던져도 무시한다.</summary>
    private static void Log(string message)
    {
        try { Warn?.Invoke(message); }
        catch (Exception) { /* 경고 출력 실패는 무시 */ }
    }

    private sealed class KeyObservable(Loc owner, string key) : IObservable<string>
    {
        public IDisposable Subscribe(IObserver<string> observer)
        {
            void OnChanged() => observer.OnNext(owner.Get(key));
            observer.OnNext(owner.Get(key));
            owner.LanguageChanged += OnChanged;
            return new Unsubscriber(() => owner.LanguageChanged -= OnChanged);
        }
    }

    private sealed class Unsubscriber(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;

        public void Dispose()
        {
            _dispose?.Invoke();
            _dispose = null;
        }
    }
}
