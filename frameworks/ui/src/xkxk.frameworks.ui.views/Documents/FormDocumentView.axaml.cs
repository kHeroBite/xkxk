// 한글 입력 폼 문서 화면의 코드 비하인드(메모리 저장·입력 요약 표시·열기 시간 계측·언어 전환 시 상태 문구 갱신)
using xkxk.frameworks.ui.shell;
using System;
using System.Collections.Generic;
using Avalonia.Controls;
using xkxk.frameworks.ui.shell.Diagnostics;
using xkxk.frameworks.ui.shell.Localization;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.views.Documents;

public partial class FormDocumentView : UserControl
{
    // 마지막 상태 문구를 만드는 함수. 언어가 바뀌면 다시 불러 현재 언어로 그린다(값은 그대로, 문장만 번역).
    private Func<string>? _status;

    public FormDocumentView()
    {
        PerfProbe.Current.TrackOpen(this, "form");
        InitializeComponent();
        SaveButton.Click += (_, _) => Save();
        ClearButton.Click += (_, _) =>
        {
            foreach (var box in Fields)
            {
                box.Text = string.Empty;
            }

            ShowStatus(() => Loc.Tr("doc.form.cleared.status"));
        };

        AttachedToVisualTree += (_, _) => XkxkUi.Current.Localization.LanguageChanged += RefreshStatus;
        DetachedFromVisualTree += (_, _) => XkxkUi.Current.Localization.LanguageChanged -= RefreshStatus;
    }

    /// <summary>마지막 저장값(메모리만, 자동 시험이 읽을 수 있게 공개).</summary>
    public IReadOnlyDictionary<string, string> Saved { get; private set; } = new Dictionary<string, string>();

    private IEnumerable<TextBox> Fields => new[] { TitleBox, OwnerBox, ClientBox, PlaceBox, CategoryBox, BodyBox };

    /// <summary>자동 시험용: 이름(TitleBox 등)으로 필드 값을 넣는다. 없는 이름이면 false.</summary>
    public bool SetField(string name, string value)
    {
        foreach (var box in Fields)
        {
            if (string.Equals(box.Name, name, StringComparison.Ordinal))
            {
                box.Text = value;
                return true;
            }
        }

        return false;
    }

    private void Save()
    {
        var saved = new Dictionary<string, string>();
        foreach (var box in Fields)
        {
            saved[box.Name ?? ""] = box.Text ?? "";
        }

        Saved = saved;
        var (at, title, owner, length) = (DateTime.Now, TitleBox.Text ?? "", OwnerBox.Text ?? "", (BodyBox.Text ?? "").Length);
        ShowStatus(() => Loc.Tr("doc.form.saved.status", at, title, owner, length));
    }

    private void ShowStatus(Func<string> status)
    {
        _status = status;
        RefreshStatus();
    }

    private void RefreshStatus() => SavedText.Text = _status?.Invoke() ?? string.Empty;
}
