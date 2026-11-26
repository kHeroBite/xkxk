// 컨텍스트 서비스 계약 시험 C28~C32 — XkxkUi.Current 의 Settings·Themes·Tour·Monitors 소유·옛 접근자 위임·UiHostOptions.MonitorProvider 연결을 본다(B05)
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using xkxk.frameworks.ui.shell;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.Framework;
using xkxk.frameworks.ui.shell.Theming;
using xkxk.frameworks.ui.shell.Tour;
using xkxk.frameworks.ui.shell.ViewModels;

namespace xkxk.frameworks.ui.test.framework.contract;

/// <summary>투어 카탈로그 소스 손 대역 — 원문을 읽지 못한 소스(ReadJson=null)를 흉내 낸다.</summary>
internal sealed class NullTourCatalogSource : ITourCatalogSource
{
    public int ReadCount { get; private set; }

    public string Name => "contract-null";

    public string? ReadJson()
    {
        ReadCount++;
        return null;
    }
}

/// <summary>모니터 공급자 손 대역 — 목록을 바꾸고 MonitorsChanged 를 직접 올릴 수 있다. 구독 수로 해제 여부를 본다.</summary>
internal sealed class FakeMonitorProvider : IMonitorProvider
{
    private Action? _changed;

    public FakeMonitorProvider(params MonitorInfo[] monitors)
    {
        Monitors = monitors;
    }

    public IReadOnlyList<MonitorInfo> Monitors { get; set; }

    public int SubscriberCount => _changed?.GetInvocationList().Length ?? 0;

    public event Action? MonitorsChanged
    {
        add => _changed += value;
        remove => _changed -= value;
    }

    public IReadOnlyList<MonitorInfo> GetMonitors() => Monitors;

    public void RaiseChanged() => _changed?.Invoke();

    /// <summary>시험용 모니터 2대(주 모니터 먼저).</summary>
    public static FakeMonitorProvider Two() => new(
        new MonitorInfo(@"\\.\CONTRACT1", "CONTRACT1", new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1040), true),
        new MonitorInfo(@"\\.\CONTRACT2", "CONTRACT2", new PixelRect(1920, 0, 2560, 1440), new PixelRect(1920, 0, 2560, 1400), false));
}

internal static class ContextServiceChecks
{
    public static IEnumerable<ContractCase> All()
    {
        yield return new ContractCase("C28", "XkxkUi.Current.Settings 비 null·같은 인스턴스·UiSettings.Current 와 같은 참조", C28);
        yield return new ContractCase("C29", "XkxkUi.Current.Themes 비 null·같은 인스턴스·ThemeCatalog.Current 와 같은 참조·내장 테마 1개 이상·공개 생성자 0", C29);
        yield return new ContractCase("C30", "XkxkUi.Current.Tour ≡ TourCatalog.Current·내장 단계 1개 이상, ReadJson=null 소스로 UseSource → 단계 0, UseSource(null) 원복 → 단계 1개 이상", C30);
        yield return new ContractCase("C31", "XkxkUi.Current.Monitors ≡ MonitorMenuViewModel.Instance·공개 생성자 0·기본 공급자 없음, Provider 대역 연결 → Items 2·MonitorsChanged 갱신, null 원복 → Items 0·구독 해제", C31);
        yield return new ContractCase("C32", "UiHostOptions.MonitorProvider 기본 null, UseXkxkUi 가 비 null 공급자를 Monitors 에 연결, null 옵션은 기존 공급자 유지(끝에 원복)", C32);
    }

    private static void C28()
    {
        var ctx = XkxkUi.Current;
        var settings = ctx.Settings;
        Check.True(settings is not null, "XkxkUi.Current.Settings 가 null");
        Check.True(ReferenceEquals(settings, ctx.Settings), "XkxkUi.Current.Settings 가 부를 때마다 다른 인스턴스");
        Check.True(ReferenceEquals(UiSettings.Current, settings), "UiSettings.Current 가 XkxkUi.Current.Settings 와 다른 인스턴스(위임 아님)");
    }

