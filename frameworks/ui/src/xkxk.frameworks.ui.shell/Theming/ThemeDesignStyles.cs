// 요소 디자인 셀렉터 문자열 → Avalonia Selector 조립·Style 생성(REQ-101) — 공개 Selectors 확장 메서드(OfType·Is·Class·Name·Not·Child·Descendant·Template·Or)만 쓴다
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Styling;
using xkxk.frameworks.ui.Controls;
using MdiDocumentWindow = Dock.Avalonia.Controls.MdiDocumentWindow;
using ToolChromeControl = Dock.Avalonia.Controls.ToolChromeControl;

namespace xkxk.frameworks.ui.shell.Theming;

/// <summary>
/// ThemeElements 표의 셀렉터 문자열을 Selector 로 만든다. Avalonia 의 런타임 셀렉터 문자열 파서는 공개 API 문서에 없어 쓰지 않고,
/// 공개 Selectors 확장 메서드로 직접 조립한다. 지원 문법(표가 쓰는 만큼만):
/// 목록 "A, B" · 형식 "Button"·"ns|형식" · ":is(형식)" · ".클래스" · "#이름" · "#@부품"(TemplateParts 상수) · ":not(셀렉터)" ·
/// 결합 공백(자손)·" > "(자식)·" /template/ "(템플릿). 모르는 형식·문법이면 null(그 대상은 건너뛴다 — 화면 무변경).
/// </summary>
internal static class ThemeDesignStyles
{
    /// <summary>
    /// 셀렉터가 쓰는 형식 표. 트리밍(웹 TrimMode=full)에서도 남도록 이름 검색·반사 없이 typeof 로만 둔다. 화면 층(xkxk.frameworks.ui.views) 형식은 쓰지 않고
    /// 루트 클래스(pdTitleBar·pdSdiTitleBar·pdStatusBar·pdDocScope)로 고른다.
    /// </summary>
    private static readonly Dictionary<string, Type> KnownTypes = new(StringComparer.Ordinal)
    {
        ["UserControl"] = typeof(UserControl),
        ["TopLevel"] = typeof(TopLevel), ["TemplatedControl"] = typeof(TemplatedControl), ["Border"] = typeof(Border), ["ContentControl"] = typeof(ContentControl),
        ["Menu"] = typeof(Menu), ["MenuItem"] = typeof(MenuItem), ["TabItem"] = typeof(TabItem),
        ["ListBox"] = typeof(ListBox), ["ListBoxItem"] = typeof(ListBoxItem), ["ComboBoxItem"] = typeof(ComboBoxItem),
        ["Button"] = typeof(Button), ["TextBox"] = typeof(TextBox), ["ComboBox"] = typeof(ComboBox), ["NumericUpDown"] = typeof(NumericUpDown),
        ["DataGridColumnHeader"] = typeof(DataGridColumnHeader), ["DataGridCell"] = typeof(DataGridCell),
        ["controls|MdiDocumentWindow"] = typeof(MdiDocumentWindow), ["controls|ToolChromeControl"] = typeof(ToolChromeControl),
    };

    /// <summary>"#@이름" 이 가리키는 템플릿 부품(Shell 은 부품 이름을 TemplateParts 상수로만 쓴다).</summary>
    private static readonly Dictionary<string, string> Parts = new(StringComparer.Ordinal)
    {
        [nameof(TemplateParts.Header)] = TemplateParts.Header,
    };

    private static readonly Dictionary<string, Selector?> Cache = new(StringComparer.Ordinal);

    /// <summary>디자인 속성 → Avalonia 속성. 글자 속성은 상속 첨부 속성(TextElement)이라 패널·테두리 대상에도 걸린다.</summary>
    public static AvaloniaProperty? PropertyOf(string prop) => prop switch
    {
        "fontFamily" => TextElement.FontFamilyProperty,
        "fontSize" => TextElement.FontSizeProperty,
        "fontWeight" => TextElement.FontWeightProperty,
        "cornerRadius" => TemplatedControl.CornerRadiusProperty,
        "borderThickness" => TemplatedControl.BorderThicknessProperty,
        "padding" => TemplatedControl.PaddingProperty,
        _ => null,
    };

