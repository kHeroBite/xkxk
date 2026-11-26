// 원격 명령(컨트롤 트리·클릭·값 입력·선택·뷰모델 명령·조건 대기) — 실입력 없이 Automation Peer·속성으로 조작
#if PD_DIAGNOSTICS // 진단 빌드 전용(PdDiagnostics=true)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using xkxk.frameworks.ui.shell.Docking;

namespace xkxk.frameworks.ui.shell.Diagnostics.Remote;

internal static class RemoteUiCommands
{
    // 공용 멤버 전달자(W2 F) — 본문은 옛 이름 그대로 쓴다
    private static IShellCommands Shell => RemoteShellAccess.Shell;

    private static PocDocument Doc(JsonObject a, string key = "doc") => RemoteShellAccess.Doc(a, key);

    private static Visual? Scope(JsonObject a) => RemoteUiTargets.Scope(a);

    private static Control Target(JsonObject a) => RemoteUiTargets.Target(a);

    // Scope·Target 은 RemoteUiTargets 로 옮겼다(W2 S 단계). 아래 본문은 이 파일 위의 private 전달자를 거쳐 부른다.

    /// <summary>
    /// 이름으로 Avalonia 속성 값 읽기(등록 속성 → 없으면 CLR 속성). 값은 ToString(브러시는 #AARRGGBB 등), 없으면 null,
    /// 값의 출처(Style·LocalValue 등) 판별은 하지 않는다. 쉼표로 여러 개.
    /// </summary>
    private static JsonObject PropsJson(Control c, string names)
    {
        var o = new JsonObject();
        foreach (var name in names.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            object? v;
            if (AvaloniaPropertyRegistry.Instance.FindRegistered(c, name) is { } p) { v = c.GetValue(p); }
            else if (c.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance) is { } pi) { v = pi.GetValue(c); }
            else { o[name] = "(속성 없음)"; continue; }
            // 문자열 목록(Classes 등)은 형식 이름이 아니라 내용을 쉼표로 잇는다(odev-1 03:1x — Classes.ToString 은 내용을 내지 않음)
            o[name] = v is System.Collections.Generic.IEnumerable<string> list ? string.Join(",", list) : v?.ToString();
        }

