// 데모 창 3종(xkxk.demo.grid·gantt·form)을 시험대 모듈 주체로 같은 ID 에 등록한다(B10, a10 §3)
using System;
using xkxk.frameworks.ui.data.Navigation;
using xkxk.frameworks.ui.shell.Framework;
using xkxk.frameworks.ui.testbed.Documents;
using xkxk.frameworks.ui.views;

namespace xkxk.frameworks.ui.testbed;

/// <summary>
/// 데모 창 3종을 등록한다. 셸이 B09 까지 쓰던 ID·제목 키·설명 키·아이콘을 그대로 쓰고, 내용은 <see cref="UiWindowOptions.CreateContent"/> 로 만든다.
/// </summary>
/// <remarks>
/// <para>옛 이름(Grid·Gantt·Form)·숫자 별칭은 넣지 않는다. 레지스트리 해석의 마지막 단계가 늘 이 ID 로 풀어 준다(a10 §3).</para>
/// <para>세 창 모두 중복 허용 false·배치 기본이다. 프레임워크 공개 형식만 쓴다.</para>
/// </remarks>
public static class TestbedWindows
{
    /// <summary>그리드 데모 창 ID 다.</summary>
    public const string Grid = "xkxk.demo.grid";

    /// <summary>간트 데모 창 ID 다.</summary>
    public const string Gantt = "xkxk.demo.gantt";

    /// <summary>입력폼 데모 창 ID 다. 벤치 문서도 이 창에 제목 접두를 붙여 연다.</summary>
    public const string Form = "xkxk.demo.form";

    /// <summary>3종을 그리드 → 간트 → 입력폼 순서로 등록한다.</summary>
    /// <param name="context">모듈 구성 문맥. null 이면 <see cref="ArgumentNullException"/>.</param>
    public static void Register(IUiModuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var r = context.Windows;
        Add(r, Grid, "shell.kind.grid", MenuIconCatalog.GridIconKey, "chrome.help.grid",
            _ => new GridDocumentView(), UiWindowPrewarm.KeepReady);
        Add(r, Gantt, "shell.kind.gantt", MenuIconCatalog.GanttIconKey, "chrome.help.gantt",
            _ => new GanttDocumentView(), UiWindowPrewarm.Warm);
        Add(r, Form, "shell.kind.form", MenuIconCatalog.FormIconKey, null, CreateForm, UiWindowPrewarm.Warm);
    }

    private static void Add(IUiWindowRegistry r, string id, string titleKey, string icon, string? descriptionKey,
        Func<UiWindowContentContext, object?> createContent, UiWindowPrewarm prewarm) =>
        r.Add(id, o =>
        {
            o.TitleKey = titleKey;
            o.Icon = icon;
            o.DescriptionKey = descriptionKey;
            o.AllowDuplicate = false;
            o.CreateContent = createContent;
            o.Prewarm = prewarm;
        });

    /// <summary>입력폼 창 내용. 벤치 빌드에서 제목이 벤치 접두로 시작하면 벤치 화면을 만든다.</summary>
    private static object? CreateForm(UiWindowContentContext c)
    {
#if PD_DIAGNOSTICS && PD_BENCH
        if (TestbedBench.CreateView(c.Title) is { } bench)
        {
            return bench;
        }
#endif
        _ = c;
        return new FormDocumentView();
    }
}