    /// <summary>
    /// (셀렉터, 속성, 값) 목록 → 전용 Styles 1개. 같은 셀렉터가 이어지면 Style 하나로 묶는다. 순서는 목록 순서 그대로.
    /// 리소스는 두지 않는다 — 리소스가 있는 Styles 를 넣고 빼면 트리 전체 ResourcesChanged 가 난다(적용기는 참조로 자리를 찾는다, ThemeDesignApplier.SlotIndex).
    /// </summary>
    public static Styles Build(IReadOnlyList<(string Selector, AvaloniaProperty Property, object Value)> rules)
    {
        var styles = new Styles();
        // 표식 Style(아무 컨트롤도 갖지 않는 클래스 + Setter 1개)을 늘 하나 둔다. 격리 진단판 실측(2026-11-16): Styles 를 빼기만 하면 문서 창 안
        // 컨트롤에는 빠진 값이 남고(전체 되돌리기 뒤 문서 버튼 테두리 잔존), Setter 가 있는 Style 이 들어올 때 문서 안까지 다시 평가돼 빠졌다
        // (Setter 없는 Style·빈 Styles 로는 재평가가 일어나지 않았다). 이 Style 은 맞는 대상이 없어 어떤 값도 바꾸지 않는다
        styles.Add(new Style
        {
            Selector = Selectors.Class(null, ThemeElements.RestyleSentinelClass),
            Setters = { new Setter(Control.TagProperty, ThemeElements.RestyleSentinelClass) },
        });
        Style? current = null;
        string? currentText = null;
        foreach (var (text, property, value) in rules)
        {
            if (!string.Equals(text, currentText, StringComparison.Ordinal))
            {
                currentText = text;
                current = Parse(text) is { } selector ? new Style { Selector = selector } : null;
                if (current is not null)
                {
                    styles.Add(current);
                }
            }

            current?.Setters.Add(new Setter(property, value));
        }

        return styles;
    }

    /// <summary>셀렉터 문자열 → Selector(캐시). 실패하면 null.</summary>
    public static Selector? Parse(string text)
    {
        if (Cache.TryGetValue(text, out var cached))
        {
            return cached;
        }

        Selector? result;
        try
        {
            var parser = new Parser(text);
            result = parser.List();
            if (!parser.AtEnd)
            {
                result = null;
            }
        }
        catch (FormatException)
        {
            result = null;
        }

        Cache[text] = result;
        return result;
    }

    private static Type Resolve(string ns, string name)
    {
        return KnownTypes.TryGetValue(ns.Length == 0 ? name : ns + "|" + name, out var t) ? t : throw new FormatException(name);
    }

    private sealed class Parser
    {
        private readonly string _s;
        private int _i;

        public Parser(string s) => _s = s;

        public bool AtEnd
        {
            get
            {
                SkipWs();
                return _i >= _s.Length;
            }
        }

        public Selector List()
        {
            var items = new List<Selector>();
            do
            {
                items.Add(Complex());
                SkipWs();
            }
            while (Eat(","));

            return items.Count == 1 ? items[0] : Selectors.Or(items);
        }

        private Selector Complex()
        {
            SkipWs();
            var sel = Compound(null);
            while (true)
            {
                var save = _i;
                var spaced = SkipWs();
                if (Eat(">"))
                {
                    SkipWs();
                    sel = Compound(sel.Child());
                }
                else if (Eat("/template/"))
                {
                    SkipWs();
                    sel = Compound(sel.Template());
                }
                else if (spaced && _i < _s.Length && _s[_i] is not (',' or ')'))
                {
                    sel = Compound(sel.Descendant());
                }
                else
                {
                    _i = save;
                    return sel;
                }
            }
        }

        private Selector Compound(Selector? previous)
        {
            var sel = previous;
            var any = false;
            if (_i < _s.Length && IsIdent(_s[_i]))
            {
                var (ns, name) = TypeName();
                sel = sel.OfType(Resolve(ns, name));
                any = true;
            }

            while (_i < _s.Length)
            {
                if (Eat(":is("))
                {
                    var (ns, name) = TypeName();
                    Expect(")");
                    sel = sel.Is(Resolve(ns, name));
                }
                else if (Eat(":not("))
                {
                    var argument = Complex();
                    SkipWs();
                    Expect(")");
                    sel = sel.Not(argument);
                }
                else if (Eat("."))
                {
                    sel = sel.Class(Ident());
                }
                else if (Eat("#"))
                {
                    sel = sel.Name(Eat("@") ? (Parts.TryGetValue(Ident(), out var part) ? part : throw new FormatException("part")) : Ident());
                }
                else
                {
                    break;
                }

                any = true;
            }

            return any && sel is not null ? sel : throw new FormatException(_s);
        }

        private (string Ns, string Name) TypeName()
        {
            var first = Ident();
            return Eat("|") ? (first, Ident()) : ("", first);
        }

        private string Ident()
        {
            var start = _i;
            while (_i < _s.Length && IsIdent(_s[_i]))
            {
                _i++;
            }

            return _i > start ? _s[start.._i] : throw new FormatException(_s);
        }

        private static bool IsIdent(char c) => char.IsLetterOrDigit(c) || c is '_' or '-';

        private bool SkipWs()
        {
            var start = _i;
            while (_i < _s.Length && char.IsWhiteSpace(_s[_i]))
            {
                _i++;
            }

            return _i > start;
        }

        private bool Eat(string token)
        {
            if (string.CompareOrdinal(_s, _i, token, 0, token.Length) != 0)
            {
                return false;
            }

            _i += token.Length;
            return true;
        }

        private void Expect(string token)
        {
            if (!Eat(token))
            {
                throw new FormatException(_s);
            }
        }
    }
}
