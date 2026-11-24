// 배경 장식·사용자 이미지 원격 명령(AC-014 판정용, 21 §6) — background.import·set·corrupt·state
#if PD_DIAGNOSTICS // 진단 빌드 전용(PdDiagnostics=true)
using System;
using System.Globalization;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Avalonia;
using xkxk.frameworks.ui.data.Theming;
using xkxk.frameworks.ui.shell.Theming;

namespace xkxk.frameworks.ui.shell.Diagnostics.Remote;

/// <summary>쓰기는 UiSettings·BackgroundImageStore(화면과 같은 경로), 읽기는 DesignThemeResolver 실효값·적용기 진단 값만 쓴다.</summary>
internal static class RemoteBackgroundCommands
{
    internal static void Register(RemoteCommandRouter r)
    {
        RemoteEffectPerfCommands.Register(r);
        r.Register("background.import", "배경 이미지 가져오기 path=<로컬 파일> — 검사·해시 이름 저장 뒤 decor=image → {ok,reason,file,width,height}", a => Import(a.ReqStr("path")));
        r.Register("background.set", "배경 이미지 값 fit=<fill|fit|stretch|tile|center> opacity=<0~1> dim=<0~0.8> clear=true(선택 지움) → background.state", a => Set(a));
        r.Register("background.corrupt", "손상 파일 케이스 — 지금 이미지 이름의 내용을 쓰레기 바이트로 덮고 다시 읽게 한다 → {file}", _ => Corrupt());
        r.Register("background.state", "배경 상태 → {decorStored,decorEffective,file,fit,opacity,dim,ready,lastFallback,fallbackCount,hasDecorResource,store}", _ => State());
    }

    private static async Task<JsonNode?> Import(string path)
    {
        var blobs = DesignEffectsApplier.BlobStore ?? throw new RemoteCommandException("배경 이미지 저장소(BlobStore)가 연결되지 않았습니다.");
        var s = UiSettings.Current;
        var data = await File.ReadAllBytesAsync(path);
        var r = await new BackgroundImageStore(blobs, s.BackgroundImageLimits).ImportAsync(Path.GetFileName(path), data, s.BackgroundImageFile);
        if (r.Ok)
        {
            s.BackgroundImageFile = r.FileName;
            s.SetEffectOverride("decor", "image");
        }

        return new JsonObject { ["ok"] = r.Ok, ["reason"] = r.Reason.ToString(), ["file"] = r.FileName, ["width"] = r.Width, ["height"] = r.Height };
    }

    private static JsonNode Set(JsonObject a)
    {
        var s = UiSettings.Current;
        if (a.Str("fit") is { } fit) { s.BackgroundImageFit = UiKeyNames.Parse(fit, s.BackgroundImageFit); }
        if (Num(a, "opacity") is { } o) { s.BackgroundImageOpacity = o; }
        if (Num(a, "dim") is { } d) { s.BackgroundImageDim = d; }
        if (a.Bool("clear", false)) { s.BackgroundImageFile = null; }
        return State();
    }

    private static async Task<JsonNode?> Corrupt()
    {
        var file = UiSettings.Current.BackgroundImageFile ?? throw new RemoteCommandException("배경 이미지가 선택되지 않았습니다.");
        var blobs = DesignEffectsApplier.BlobStore ?? throw new RemoteCommandException("배경 이미지 저장소(BlobStore)가 연결되지 않았습니다.");
        await blobs.WriteAsync(file, new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07 });
        DesignEffectsApplier.ResetBackgroundImage();
        return new JsonObject { ["file"] = file };
    }

    private static JsonNode State()
    {
        var s = UiSettings.Current;
        return new JsonObject
        {
            ["decorStored"] = DesignThemeResolver.Stored("decor"),
            ["decorEffective"] = DesignThemeResolver.Effective("decor"),
            ["file"] = s.BackgroundImageFile,
            ["fit"] = UiKeyNames.Of(s.BackgroundImageFit),
            ["opacity"] = s.BackgroundImageOpacity,
            ["dim"] = s.BackgroundImageDim,
            ["ready"] = DesignEffectsApplier.BackgroundImageReady,
            ["lastFallback"] = DesignEffectsApplier.LastBackgroundFallback,
            ["fallbackCount"] = DesignEffectsApplier.BackgroundFallbackCount,
            ["hasDecorResource"] = Application.Current?.Resources.ContainsKey("PdStyleDecor") ?? false,
            ["store"] = DesignEffectsApplier.BlobStore?.GetType().Name,
        };
    }

    private static double? Num(JsonObject a, string key) =>
        a.Str(key) is { } t && double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
}
#endif
