// 앱 사용자 데이터 폴더(LocalApplicationData/xkxk/frameworks/ui) 경로를 한 곳에서 정하는 공용 정보
using System;
using System.IO;

namespace xkxk.frameworks.ui.data;

/// <summary>저장소 폴더 경로와 같은 중첩 구조. 데스크톱 %LOCALAPPDATA%\xkxk\frameworks\ui, Android 앱 전용 files/xkxk/frameworks/ui.
/// 옛 Pd 앱 데이터 폴더는 읽지도 옮기지도 않는다(2026-11-25 사용자 결정).
/// 호스트는 <see cref="UseSegments"/> 로 하위 폴더 구성을 바꿀 수 있다(opt-in, UADR-0018 D4). 우선순위는
/// --data-dir(PD_DATA_DIR) 인자 &gt; UseSegments &gt; 기본값이며, 인자 처리는 이 형식을 읽는 쪽(RemoteInfoPath·호스트 인자)이 맡는다.</summary>
public static class AppDataFolder
{
    private static readonly string[] DefaultSegments = ["xkxk", "frameworks", "ui"];
    private static readonly object Gate = new();
    private static string[]? _segments;
    private static bool _rootRead;

    /// <summary>데이터 폴더 경로. 처음 읽은 뒤에는 <see cref="UseSegments"/> 를 부를 수 없다. 폴더를 만들지는 않는다.</summary>
    public static string Root
    {
        get
        {
            string[] segments;
            lock (Gate)
            {
                _rootRead = true;
                segments = _segments ?? DefaultSegments;
            }

            var parts = new string[segments.Length + 1];
            parts[0] = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            Array.Copy(segments, 0, parts, 1, segments.Length);
            return Path.Combine(parts);
        }
    }

    /// <summary>LocalApplicationData 아래 하위 폴더 구성을 지정한다(예 "xkxk","frameworks","ui","testbed").
    /// 첫 <see cref="Root"/> 읽기 전에 프로세스당 1회만 부른다. 부르지 않으면 기본 xkxk/frameworks/ui 그대로다.</summary>
    /// <exception cref="ArgumentNullException">segments 가 null.</exception>
    /// <exception cref="ArgumentException">빈 배열, 또는 빈 값·경로 구분자·"."·".."·루트 경로가 든 항목.</exception>
    /// <exception cref="InvalidOperationException">Root 를 이미 읽었거나 이미 지정했다.</exception>
    public static void UseSegments(params string[] segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        if (segments.Length == 0)
        {
            throw new ArgumentException("하위 폴더를 1개 이상 지정해야 한다.", nameof(segments));
        }

        foreach (var s in segments)
        {
            if (string.IsNullOrWhiteSpace(s) || s is "." or ".." || Path.IsPathRooted(s)
                || s.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, '/', '\\']) >= 0
                || s.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                throw new ArgumentException($"하위 폴더 이름으로 쓸 수 없는 값이다: '{s}'", nameof(segments));
            }
        }

        lock (Gate)
        {
            if (_rootRead)
            {
                throw new InvalidOperationException("AppDataFolder.Root 를 이미 읽은 뒤라 UseSegments 를 부를 수 없다. 호스트 진입 맨 앞에서 부른다.");
            }

            if (_segments is not null)
            {
                throw new InvalidOperationException("AppDataFolder.UseSegments 는 프로세스당 1회만 부를 수 있다.");
            }

            _segments = (string[])segments.Clone();
        }
    }
}
