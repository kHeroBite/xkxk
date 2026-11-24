// 원격 명령 background.selftest 본문 — 배경 이미지 저장소의 거부(크기·형식·매직 바이트·가로세로)·해시 이름 규칙·교체 삭제를 메모리 저장소로 판정한다. 진단 빌드 전용
#if PD_DIAGNOSTICS // 진단 빌드 전용(PdDiagnostics=true)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using xkxk.frameworks.ui.data.Theming;

namespace xkxk.frameworks.ui.shell.Diagnostics.Remote;

/// <summary>별도 시험 프로젝트가 없어 앱 안에서 돈다(RemoteBusySelfTest 와 같은 방식). 파일·화면을 쓰지 않는다.</summary>
internal static class RemoteBackgroundSelfTest
{
    /// <summary>원격 명령 등록 — RemoteBusyCommands.Register 가 부른다.</summary>
    internal static void Register(RemoteCommandRouter r)
    {
        r.Register("background.selftest", "배경 이미지 저장소 시험(거부 4종·해시 이름 정규식·같은 이미지 같은 이름·교체 시 옛 파일 삭제·잘못된 이름 읽기 거부) → cases[{name,pass,detail}]·pass",
            async _ => await RunAsync().ConfigureAwait(true));
        RemoteBackgroundCommands.Register(r); // 배경 장식·이미지 조작·상태(AC-014)
    }

    internal static async Task<JsonObject> RunAsync()
    {
        var cases = new JsonArray();
        var limits = new BackgroundImageLimits(4096, 16384, BackgroundImageLimits.DefaultFormats);
        void Case(string name, bool pass, string detail) => cases.Add(new JsonObject { ["name"] = name, ["pass"] = pass, ["detail"] = detail });

        string Check(string file, byte[] data, BackgroundRejectReason want)
        {
            var r = BackgroundImageStore.Validate(file, data, limits);
            return r.Reason == want ? string.Empty : $"got {r.Reason} want {want}";
        }

        void Reject(string name, string file, byte[] data, BackgroundRejectReason want)
        {
            var failure = Check(file, data, want);
            Case(name, failure.Length == 0, failure.Length == 0 ? want.ToString() : failure);
        }

        Reject("reject_too_large", "a.png", Pad(Png(100, 100), 5000), BackgroundRejectReason.TooLarge);
        Reject("reject_format", "a.gif", Png(100, 100), BackgroundRejectReason.Format);
        Reject("reject_magic", "a.png", Jpeg(100, 100), BackgroundRejectReason.Magic);
        Reject("reject_magic_garbage", "a.bmp", new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }, BackgroundRejectReason.Magic);
        Reject("reject_dimensions", "a.png", Png(20000, 100), BackgroundRejectReason.Dimensions);
        Reject("reject_dimensions_jpeg", "a.jpg", Jpeg(100, 17000), BackgroundRejectReason.Dimensions);
        Reject("reject_dimensions_zero", "a.bmp", Bmp(0, 10), BackgroundRejectReason.Dimensions);
        Reject("reject_empty", "a.png", Array.Empty<byte>(), BackgroundRejectReason.Empty);

        var ok = new[] { ("png", Png(320, 200), 320, 200), ("jpg", Jpeg(640, 480), 640, 480), ("jpeg", Jpeg(64, 48), 64, 48), ("bmp", Bmp(32, -16), 32, 16) };
        foreach (var (ext, data, w, h) in ok)
        {
            var r = BackgroundImageStore.Validate("photo." + ext, data, limits);
            var pass = r.Ok && r.Width == w && r.Height == h && BackgroundImageStore.IsValidName(r.FileName) && r.FileName!.IndexOfAny(new[] { '/', '\\' }) < 0;
            Case("accept_" + ext, pass, $"{r.Reason} {r.FileName} {r.Width}x{r.Height}");
        }

