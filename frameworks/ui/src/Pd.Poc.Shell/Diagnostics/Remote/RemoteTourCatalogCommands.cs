// 원격 명령(도움말 카탈로그, 요건 6) — 재빌드 없이 파일에서 카탈로그를 읽어 시연·시험한다. 진단 빌드 전용이라 제품에는 없다
#if PD_DIAGNOSTICS // 진단 빌드 전용(PdDiagnostics=true)
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Avalonia.Input;
using Avalonia.VisualTree;
using Pd.Poc.Shell.Tour;

namespace Pd.Poc.Shell.Diagnostics.Remote;

internal static class RemoteTourCatalogCommands
{
    private sealed class FileSource(string path) : ITourCatalogSource
    {
        public string Name => "file";

        public string? ReadJson() => File.Exists(path) ? File.ReadAllText(path) : null;
    }

    public static void Register(RemoteCommandRouter r)
    {
        r.Register("tour.catalog.info", "지금 카탈로그 요약 → version·errors·stepIds·navStepIds·shortcuts·features", _ => Info());
        r.Register("tour.catalog.load", "파일에서 카탈로그 읽기 path= → 요약. 다음 투어 시작부터 적용", a =>
        {
            TourCatalog.UseSource(new FileSource(a.Str("path") ?? throw new RemoteCommandException("path= 가 필요합니다.")));
            return Info();
        });
        r.Register("tour.catalog.bindings", "메인 화면에 걸린 KeyBinding 목록 [run=제스처: 그 명령을 실행] → [{gesture, hasCommand}]·ran. 실제 키 입력이 아니다", Bindings);
        r.Register("tour.catalog.reset", "내장 카탈로그로 되돌리기 → 요약", _ =>
        {
            TourCatalog.UseSource(null);
            return Info();
        });
    }

    private static JsonObject Bindings(JsonObject a)
    {
        var view = VisualTreeInspector.Main().GetVisualDescendants().OfType<InputElement>().FirstOrDefault(e => e.KeyBindings.Count > 0);
        var bindings = view?.KeyBindings.ToList() ?? new List<KeyBinding>();
        var run = a.Str("run");
        var ran = false;
        foreach (var binding in bindings.Where(b => run is not null && b.Gesture?.ToString() == run && b.Command is not null))
        {
            binding.Command!.Execute(null);
            ran = true;
        }

        return new JsonObject
        {
            ["bindings"] = new JsonArray(bindings.Select(b => (JsonNode?)new JsonObject { ["gesture"] = b.Gesture?.ToString(), ["hasCommand"] = b.Command is not null }).ToArray()),
            ["ran"] = ran,
        };
    }

    private static JsonObject Info()
    {
        var data = TourCatalog.Current;
        return new JsonObject
        {
            ["version"] = data.Version,
            ["errors"] = new JsonArray(data.Errors.Select(e => (JsonNode?)e).ToArray()),
            ["stepIds"] = new JsonArray(data.Steps.Select(s => (JsonNode?)s.Id).ToArray()),
            ["navStepIds"] = new JsonArray(TourNavSteps.Steps.Select(s => (JsonNode?)s.Id).ToArray()),
            ["shortcuts"] = data.Shortcuts.Count,
            ["features"] = data.Features.Count,
        };
    }
}
#endif
