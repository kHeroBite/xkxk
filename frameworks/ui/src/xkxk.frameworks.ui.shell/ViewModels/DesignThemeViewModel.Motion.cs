// 디자인 테마 탭 배경 행(21 §6.2) — 사용자 배경 이미지 가져오기·맞춤·불투명도·어둡게·지우기와 낮춤 알림 표시. 창 열기·닫기·전환 효과는 효과 행(Effects)으로 고른다
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using xkxk.frameworks.ui.data.Logging;
using xkxk.frameworks.ui.data.Theming;
using xkxk.frameworks.ui.shell.Localization;
using xkxk.frameworks.ui.shell.Theming;

namespace xkxk.frameworks.ui.shell.ViewModels;

public sealed partial class DesignThemeViewModel
{
    private static readonly string[] BackgroundProps =
        { nameof(IsImageDecor), nameof(HasBackgroundImage), nameof(BackgroundOpacity), nameof(BackgroundDim), nameof(BackgroundStatus), nameof(HasBackgroundStatus) };

    private DesignChoice? _selectedFit;
    private RelayCommand? _clearBackgroundCommand;
    private string _backgroundStatus = "";

    /// <summary>맞춤 선택 항목(fill·fit·stretch·tile·center). 문구 키 theme.style.bgFit.&lt;값&gt;.</summary>
    public IReadOnlyList<DesignChoice> BackgroundFits { get; } =
        Enum.GetValues<BackgroundImageFit>().Select(f => new DesignChoice(UiKeyNames.Of(f), "theme.style.bgFit." + UiKeyNames.Of(f))).ToList();

    /// <summary>배경 장식이 image 인가(배경 행의 이미지 조작을 보인다).</summary>
    public bool IsImageDecor => DesignThemeResolver.Effective("decor") == "image";
    public bool HasBackgroundImage => _settings.BackgroundImageFile is not null;
    public double MaxBackgroundDim => UiSettings.MaxBackgroundImageDim;

    /// <summary>파일 고르기 창 확장자 필터(ui.background.image.formats → "*.png" 꼴).</summary>
    public IReadOnlyList<string> BackgroundPatterns => _settings.BackgroundImageLimits.Formats.Select(f => "*." + f).ToList();

    public double BackgroundOpacity { get => _settings.BackgroundImageOpacity; set => _settings.BackgroundImageOpacity = value; }
    public double BackgroundDim { get => _settings.BackgroundImageDim; set => _settings.BackgroundImageDim = value; }

    /// <summary>가져오기 결과·낮춤 사유 문구(빈 문자열 = 없음).</summary>
    public string BackgroundStatus { get => _backgroundStatus; private set => SetProperty(ref _backgroundStatus, value); }
    public bool HasBackgroundStatus => BackgroundStatus.Length > 0;

    public DesignChoice? SelectedBackgroundFit
    {
        get => _selectedFit;
        set
        {
            if (SetProperty(ref _selectedFit, value) && !_loading && value is not null)
            {
                _settings.BackgroundImageFit = UiKeyNames.Parse(value.Value, BackgroundImageFit.Fill);
            }
        }
    }

    /// <summary>이미지 선택만 지운다(장식 값은 그대로 — image 면 none 처럼 보인다).</summary>
    public RelayCommand ClearBackgroundCommand => _clearBackgroundCommand ??= new RelayCommand(() =>
    {
        _settings.BackgroundImageFile = null;
        BackgroundStatus = "";
    });

    /// <summary>
    /// 고른 파일을 검사해 해시 이름으로 저장하고 장식을 image 로 둔다. 거부·할당량 초과면 저장값(옛 이미지)을 그대로 두고 사유를 보인다.
    /// 파일 고르기 창은 뷰(DesignThemePanel)가 띄우고 이름·바이트만 넘긴다(레이어 분리).
    /// </summary>
    public async Task<bool> ImportBackgroundAsync(string sourceName, byte[] data)
    {
        if (IsLocked)
        {
            return false;
        }

        if (DesignEffectsApplier.BlobStore is not { } blobs)
        {
            BackgroundStatus = Loc.Tr("background.fail.store");
            return false;
        }

        try
        {
            var result = await new BackgroundImageStore(blobs, _settings.BackgroundImageLimits).ImportAsync(sourceName, data, _settings.BackgroundImageFile);
            if (!result.Ok)
            {
                var rejected = "reject_" + result.Reason.ToString().ToLowerInvariant();
                BackgroundStatus = Loc.Tr("background.fail." + rejected); // 동적 키는 점으로 끝나는 접두로 쓴다(check_i18n ③)
                return false;
            }

            _settings.BackgroundImageFile = result.FileName;
            if (DesignThemeResolver.Stored("decor") != "image")
            {
                _settings.SetEffectOverride("decor", DesignThemeResolver.PresetValue("decor") == "image" ? null : "image");
            }

            BackgroundStatus = Loc.Tr("theme.style.bg.imported", result.Width, result.Height);
            Reload();
            return true;
        }
        catch (Exception ex)
        {
            var quota = ex.Message.Contains("QuotaExceeded", StringComparison.Ordinal);
            PdLog.Write(PdLogLevel.Warning, "theme", $"배경 이미지 가져오기 실패 {ex.GetType().Name}: {ex.Message}"); // i18n-ok 진단 로그
            BackgroundStatus = Loc.Tr(quota ? "background.fail.quota" : "background.fail.write");
            return false;
        }
    }

    /// <summary>생성자에서 부른다 — 배경 설정 변경·낮춤 알림을 받는다.</summary>
    private void InitExtras()
    {
        _settings.PropertyChanged += OnSettingsChanged;
        DesignEffectsApplier.BackgroundFallback += OnBackgroundFallback;
    }

    private void DisposeExtras()
    {
        _settings.PropertyChanged -= OnSettingsChanged;
        DesignEffectsApplier.BackgroundFallback -= OnBackgroundFallback;
    }

    /// <summary>Reload 안(_loading 중)에서 부른다.</summary>
    private void ReloadExtras()
    {
        ReloadLevels();
        foreach (var f in BackgroundFits)
        {
            f.RefreshText();
        }

        SelectedBackgroundFit = BackgroundFits.FirstOrDefault(f => f.Value == UiKeyNames.Of(_settings.BackgroundImageFit));
        RaiseBackground();
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is { } name && name.StartsWith("BackgroundImage", StringComparison.Ordinal))
        {
            RaiseBackground();
        }
    }

    private void OnBackgroundFallback(string reasonKey) => BackgroundStatus = Loc.Tr(reasonKey);

    private void RaiseBackground()
    {
        foreach (var p in BackgroundProps)
        {
            OnPropertyChanged(p);
        }
    }
}
