// 문구 키 관찰 도우미 — Loc.Observe 가 돌려주는 관찰자(구독 즉시 현재 값, 언어 변경마다 새 값)와 구독 해제 객체(Loc.cs 300줄 분할)
using System;

namespace xkxk.frameworks.ui.shell.Localization;

/// <summary>키 1개를 관찰한다: 구독 즉시 현재 값, 이후 언어가 바뀔 때마다 새 값.</summary>
internal sealed class KeyObservable(Loc owner, string key) : IObservable<string>
{
    public IDisposable Subscribe(IObserver<string> observer)
    {
        void OnChanged() => observer.OnNext(owner.Get(key));
        observer.OnNext(owner.Get(key));
        owner.LanguageChanged += OnChanged;
        return new Unsubscriber(() => owner.LanguageChanged -= OnChanged);
    }
}

/// <summary>구독 해제(1회만 실행).</summary>
internal sealed class Unsubscriber(Action dispose) : IDisposable
{
    private Action? _dispose = dispose;

    public void Dispose()
    {
        _dispose?.Invoke();
        _dispose = null;
    }
}