        var blobs = new MemoryBlobStore();
        var store = new BackgroundImageStore(blobs, limits);
        var first = await store.ImportAsync("one.png", Png(10, 10), null).ConfigureAwait(true);
        var again = await store.ImportAsync("copy.png", Png(10, 10), first.FileName).ConfigureAwait(true);
        Case("same_image_same_name", first.Ok && first.FileName == again.FileName && blobs.Count == 1, $"{first.FileName} {again.FileName} blobs={blobs.Count}");
        var second = await store.ImportAsync("two.bmp", Bmp(12, 12), first.FileName).ConfigureAwait(true);
        Case("replace_deletes_old", second.Ok && blobs.Count == 1 && blobs.Has(second.FileName!) && !blobs.Has(first.FileName!), $"blobs={string.Join(",", blobs.Names)}");
        var rejected = await store.ImportAsync("bad.png", Png(20000, 10), second.FileName).ConfigureAwait(true);
        Case("reject_keeps_previous", !rejected.Ok && blobs.Has(second.FileName!), $"{rejected.Reason} blobs={blobs.Count}");

        var badNames = new[] { "../x.png", "..\\x.png", "/etc/x.png", "0123456789ABCDEF.png", "0123456789abcdef.gif", "0123456789abcdef.png/..", string.Empty };
        var leaked = new List<string>();
        foreach (var n in badNames)
        {
            if (BackgroundImageStore.IsValidName(n) || await store.LoadAsync(n).ConfigureAwait(true) is not null) { leaked.Add(n); }
        }

        Case("invalid_names_refused", leaked.Count == 0, leaked.Count == 0 ? $"{badNames.Length} refused" : "accepted: " + string.Join(",", leaked));
        Case("load_valid_name", await store.LoadAsync(second.FileName).ConfigureAwait(true) is { Length: > 0 }, second.FileName ?? "null");

        var all = cases.All(c => c?["pass"]?.GetValue<bool>() == true);
        return new JsonObject { ["pass"] = all, ["count"] = cases.Count, ["cases"] = cases };
    }

    private static byte[] Png(int w, int h)
    {
        var d = new byte[33];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R' }.CopyTo(d, 0);
        Be32(d, 16, w);
        Be32(d, 20, h);
        d[24] = 8;
        d[25] = 6;
        return d;
    }

    private static byte[] Jpeg(int w, int h) => new byte[]
    {
        0xFF, 0xD8, 0xFF, 0xE0, 0, 16, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1, 1, 0, 0, 1, 0, 1, 0, 0,
        0xFF, 0xC0, 0, 17, 8, (byte)(h >> 8), (byte)h, (byte)(w >> 8), (byte)w, 3, 1, 0x22, 0, 2, 0x11, 1, 3, 0x11, 1,
        0xFF, 0xD9,
    };

    private static byte[] Bmp(int w, int h)
    {
        var d = new byte[54];
        d[0] = (byte)'B';
        d[1] = (byte)'M';
        BitConverter.GetBytes(w).CopyTo(d, 18);
        BitConverter.GetBytes(h).CopyTo(d, 22);
        return d;
    }

    private static byte[] Pad(byte[] head, int size)
    {
        var d = new byte[size];
        head.CopyTo(d, 0);
        return d;
    }

    private static void Be32(byte[] d, int o, int v)
    {
        d[o] = (byte)(v >> 24);
        d[o + 1] = (byte)(v >> 16);
        d[o + 2] = (byte)(v >> 8);
        d[o + 3] = (byte)v;
    }

    private sealed class MemoryBlobStore : IBlobStore
    {
        private readonly Dictionary<string, byte[]> _items = new(StringComparer.Ordinal);

        public int Count => _items.Count;

        public IEnumerable<string> Names => _items.Keys;

        public bool Has(string name) => _items.ContainsKey(name);

        public Task<byte[]?> ReadAsync(string name) => Task.FromResult(_items.TryGetValue(name, out var v) ? v : null);

        public Task WriteAsync(string name, byte[] data)
        {
            _items[name] = data;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string name)
        {
            _items.Remove(name);
            return Task.CompletedTask;
        }
    }
}
#endif
