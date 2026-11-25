// 앱 모달 1차 관문(히트 테스트 차단) — 막힌 창(데스크톱)·메인 뷰(단일 뷰)의 히트 테스트를 꺼 포인터가 그 안 요소로 라우팅되지 않게 해 handledEventsToo 처리기(Dock 내부 포함)까지 막는다
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace xkxk.frameworks.ui.shell.Platform;

public static partial class ModalScope
{
    /// <summary>히트 테스트를 끈 요소 → (막은 범위 수, 원래 로컬 IsHitTestVisible — null 이면 로컬 값이 없었다). 마지막 범위가 풀 때 원래대로 되돌린다.</summary>
    private static readonly Dictionary<Control, (int Count, bool? Saved)> HitBlocks = new();

    /// <summary>
    /// 1차 관문 사용 여부(진단 modal.state rawGateInstalled — 이름은 호환 유지). 처음 설계한 원시 입력(IInputManager.PreProcess) 관문은
    /// 12.1.4 참조 어셈블리가 PreProcess·RawInputEventArgs 멤버를 공개하지 않아(빌드 CS0122·CS1061) 쓸 수 없고, 공개 API 인 IsHitTestVisible 로 대신한다.
    /// 터널 관문(Handled)은 handledEventsToo 처리기를 막지 못하지만, 히트 테스트에 걸리지 않는 요소에는 포인터 이벤트가 아예 가지 않는다.
    /// </summary>
    public static bool RawGateInstalled { get; private set; }

    private static void InstallRawGate() => RawGateInstalled = true;

    /// <summary>창의 히트 테스트 차단 대상 = 템플릿 루트(제목줄·내용 전부). 창 자신은 남겨 막힌 누름이 창에만 닿게 한다(터널 관문이 모달을 앞으로 가져온다).</summary>
    private static Control? HitRootOf(TopLevel top) => top.GetVisualChildren().OfType<Control>().FirstOrDefault();

    /// <summary>단일 뷰(웹·Android) 모달 내용이면 메인 뷰. 모달 Flyout 은 OverlayLayer 라 메인 뷰 밖이다 — 내용이 메인 뷰 안이면(호스트가 층을 안에 둔 경우) 막지 않는다.</summary>
    private static Control? SingleViewRootFor(Control modal)
    {
        if (modal is TopLevel || (Application.Current?.ApplicationLifetime as ISingleViewApplicationLifetime)?.MainView is not { } main)
        {
            return null;
        }

        return modal.GetVisualAncestors().Contains(main) ? null : main;
    }

    /// <summary>범위가 요소의 히트 테스트를 끈다(이미 껐으면 건너뜀). 범위가 끝나면 HitRelease 로 되돌린다.</summary>
    private static void HitBlock(Scope scope, Control? target)
    {
        if (target is null || scope.HitBlocked.Contains(target))
        {
            return;
        }

        scope.HitBlocked.Add(target);
        if (HitBlocks.TryGetValue(target, out var entry))
        {
            HitBlocks[target] = (entry.Count + 1, entry.Saved);
            return;
        }

        HitBlocks[target] = (1, target.IsSet(InputElement.IsHitTestVisibleProperty) ? (bool?)target.IsHitTestVisible : null);
        target.IsHitTestVisible = false;
    }

    private static void HitRelease(Control target)
    {
        if (!HitBlocks.TryGetValue(target, out var entry))
        {
            return;
        }

        if (entry.Count > 1)
        {
            HitBlocks[target] = (entry.Count - 1, entry.Saved);
            return;
        }

        HitBlocks.Remove(target);
        Restore(target, entry.Saved);
    }

    /// <summary>모든 범위가 끝났는데 남은 히트 차단을 되돌린다(해제 누락 안전망).</summary>
    private static void SweepHitBlocks()
    {
        if (HitBlocks.Count == 0)
        {
            return;
        }

        Log($"[경고] 히트 차단 해제 누락 {HitBlocks.Count}건 정리");
        foreach (var (target, entry) in HitBlocks.ToArray())
        {
            Restore(target, entry.Saved);
        }

        HitBlocks.Clear();
    }

    /// <summary>로컬 값이 없었으면 지워 스타일·기본값으로 돌아가게 하고, 있었으면 그 값을 다시 넣는다.</summary>
    private static void Restore(Control target, bool? saved)
    {
        if (saved is { } value)
        {
            target.IsHitTestVisible = value;
        }
        else
        {
            target.ClearValue(InputElement.IsHitTestVisibleProperty);
        }
    }
}