    private static void C29()
    {
        var ctx = XkxkUi.Current;
        var themes = ctx.Themes;
        Check.True(themes is not null, "XkxkUi.Current.Themes 가 null");
        Check.True(ReferenceEquals(themes, ctx.Themes), "XkxkUi.Current.Themes 가 부를 때마다 다른 인스턴스");
        Check.True(ReferenceEquals(ThemeCatalog.Current, themes), "ThemeCatalog.Current 가 XkxkUi.Current.Themes 와 다른 인스턴스(위임 아님)");
        Check.True(themes!.Themes.Count >= 1, $"내장 테마 수 {themes.Themes.Count}(1 이상 기대)");
        Check.True(typeof(ThemeCatalog).GetConstructors().Length == 0, "ThemeCatalog 에 공개 생성자가 있음(컨텍스트 소유 단일 인스턴스여야 함)");
    }

    private static void C30()
    {
        var ctx = XkxkUi.Current;
        var tour = ctx.Tour;
        Check.True(tour is not null, "XkxkUi.Current.Tour 가 null");
        Check.True(ReferenceEquals(TourCatalog.Current, tour), "TourCatalog.Current 가 XkxkUi.Current.Tour 와 다른 인스턴스(위임 아님)");
        Check.True(ReferenceEquals(ctx.Tour, tour), "소스를 바꾸지 않았는데 XkxkUi.Current.Tour 가 다시 읽힘");
        Check.True(tour!.Steps.Count >= 1, $"내장 투어 단계 수 {tour.Steps.Count}(1 이상 기대)");
        Check.True(TourCatalog.All.Count() == tour.Steps.Count, "TourCatalog.All 수가 XkxkUi.Current.Tour.Steps 수와 다름");

        var fake = new NullTourCatalogSource();
        try
        {
            TourCatalog.UseSource(fake);
            var empty = ctx.Tour;
            Check.True(fake.ReadCount >= 1, "UseSource 뒤 XkxkUi.Current.Tour 가 새 소스를 읽지 않음");
            Check.True(!ReferenceEquals(empty, tour), "UseSource 뒤에도 XkxkUi.Current.Tour 가 예전 카탈로그(캐시 무효화 안 됨)");
            Check.True(empty.Steps.Count == 0, $"ReadJson=null 소스인데 단계 수 {empty.Steps.Count}(0 기대)");
            Check.True(ReferenceEquals(TourCatalog.Current, empty), "UseSource 뒤 TourCatalog.Current 가 XkxkUi.Current.Tour 와 다름");
        }
        finally
        {
            TourCatalog.UseSource(null);
        }

        var restored = ctx.Tour;
        Check.True(restored.Steps.Count >= 1, $"UseSource(null) 원복 뒤 단계 수 {restored.Steps.Count}(1 이상 기대)");
        Check.True(restored.Steps.Count == tour.Steps.Count, $"원복 뒤 단계 수 {restored.Steps.Count} ≠ 처음 {tour.Steps.Count}");
    }

