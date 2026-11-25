// 앱 사용자 데이터 폴더(LocalApplicationData/xkxk/frameworks/ui) 경로를 한 곳에서 정하는 공용 정보
using System;
using System.IO;

namespace xkxk.frameworks.ui.data;

/// <summary>저장소 폴더 경로와 같은 중첩 구조. 데스크톱 %LOCALAPPDATA%\xkxk\frameworks\ui, Android 앱 전용 files/xkxk/frameworks/ui.
/// 옛 Pd 앱 데이터 폴더는 읽지도 옮기지도 않는다(2026-11-25 사용자 결정).</summary>
public static class AppDataFolder
{
    public static string Root => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "xkxk", "frameworks", "ui");
}
