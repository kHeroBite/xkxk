// 레이아웃 관리 패널의 기본 템플릿 고르기·적용 하위 뷰모델 — LayoutManagerViewModel 에서 분리
using xkxk.frameworks.ui.data.Layout;
using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.Localization;

namespace xkxk.frameworks.ui.shell.ViewModels;

/// <summary>템플릿 목록·선택·적용. 결과 문구는 관리 패널의 Message 로 보낸다(showMessage).</summary>
public sealed class LayoutTemplatePickerViewModel : ObservableObject
{
    private readonly Func<ILayoutLibrary?> _library;
    private readonly Action<string> _showMessage;
    private LayoutTemplate? _selectedTemplate;

    public LayoutTemplatePickerViewModel(Func<ILayoutLibrary?> library, Action<string> showMessage)
    {
        _library = library;
        _showMessage = showMessage;
        ApplyTemplateCommand = new RelayCommand(ApplyTemplate);
    }

    public IReadOnlyList<LayoutTemplate> Templates => _library()?.Templates ?? Array.Empty<LayoutTemplate>();

    public LayoutTemplate? SelectedTemplate { get => _selectedTemplate; set => SetProperty(ref _selectedTemplate, value); }

    public RelayCommand ApplyTemplateCommand { get; }

    /// <summary>언어 변경·목록 새로 고침 뒤 템플릿 이름을 다시 읽게 알린다(Q17).</summary>
    public void Refresh() => OnPropertyChanged(nameof(Templates));

    private void ApplyTemplate()
    {
        if (_library() is not { } lib || _selectedTemplate is null) { return; }
        _showMessage(lib.ApplyTemplate(_selectedTemplate.Id)
            ? Loc.Tr("layout.msg.templateApplied", _selectedTemplate.Name)
            : Loc.Tr("layout.msg.templateMissing"));
    }
}
