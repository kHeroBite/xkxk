// 원격 명령(불러오는 중·즐겨찾기 판정용) — grid.search·grid.refresh·sample.load·favorite.state·toggle·click (AC-016~019)
#if PD_DIAGNOSTICS // 진단 빌드 전용(PdDiagnostics=true)
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Avalonia.VisualTree;
using xkxk.frameworks.ui.data.Samples;
using xkxk.frameworks.ui.shell.Diagnostics.Remote;
using xkxk.frameworks.ui.shell.ViewModels;

namespace xkxk.frameworks.ui.shell.Diagnostics
{
    /// <summary>표 화면의 불러오기 진단 표면(화면 형식을 모른 채 검색·새로고침을 건다).</summary>
    public interface IGridLoadDiagnostics
    {
        void DiagSearch(string text);

        void DiagRefresh();

        int DiagVisibleCount { get; }
    }
}

namespace xkxk.frameworks.ui.shell.Diagnostics.Remote
{
    internal static partial class RemoteBusyCommands
    {
        private static void RegisterGrid(RemoteCommandRouter r)
        {
            r.Register("grid.search", "표 검색(화면 입력과 같은 경로, 끝을 기다리지 않음 — window.load.state 로 관찰) doc= text=(생략=전체) → {started,visibleBefore}", a =>
            {
                var grid = Grid(a);
                var before = grid.DiagVisibleCount;
                grid.DiagSearch(a.Str("text") ?? "");
                return new JsonObject { ["started"] = true, ["visibleBefore"] = before };
            });
            r.Register("grid.refresh", "표 새로고침(끝을 기다리지 않음) doc= → {started,visibleBefore}", a =>
            {
                var grid = Grid(a);
                var before = grid.DiagVisibleCount;
                grid.DiagRefresh();
                return new JsonObject { ["started"] = true, ["visibleBefore"] = before };
            });
            r.Register("sample.load", "표본 조회 지연(준 값만) delayMs= blockMs= → {delayMs,blockMs}", a =>
            {
                if (a.Int("delayMs") is { } d) { SampleLoadDelay.DelayMs = d; }
                if (a.Int("blockMs") is { } b) { SampleLoadDelay.BlockMs = b; }
                return new JsonObject { ["delayMs"] = SampleLoadDelay.DelayMs, ["blockMs"] = SampleLoadDelay.BlockMs };
            });
            r.Register("favorite.state", "제목 줄 즐겨찾기 상태 → {shown,position,items[],visible[],overflow[],openDocs,active}", _ => FavoriteState());
            r.Register("favorite.toggle", "즐겨찾기 넣기/빼기 screen= → favorite.state", async a =>
            {
                await Favorites().ToggleScreenAsync(a.ReqStr("screen"));
                await Task.Delay(100); // 목록은 저장소 알림을 받아 UI 스레드에서 다시 읽는다
                return (JsonNode?)FavoriteState();
            });
            r.Register("favorite.click", "즐겨찾기 아이콘 클릭과 같은 명령 screen= → favorite.state", a =>
            {
                var vm = Favorites();
                var id = a.ReqStr("screen");
                var item = vm.Items.FirstOrDefault(i => i.ScreenId == id) ?? throw new RemoteCommandException($"즐겨찾기에 없습니다: {id}");
                vm.OpenCommand.Execute(item);
                return FavoriteState();
            });
        }

        private static IGridLoadDiagnostics Grid(JsonObject a)
        {
            var doc = RemoteShellAccess.Doc(a, "doc");
            return VisualTreeInspector.DocumentHost(doc)?.GetVisualDescendants().OfType<IGridLoadDiagnostics>().FirstOrDefault()
                ?? throw new RemoteCommandException("표 문서가 화면에 없습니다(뷰 생성 전·최소화·다른 종류).");
        }

        private static FavoriteBarViewModel Favorites() =>
            App.MainViewModel?.FavoriteBar ?? throw new RemoteCommandException("셸이 없습니다.");

        private static JsonObject FavoriteState()
        {
            var vm = Favorites();
            var shell = RemoteShellAccess.Shell;
            return new JsonObject
            {
                ["shown"] = vm.IsShown,
                ["position"] = vm.Position.ToString(),
                ["items"] = new JsonArray(vm.Items.Select(i => (JsonNode?)JsonValue.Create(i.ScreenId)).ToArray()),
                ["visible"] = new JsonArray(vm.VisibleItems.Select(i => (JsonNode?)JsonValue.Create(i.ScreenId)).ToArray()),
                ["overflow"] = new JsonArray(vm.OverflowItems.Select(i => (JsonNode?)JsonValue.Create(i.ScreenId)).ToArray()),
                ["openDocs"] = shell.Documents.Count(),
                ["active"] = shell.ActiveDocument?.Id,
            };
        }
    }
}
#endif
