// 기본 문서 뷰 생성기(문서 종류별 뷰) — App.axaml.cs 에서 분리한 책임 단위
using Pd.Ui.Views.Documents;
using Pd.Poc.Shell;
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
using Pd.Poc.Shell.Diagnostics;
using Pd.Poc.Shell.Docking;
using Pd.Poc.Shell.Theming;
using Pd.Poc.Shell.ViewModels;

namespace Pd.Ui.Views;

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
