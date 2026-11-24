// 문의하기 화면 뷰모델(UI-17) — 분류·제목·내용 입력과 앱 정보 첨부 동의, 보내기는 서버 전까지 로컬 임시함에 저장
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pd.Poc.Shell.Localization;

namespace Pd.Poc.Shell.ViewModels.SystemMenu;

public sealed partial class InquiryViewModel : ObservableObject
{
    /// <summary>분류 선택지(앱 수명 동안 1개 — LocalizedOption 은 언어 구독을 해제하지 않는다).</summary>
    public static IReadOnlyList<LocalizedOption> CategoryOptions { get; } = LocalizedOption.List(
        "sysview.inquiry.cat.question", "sysview.inquiry.cat.bug", "sysview.inquiry.cat.idea", "sysview.inquiry.cat.other");

    private readonly InquiryStore _store;
    private LocalizedOption _category = CategoryOptions[0];
    private string _title = "";
    private string _body = "";
    private bool _attachAppInfo = true;
    private string _message = "";
    private bool _sending;

    public InquiryViewModel(InquiryStore? store = null)
    {
        _store = store ?? InquiryStore.Default;
        SendCommand = new AsyncRelayCommand(SendAsync, () => CanSend);
    }

    public IReadOnlyList<LocalizedOption> Categories => CategoryOptions;

    public LocalizedOption Category
    {
        get => _category;
        set => SetProperty(ref _category, value ?? CategoryOptions[0]);
    }

    public string Title
    {
        get => _title;
        set
        {
            if (SetProperty(ref _title, value ?? ""))
            {
                SendCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string Body
    {
        get => _body;
        set
        {
            if (SetProperty(ref _body, value ?? ""))
            {
                SendCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>앱 정보(버전·채널·OS 등) 첨부 동의. 기본 켬, 사용자가 끌 수 있다.</summary>
    public bool AttachAppInfo
    {
        get => _attachAppInfo;
        set => SetProperty(ref _attachAppInfo, value);
    }

    /// <summary>첨부될 앱 정보 미리보기(사용자가 무엇이 보내지는지 확인).</summary>
    public string AppInfoPreview => SystemInfo.AppInfoBlock();

    /// <summary>결과 안내(저장됨·실패). 빈 문자열이면 숨긴다.</summary>
    public string Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    public bool CanSend => !_sending && _title.Trim().Length > 0 && _body.Trim().Length > 0;

    public AsyncRelayCommand SendCommand { get; }

    /// <summary>방금 저장한 문의 Id(시험 판정용). 없으면 null.</summary>
    public string? LastSavedId { get; private set; }

    private async Task SendAsync()
    {
        _sending = true;
        SendCommand.NotifyCanExecuteChanged();
        try
        {
            var record = new InquiryRecord(
                Guid.NewGuid().ToString("N"),
                DateTimeOffset.Now,
                _category.Key,
                _title.Trim(),
                _body.Trim() + (_attachAppInfo ? "\n\n---\n" + SystemInfo.AppInfoBlock() : ""),
                _attachAppInfo,
                "local");
            if (await _store.AddAsync(record))
            {
                LastSavedId = record.Id;
                Message = Loc.Tr("sysview.inquiry.savedLocal");
                Title = "";
                Body = "";
            }
            else
            {
                Message = Loc.Tr("sysview.inquiry.saveFailed");
            }
        }
        finally
        {
            _sending = false;
            SendCommand.NotifyCanExecuteChanged();
        }
    }
}
