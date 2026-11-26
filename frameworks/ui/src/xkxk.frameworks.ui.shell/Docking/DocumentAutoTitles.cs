// 자동 문서 제목(Q17, 옛 WindowCommands.Titles) — 종류 이름+번호로 만든 제목은 언어가 바뀌면 다시 계산하고, 사용자가 바꾼 제목은 그대로 둔다
using System.Runtime.CompilerServices;
using xkxk.frameworks.ui.shell.Localization;
using xkxk.frameworks.ui.shell.Framework;

namespace xkxk.frameworks.ui.shell.Docking;

/// <summary>
/// 자동 제목 표시(static 표 s_autoTitle — K4, 옛 WindowCommands static 그대로)와 언어 변경 구독(인스턴스, 파사드 생성자에서 1회 Install)을 맡는다.
/// </summary>
[WindowCommandsPart]
internal sealed class DocumentAutoTitles
{
    // 자동 제목 문서 표시(문서 객체가 사라지면 함께 사라진다)
    private static readonly ConditionalWeakTable<PocDocument, object> s_autoTitle = new();
    private static readonly object Marker = new();
    private readonly WindowSession _s;

    internal DocumentAutoTitles(WindowSession s) => _s = s;

    /// <summary>현재 언어의 자동 제목("그리드 3" / "Grid 3").</summary>
    public static string AutoTitle(DocumentKind kind, int seq) => $"{kind.DisplayName()} {seq}";

    /// <summary>제목이 종류 이름+번호로 자동 생성된 것인가(사용자가 이름을 바꾸면 false).</summary>
    public static bool IsAutoTitle(PocDocument document) => s_autoTitle.TryGetValue(document, out _);

    internal static void SetAutoTitle(PocDocument document, bool auto)
    {
        s_autoTitle.Remove(document);
        if (auto)
        {
            s_autoTitle.Add(document, Marker);
        }
    }

    /// <summary>
    /// 옛 저장본(자동 여부 없음)용 추정: 제목이 현재 언어의 "종류 이름 번호" 꼴이면 자동 제목으로 본다.
    /// 다른 언어로 저장된 옛 자동 제목은 판별하지 못해 사용자 제목으로 남는다(사전에 언어별 조회 API 가 없음 — 새 저장본은 AutoTitle 필드로 정확히 판별).
    /// </summary>
    private static bool LooksAutoTitle(DocumentKind kind, int seq, string title) => title == AutoTitle(kind, seq);

    /// <summary>복원한 문서의 자동 제목 여부 적용. 자동이면 현재 언어로 제목을 다시 만든다.</summary>
    internal static void RestoreAutoTitle(PocDocument doc, DocumentSnapshot item)
    {
        var auto = item.AutoTitle ?? LooksAutoTitle(item.Kind, item.Seq, item.Title);
        SetAutoTitle(doc, auto);
        if (auto)
        {
            doc.Title = AutoTitle(doc.Kind, doc.Seq);
        }
    }

    /// <summary>언어가 바뀌면 자동 제목 문서의 제목을 새 언어로 다시 만든다(파사드 생성자에서 1회 구독).</summary>
    internal void InstallAutoTitles()
    {
        XkxkUi.Current.Localization.LanguageChanged += () =>
        {
            foreach (var doc in _s.Owner.OpenDocuments)
            {
                if (IsAutoTitle(doc))
                {
                    doc.Title = AutoTitle(doc.Kind, doc.Seq);
                }
            }
        };
    }
}
