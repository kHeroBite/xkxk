// MDI 탭 합류 끌기의 합류 아이콘 근접 켬(옵션 A, MdiMoveLight.TabJoinArmNear) — 포인터가 아이콘 자리 반경 안인 창만 아이콘을 켜고 벗어나면 끈다
using System;
using System.Collections.Generic;
using Avalonia;
using Dock.Avalonia.Controls;
using Pd.Ui.Controls;

namespace Pd.Poc.Shell.Docking;

/// <summary>
/// 옵션 A(정본 01a 끌기 가벼운 크롬, 기본 끔). 켬이면 끌기 시작 때 다른 창 전부에 .pdTabJoinArmed 를 걸지 않고,
/// 포인터가 그 창 합치기 아이콘 사각형을 MdiMoveLight.TabJoinArmRadius(DIP)만큼 키운 영역 안에 들어올 때만 걸고 벗어나면 뗀다.
/// 표시 클래스만 바뀐다 — 표적 목록·가림 판정·머무름(TabJoinDwellGate)·놓기 판정·합류는 끔과 같은 경로다.
/// 영역은 끌기 시작 때 1회 만든 표적 목록(s_targets)에서 계산한다(끌기 중 트리 훑기 없음).
/// </summary>
public static partial class MdiTabJoinDrag
{
    private static readonly List<(MdiDocumentWindow Window, Rect Zone, string Key, int Z)> s_nearZones = new();
    private static readonly HashSet<MdiDocumentWindow> s_nearArmed = new();
    private static bool s_nearMode;

    /// <summary>끌기 시작 1회. 켬이면 근접 영역을 만들고 true(호출부는 전체 armed 를 건너뛴다). 끔이면 false — 호출부가 기존 전체 armed 경로를 그대로 탄다.</summary>
    private static bool BeginNearArm(MdiDocumentWindow source)
    {
        s_nearMode = MdiMoveLight.TabJoinArmNear;
        if (!s_nearMode)
        {
#if PD_DIAGNOSTICS
            MdiTabJoinDiag.NoteStart(false, Math.Max(0, s_windows.Count - 1));
#endif
            return false;
        }

        var radius = Math.Max(0, MdiMoveLight.TabJoinArmRadius);
        foreach (var t in s_targets)
        {
            if (t.Kind == TabJoinKind.Icon && s_windows.TryGetValue(t.WindowKey, out var w) && !ReferenceEquals(w.Window, source))
            {
                s_nearZones.Add((w.Window, t.Rect.Inflate(radius), t.WindowKey, w.Z));
            }
        }

#if PD_DIAGNOSTICS
        MdiTabJoinDiag.NoteStart(true, 0);
#endif
        return true;
    }

    /// <summary>
    /// 포인터 이동마다(켬일 때만 호출): 영역 안으로 들어온 창은 아이콘을 켜고, 벗어난 창은 끈다. 바뀐 창만 클래스를 건드린다.
    /// 가림 판정은 PickVisible 과 같은 규칙(끄는 창 제외, Z 가 더 높은 다른 창이 그 점을 덮으면 가려짐)이며 영역 안일 때만 돈다(영역 밖 이동은 비용 그대로).
    /// </summary>
    private static void UpdateNearArm(Point point)
    {
        foreach (var (w, zone, key, z) in s_nearZones)
        {
            var inside = zone.Contains(point) && !IsCovered(point, key, z);
            if (inside == s_nearArmed.Contains(w)) { continue; }
            if (inside)
            {
                s_nearArmed.Add(w);
                w.Classes.Add(ArmedClass);
            }
            else
            {
                s_nearArmed.Remove(w);
                w.Classes.Remove(ArmedClass);
            }

#if PD_DIAGNOSTICS
            MdiTabJoinDiag.NoteNear(inside, s_nearArmed.Count);
#endif
        }
    }

    /// <summary>point 를 Z 가 z 보다 높은 다른 창(끄는 창·자기 자신 제외)이 덮고 있는가 — PickVisible 의 가림 조건과 같다(LINQ 할당 없이).</summary>
    private static bool IsCovered(Point point, string key, int z)
    {
        var sourceKey = s_source?.Id;
        foreach (var kv in s_windows)
        {
            if (kv.Key != sourceKey && kv.Key != key && kv.Value.Z > z && kv.Value.Rect.Contains(point)) { return true; }
        }

        return false;
    }

    /// <summary>놓기·취소·새 누름 때(켬 모드로 시작한 끌기만): 켜져 있던 창만 아이콘을 끄고 근접 상태를 비운다.</summary>
    private static void ResetNear()
    {
        foreach (var w in s_nearArmed)
        {
            w.Classes.Remove(ArmedClass);
        }

        s_nearArmed.Clear();
        s_nearZones.Clear();
        s_nearMode = false;
    }
}