        return o;
    }

    internal static void Register(RemoteCommandRouter r)
    {
        r.Register("tree.get", "컨트롤 트리 [window=0|doc] [named=true(의미 있는 노드만)|false] [depth=60]", a =>
            VisualTreeInspector.Dump(Scope(a) ?? VisualTreeInspector.Main(), a.Bool("named", true), a.Int("depth") ?? 60));
        r.Register("ui.find", "컨트롤 1개 정보 id=AutomationId|Name(\"id#2\"=2번째) 또는 dock=<Dock Id>(바깥 패널 ToolDockControl) [doc|window] [props=Background,BorderBrush,…(Avalonia 속성 이름 — 값 문자열)]", a =>
        {
            var c = Target(a);
            var o = VisualTreeInspector.Describe(c);
            if (a.Str("props") is { Length: > 0 } names) { o["props"] = PropsJson(c, names); } // odev-4b 01:4x: MDI 바탕(PART_Root Background) 진단
            return o;
        });
        r.Register("ui.click", "클릭(Invoke/Toggle/Select 패턴 — 실입력 아님) id=", a => { var c = Target(a); Click(c); return VisualTreeInspector.Describe(c); });
        r.Register("ui.setText", "값 입력 id= text=", a =>
        {
            var c = Target(a);
            var text = a.Str("text") ?? "";
            switch (c)
            {
                case TextBox tb: tb.Text = text; break;
                case NumericUpDown nud: nud.Value = decimal.TryParse(text, out var dv) ? dv : throw new RemoteCommandException("숫자가 아닙니다: " + text); break;
                default:
                    var vp = Peer(c)?.GetProvider<IValueProvider>() ?? throw new RemoteCommandException($"{c.GetType().Name} 은 값 입력을 지원하지 않습니다.");
                    vp.SetValue(text);
                    break;
            }

            return VisualTreeInspector.Describe(c);
        });
        r.Register("ui.select", "항목 선택 id= index=n | text=항목 문자열(ComboBox·ListBox·TabControl 등)", a =>
        {
            var c = Target(a);
            if (c is not SelectingItemsControl s) { throw new RemoteCommandException($"{c.GetType().Name} 은 선택 컨트롤이 아닙니다."); }
            if (a.Int("index") is { } idx) { s.SelectedIndex = idx; }
            else
            {
                var text = a.ReqStr("text");
                var items = s.Items.Cast<object?>().ToList();
                var i = items.FindIndex(it => ItemText(it) == text);
                if (i < 0) { i = items.FindIndex(it => ItemText(it)?.Contains(text, StringComparison.OrdinalIgnoreCase) == true); }
                s.SelectedIndex = i >= 0 ? i : throw new RemoteCommandException($"항목 없음: {text} (항목: {string.Join(" | ", items.Select(ItemText))})");
            }

            return VisualTreeInspector.Describe(c);
        });
        r.Register("ui.toggle", "체크/토글 id= [on=true|false, 생략=반전]", a =>
        {
            var c = Target(a);
            if (c is not ToggleButton t) { throw new RemoteCommandException($"{c.GetType().Name} 은 토글 컨트롤이 아닙니다."); }
            t.IsChecked = a.ContainsKey("on") ? a.Bool("on") : t.IsChecked != true;
            return VisualTreeInspector.Describe(c);
        });
        r.Register("ui.pseudo", "의사 클래스 강제 id=|dock= [doc] cls=:pointerover|:pressed|:focus|:focus-visible|:checked|:selected(별칭 state=, ':' 생략 가능) on=true|false → 컨트롤 정보(classes·pseudo 포함) — 실커서 없이 hover·눌림 상태 캡처용(다음 실제 포인터 이벤트가 오면 원래대로 바뀔 수 있음)", a =>
        {
            var c = Target(a);
            // cls= 기본(lead 03:4x 사양), state= 는 옛 이름 별칭. 공개 API 근거: Avalonia 12.1.4 Avalonia.Base.xml — IPseudoClasses('Classes 에 의사 클래스를 설정하는 인터페이스')·
            // PseudoClassesExtensions.Set(IPseudoClasses, name, bool), Classes.Add 는 표준 클래스만(의사 클래스는 IPseudoClasses 경로)
            var state = (a.Str("cls") ?? a.Str("state") ?? "").TrimStart(':').ToLowerInvariant();
            if (state is not ("pointerover" or "pressed" or "focus" or "focus-visible" or "checked" or "selected"))
            {
                throw new RemoteCommandException("cls=:pointerover|:pressed|:focus|:focus-visible|:checked|:selected 가 필요합니다(별칭 state=).");
            }

            ((IPseudoClasses)c.Classes).Set(":" + state, a.Bool("on", true)); // Classes 는 IPseudoClasses 를 명시 구현 — ':' 이름은 이 경로로만 설정된다
            var d = VisualTreeInspector.Describe(c);
            d["pseudo"] = new JsonArray(c.Classes.Where(x => x.StartsWith(':')).Select(x => (JsonNode?)JsonValue.Create(x)).ToArray());
            return d;
        });
        r.Register("ui.tabClose", "제목 탭 닫기(×) 표시 조회·변경 [mode=Auto|Always|Hover|ActiveAndHover(즉시 저장 — 시험이 원복)] [focus=AutomationId(Tab 방식 포커스)] → mode·effective·touch·focused·stripClasses·tabs(id·selected·pointerover·kbfocus·closeOpacity·fade·titleX·closeX)", TabClose);
        r.Register("ui.invoke", "메인 뷰모델 명령 실행 command=ToggleTheme|ToggleThemeCommand… [param] [doc](문서 인자 명령) — command 생략 시 목록", Invoke);
        r.Register("wait.for", "조건 대기 timeout=ms(5000) + docCount=n | control=id | gone=id | id=+text=(일치)/contains= | active=문서제목", WaitForAsync);
    }

    /// <summary>
    /// 제목 탭 × 표시 상태(01 §2-1, AC-108~110). 값은 화면이 실제로 쓴 결과(Opacity·OpacityMask·제목 글자 x)라 스타일 판정을 그대로 확인한다.
    /// titleX 는 제목 TextBlock(shell.tabTitle.{Id})의 탭 줄 기준 x — × 표시 전후 같아야 글자 밀림이 없다.
    /// </summary>
    private static JsonNode? TabClose(JsonObject a)
    {
        var settings = xkxk.frameworks.ui.shell.Framework.XkxkUi.Current.Settings;
        if (a.Str("mode") is { } m)
        {
            if (char.IsDigit(m.Trim().FirstOrDefault()) || !Enum.TryParse<xkxk.frameworks.ui.shell.Theming.TitleTabCloseMode>(m, true, out var mode) || !Enum.IsDefined(mode))
            {
                throw new RemoteCommandException("mode 는 Auto|Always|Hover|ActiveAndHover 입니다: " + m);
            }

            settings.TitleTabCloseMode = mode;
        }

        var strip = VisualTreeInspector.Require("shell.tabStrip", null);
        // focus=<AutomationId>: 키보드(Tab) 방식으로 포커스를 준다(OS 입력 아님) — 탭 안 키보드 포커스 때 × 보임(.kbfocus)을 실입력 없이 재현
        if (a.Str("focus") is { Length: > 0 } fid)
        {
            VisualTreeInspector.Require(fid, null).Focus(Avalonia.Input.NavigationMethod.Tab);
        }

        var focused = TopLevel.GetTopLevel(strip)?.FocusManager?.GetFocusedElement() as Control;
        var tabs = new JsonArray();
        foreach (var item in Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(strip).OfType<ListBoxItem>())
        {
            if (item.DataContext is not PocDocument doc) { continue; }
            var buttons = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(item).OfType<Button>().ToList();
            var close = buttons.FirstOrDefault(b => b.Classes.Contains("tabClose"));
            var title = buttons.FirstOrDefault(b => b.Classes.Contains("tabTitle"));
            var text = title is null ? null : Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(title).OfType<TextBlock>().FirstOrDefault();
            tabs.Add((JsonNode)new JsonObject // 비제네릭 Add(JsonNode) — 제네릭 Add<T> 는 trim 경고(IL2026)
            {
                ["id"] = doc.Id,
                ["title"] = doc.Title,
                ["selected"] = item.IsSelected,
                ["pointerover"] = item.Classes.Contains(":pointerover"),
                ["kbfocus"] = item.Classes.Contains("kbfocus"),
                ["closeOpacity"] = close?.Opacity,
                ["fade"] = title?.OpacityMask is not null,
                ["titleX"] = text?.TranslatePoint(default, strip)?.X,
                ["titleWidth"] = text?.Bounds.Width,
                ["closeX"] = close?.TranslatePoint(default, strip)?.X,
                ["tabWidth"] = item.Bounds.Width,
            });
        }

        return new JsonObject
        {
            ["mode"] = settings.TitleTabCloseMode.ToString(),
            ["effective"] = settings.EffectiveTitleTabCloseMode.ToString(),
            ["touch"] = xkxk.frameworks.ui.shell.Platform.InputFormFactor.IsTouchDevice,
            ["focused"] = focused is null ? null : Avalonia.Automation.AutomationProperties.GetAutomationId(focused) ?? focused.GetType().Name,
            ["stripClasses"] = new JsonArray(strip.Classes.Where(c => !c.StartsWith(':')).Select(c => (JsonNode?)JsonValue.Create(c)).ToArray()),
            ["tabs"] = tabs,
        };
    }

    private static string? ItemText(object? item) => item switch
    {
        null => null,
        ContentControl { Content: { } c } => c.ToString(),
        HeaderedContentControl { Header: { } h } => h.ToString(),
        _ => item.ToString(),
    };

    private static AutomationPeer? Peer(Control c) => ControlAutomationPeer.CreatePeerForElement(c);

    /// <summary>클릭 = UIA 와 같은 패턴 호출(Button.OnClick → Command 실행 포함). 패턴이 없으면 Click 이벤트를 직접 발생.</summary>
    private static void Click(Control c)
    {
        if (!c.IsEffectivelyEnabled) { throw new RemoteCommandException("비활성 컨트롤입니다."); }
        var peer = Peer(c);
        if (peer?.GetProvider<IInvokeProvider>() is { } inv) { inv.Invoke(); return; }
        if (peer?.GetProvider<IToggleProvider>() is { } tog) { tog.Toggle(); return; }
        if (peer?.GetProvider<ISelectionItemProvider>() is { } sel) { sel.Select(); return; }
        if (peer?.GetProvider<IExpandCollapseProvider>() is { } ex)
        {
            if (ex.ExpandCollapseState == Avalonia.Automation.ExpandCollapseState.Expanded) { ex.Collapse(); } else { ex.Expand(); }
            return;
        }

        if (c is Button b)
        {
            if (b.Command is { } cmd && cmd.CanExecute(b.CommandParameter)) { cmd.Execute(b.CommandParameter); }
            b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            return;
        }

        throw new RemoteCommandException($"{c.GetType().Name} 은 클릭 패턴이 없습니다.");
    }

    /// <summary>
    /// MainViewModel 과 그 하위 뷰모델(MainViewModel 의 공개 속성 중 형식 이름이 ViewModel 로 끝나는 것 — Compact·Extras·SettingsPanel·SystemMenu·WindowMenu)의
    /// ICommand 속성 실행(W3 R1 — 1단계만 탐색). 이름이 겹치면 MainViewModel 것이 먼저다. RelayCommand&lt;PocDocument&gt; 는 doc 인자(생략 시 활성 문서)를 넘긴다.
    /// </summary>
    private static JsonNode? Invoke(JsonObject a)
    {
        var vm = xkxk.frameworks.ui.shell.Framework.XkxkUi.Current.MainViewModel ?? throw new RemoteNotReadyException("no-viewmodel", "뷰모델이 아직 없습니다.");
        var owners = new List<object> { vm };
        owners.AddRange(vm.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType.Name.EndsWith("ViewModel", StringComparison.Ordinal) && p.GetIndexParameters().Length == 0)
            .Select(p => p.GetValue(vm)).OfType<object>());
        var props = new List<(object Owner, PropertyInfo Prop)>();
        foreach (var owner in owners)
        {
            foreach (var p in owner.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (typeof(ICommand).IsAssignableFrom(p.PropertyType) && !props.Any(x => x.Prop.Name == p.Name)) { props.Add((owner, p)); }
            }
        }

        var name = a.Str("command");
        if (string.IsNullOrEmpty(name))
        {
            return new JsonArray(props.Select(x => (JsonNode?)JsonValue.Create(x.Prop.Name)).ToArray());
        }

        var (target, prop) = props.FirstOrDefault(x => x.Prop.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
                || x.Prop.Name.Equals(name + "Command", StringComparison.OrdinalIgnoreCase));
        if (prop is null) { throw new RemoteCommandException($"명령 없음: {name} (command 생략 시 목록)"); }
        var cmd = (ICommand)(prop.GetValue(target) ?? throw new RemoteCommandException(name + " 이 아직 초기화되지 않았습니다."));
        var wantsDoc = prop.PropertyType.IsGenericType && prop.PropertyType.GetGenericArguments()[0] == typeof(PocDocument);
        object? param = wantsDoc ? Doc(a) : a.Str("param");
        if (!cmd.CanExecute(param)) { throw new RemoteCommandException(prop.Name + " 실행 불가(CanExecute=false)."); }
        cmd.Execute(param);
        return JsonValue.Create(prop.Name);
    }

    /// <summary>조건을 100ms 간격으로 확인(UI 스레드를 막지 않는 await). 시간 초과는 오류.</summary>
    private static async Task<JsonNode?> WaitForAsync(JsonObject a)
    {
        var timeout = a.Int("timeout") ?? 5000;
        var sw = Stopwatch.StartNew();
        while (true)
        {
            if (Met(a, out var detail))
            {
                return new JsonObject { ["met"] = true, ["elapsedMs"] = sw.ElapsedMilliseconds, ["detail"] = detail };
            }

            if (sw.ElapsedMilliseconds >= timeout)
            {
                throw new RemoteCommandException($"wait.for 시간 초과 {timeout}ms — 마지막 상태: {detail}");
            }

            await Task.Delay(100);
        }
    }

    private static bool Met(JsonObject a, out string detail)
    {
        var shell = Shell;
        if (a.Int("docCount") is { } n) { detail = $"docCount={shell.Documents.Count}"; return shell.Documents.Count == n; }
        if (a.Str("active") is { } title) { detail = $"active={shell.ActiveDocument?.Title}"; return shell.ActiveDocument?.Title == title; }
        if (a.Str("gone") is { } gone)
        {
            var g = VisualTreeInspector.Find(gone, Scope(a));
            detail = g is null ? "없음" : "있음";
            return g is null || !g.IsEffectivelyVisible;
        }

        var id = a.Str("control") ?? a.Str("id") ?? throw new RemoteCommandException("조건 인자(docCount|control|gone|id+text|active)가 필요합니다.");
        var c = VisualTreeInspector.Find(id, Scope(a));
        var text = c is null ? null : VisualTreeInspector.TextOf(c);
        detail = c is null ? "컨트롤 없음" : $"text={text}";
        if (c is null || !c.IsEffectivelyVisible) { return false; }
        if (a.Str("text") is { } exact) { return text == exact; }
        if (a.Str("contains") is { } part) { return text?.Contains(part, StringComparison.Ordinal) == true; }
        return true;
    }
}
#endif
