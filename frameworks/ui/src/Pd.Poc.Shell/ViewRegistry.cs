// 화면 층 등록 지점 — 등록 전 사용은 조용히 무동작하지 않고 예외로 드러낸다
using System;

namespace Pd.Poc.Shell;

/// <summary>기동 때 화면 층이 넣는 <see cref="IViewRegistry"/> 보관소.</summary>
public static class ViewRegistry
{
    private static IViewRegistry? s_current;

    public static IViewRegistry Current
    {
        get => s_current ?? throw new InvalidOperationException("IViewRegistry 가 등록되지 않았다. Application 생성자에서 등록해야 한다."); // i18n-ok 개발자 오류
        set => s_current = value;
    }
}