    private static void C31()
    {
        var ctx = XkxkUi.Current;
        var monitors = ctx.Monitors;
        Check.True(monitors is not null, "XkxkUi.Current.Monitors 가 null");
        Check.True(ReferenceEquals(monitors, ctx.Monitors), "XkxkUi.Current.Monitors 가 부를 때마다 다른 인스턴스");
        Check.True(ReferenceEquals(MonitorMenuViewModel.Instance, monitors), "MonitorMenuViewModel.Instance 가 XkxkUi.Current.Monitors 와 다른 인스턴스(위임 아님)");
        Check.True(typeof(MonitorMenuViewModel).GetConstructors().Length == 0, "MonitorMenuViewModel 에 공개 생성자가 있음");

        var original = monitors!.CurrentProvider;
        Check.True(original is null, "앱 없는 시험에서 기본 CurrentProvider 가 null 이 아님");
        Check.True(MonitorMenuViewModel.Provider is null, "기본 MonitorMenuViewModel.Provider 가 null 이 아님");
        Check.True(!monitors.HasMonitors, "공급자가 없는데 HasMonitors=true");

        var fake = FakeMonitorProvider.Two();
        try
        {
            MonitorMenuViewModel.Provider = fake;
            Check.True(ReferenceEquals(monitors.CurrentProvider, fake), "Provider 대입이 XkxkUi.Current.Monitors.CurrentProvider 에 반영되지 않음(위임 아님)");
            Check.True(ReferenceEquals(MonitorMenuViewModel.Provider, fake), "Provider 재조회가 넣은 대역이 아님");
            Check.True(monitors.Items.Count == 2 && monitors.HasMonitors, $"대역 모니터 2대 연결 뒤 Items {monitors.Items.Count}·HasMonitors {monitors.HasMonitors}");
            Check.True(fake.SubscriberCount == 1, $"MonitorsChanged 구독 수 {fake.SubscriberCount}(1 기대)");

            fake.Monitors = new[] { fake.Monitors[0] };
            fake.RaiseChanged();
            Check.True(monitors.Items.Count == 1, $"MonitorsChanged 뒤 Items {monitors.Items.Count}(1 기대)");
        }
        finally
        {
            MonitorMenuViewModel.Provider = original;
        }

        Check.True(monitors.CurrentProvider is null, "null 원복 뒤 CurrentProvider 가 남아 있음");
        Check.True(monitors.Items.Count == 0 && !monitors.HasMonitors, $"null 원복 뒤 Items {monitors.Items.Count}(0 기대)");
        Check.True(fake.SubscriberCount == 0, $"null 원복 뒤 대역 MonitorsChanged 구독이 남음({fake.SubscriberCount})");
    }

    private static void C32()
    {
        Check.True(new XkxkUiBuilderOptions().Host.MonitorProvider is null, "UiHostOptions.MonitorProvider 기본값이 null 이 아님");
        var prop = typeof(UiHostOptions).GetProperty(nameof(UiHostOptions.MonitorProvider));
        Check.True(prop is not null && prop.PropertyType == typeof(IMonitorProvider) && prop.SetMethod is { IsPublic: true },
            "UiHostOptions.MonitorProvider 공개 get·set(IMonitorProvider) 없음");

        var monitors = XkxkUi.Current.Monitors;
        var original = monitors.CurrentProvider;
        var snapshot = new AppStaticSnapshot();
        var fake = FakeMonitorProvider.Two();
        try
        {
            var b = XkxkUi.CreateBuilder();
            b.Options.Host.MonitorProvider = fake;
            var app = b.Build();
            Check.True(ReferenceEquals(app.Options.Host.MonitorProvider, fake), "Build 뒤 Host.MonitorProvider 참조가 바뀜");
            AppBuilder.Configure<Application>().UseXkxkUi(app); // Setup·Start 는 부르지 않는다(플랫폼 없음)
            Check.True(ReferenceEquals(XkxkUi.Current.Monitors.CurrentProvider, fake), "UseXkxkUi 뒤 XkxkUi.Current.Monitors.CurrentProvider 가 옵션 공급자가 아님");
            Check.True(ReferenceEquals(MonitorMenuViewModel.Provider, fake), "UseXkxkUi 뒤 MonitorMenuViewModel.Provider 가 옵션 공급자가 아님");
            Check.True(monitors.Items.Count == 2, $"UseXkxkUi 연결 뒤 Items {monitors.Items.Count}(2 기대)");

            AppBuilder.Configure<Application>().UseXkxkUi(XkxkUi.CreateBuilder().Build());
            Check.True(ReferenceEquals(XkxkUi.Current.Monitors.CurrentProvider, fake), "null MonitorProvider 옵션이 기존 공급자를 바꿈");
        }
        finally
        {
            MonitorMenuViewModel.Provider = original;
            snapshot.Restore();
        }

        Check.True(ReferenceEquals(monitors.CurrentProvider, original), "C32 끝 원복 실패 — CurrentProvider 가 처음 값이 아님");
        Check.True(fake.SubscriberCount == 0, $"원복 뒤 대역 MonitorsChanged 구독이 남음({fake.SubscriberCount})");
    }
}
