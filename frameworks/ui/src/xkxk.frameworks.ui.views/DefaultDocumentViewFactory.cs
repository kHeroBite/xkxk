// 기본 문서 뷰 생성기(문서 종류별 뷰) — App.axaml.cs 에서 분리한 책임 단위
using xkxk.frameworks.ui.views.Documents;
using xkxk.frameworks.ui.shell;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Templates;
using Avalonia.Markup.Xaml;
using Dock.Model.Core;
using xkxk.frameworks.ui.shell.Diagnostics;
using xkxk.frameworks.ui.shell.Docking;
using xkxk.frameworks.ui.shell.Theming;
using xkxk.frameworks.ui.shell.ViewModels;

namespace xkxk.frameworks.ui.views;

/// <summary>기본 문서 뷰 생성기 — 문서 3종(odev-3 소유 뷰)을 종류별로 생성.</summary>
public sealed class DefaultDocumentViewFactory : IDocumentViewFactory
{
    public Control Create(PocDocument document) => document.Kind switch
    {
        DocumentKind.Grid => GridViewPool.TryTake() ?? new GridDocumentView(), // 준비된 표 뷰가 있으면 그것을 쓴다(A16, 옵션 GridViewPool)
        DocumentKind.Gantt => new GanttDocumentView(),
        _ => new FormDocumentView(),
    };
}
