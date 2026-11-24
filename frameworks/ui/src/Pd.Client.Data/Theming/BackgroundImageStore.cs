// 사용자 배경 이미지 저장소(REQ-112·21 §6.2) — 확장자·매직 바이트·머리 가로세로 선읽기·크기 상한을 검사하고 sha256 해시 이름으로 IBlobStore 에 둔다. 디코드는 하지 않는다(UI 비의존)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Pd.Client.Data.Theming;

/// <summary>이름 붙은 바이트 저장소. 이름은 BackgroundImageStore.NamePattern 에 맞는 것만 들어온다.</summary>
public interface IBlobStore
{
    /// <summary>없으면 null.</summary>
    Task<byte[]?> ReadAsync(string name);

    Task WriteAsync(string name, byte[] data);

    /// <summary>없는 이름을 지워도 오류가 아니다.</summary>
    Task DeleteAsync(string name);
}

/// <summary>거부 사유. None = 통과.</summary>
public enum BackgroundRejectReason
{
    None,
    Empty,
    TooLarge,
    Format,
    Magic,
    Dimensions,
}

/// <summary>운영 상한(ui.background.image.max_bytes·max_source_px·formats). 기본값은 plt_2 §12.1 키 기본값과 같다.</summary>
public sealed record BackgroundImageLimits(long MaxBytes, int MaxSourcePx, IReadOnlyList<string> Formats)
{
    public const long DefaultMaxBytes = 10_485_760;
    public const int DefaultMaxSourcePx = 16_384;
    public static readonly IReadOnlyList<string> DefaultFormats = new[] { "png", "jpg", "jpeg", "bmp" };
    public static BackgroundImageLimits Default { get; } = new(DefaultMaxBytes, DefaultMaxSourcePx, DefaultFormats);
}

/// <summary>검사·가져오기 결과. Ok 이면 FileName 이 저장된 해시 이름이다.</summary>
public sealed record BackgroundImportResult(BackgroundRejectReason Reason, string? FileName, string? Format, int Width, int Height)
{
    public bool Ok => Reason == BackgroundRejectReason.None;
}

/// <summary>
/// 배경 이미지 가져오기·읽기·교체. 저장 키(ui.background.image.file)에는 해시 이름만 쓴다 — 경로 구분자가 들어갈 수 없다(NamePattern).
/// 확장자는 사용자가 고른 파일 이름에서, 실제 형식은 매직 바이트에서 판정하며 둘이 다르면 거부한다. 저장 이름의 확장자는 실제 형식이다.
/// </summary>
public sealed class BackgroundImageStore
{
    /// <summary>저장 이름 규칙: sha256 앞 16자(소문자 16진) + 확장자.</summary>
    public static readonly Regex NamePattern = new("^[0-9a-f]{16}\\.(png|jpg|jpeg|bmp)$", RegexOptions.CultureInvariant);

    private readonly IBlobStore _blobs;

    public BackgroundImageStore(IBlobStore blobs, BackgroundImageLimits? limits = null)
    {
        _blobs = blobs ?? throw new ArgumentNullException(nameof(blobs));
        Limits = limits ?? BackgroundImageLimits.Default;
    }

    public BackgroundImageLimits Limits { get; }

    public static bool IsValidName(string? name) => name is not null && NamePattern.IsMatch(name);

    /// <summary>검사만 한다(저장 없음). sourceFileName 은 확장자 판정에만 쓴다.</summary>
    public static BackgroundImportResult Validate(string sourceFileName, byte[] data, BackgroundImageLimits limits)
    {
        if (data is null || data.Length == 0) { return Reject(BackgroundRejectReason.Empty); }
        if (data.LongLength > limits.MaxBytes) { return Reject(BackgroundRejectReason.TooLarge); }
        var ext = Path.GetExtension(sourceFileName ?? string.Empty).TrimStart('.').ToLowerInvariant();
        if (ext.Length == 0 || !limits.Formats.Contains(ext, StringComparer.OrdinalIgnoreCase)) { return Reject(BackgroundRejectReason.Format); }
        var actual = DetectFormat(data);
        if (actual is null || actual != (ext == "jpeg" ? "jpg" : ext)) { return Reject(BackgroundRejectReason.Magic); }
        var dims = ReadDimensions(data, actual);
        if (dims is not { } d || d.Width <= 0 || d.Height <= 0 || d.Width > limits.MaxSourcePx || d.Height > limits.MaxSourcePx)
        {
            return new BackgroundImportResult(BackgroundRejectReason.Dimensions, null, actual, dims?.Width ?? 0, dims?.Height ?? 0);
        }

        return new BackgroundImportResult(BackgroundRejectReason.None, HashName(data, actual), actual, d.Width, d.Height);
    }

    /// <summary>검사 뒤 해시 이름으로 저장하고, 성공하면 옛 이름(previousName, 다르고 규칙에 맞을 때만)을 지운다. 같은 이미지는 같은 이름이다.</summary>
    public async Task<BackgroundImportResult> ImportAsync(string sourceFileName, byte[] data, string? previousName)
    {
        var result = Validate(sourceFileName, data, Limits);
        if (!result.Ok || result.FileName is null) { return result; }
        await _blobs.WriteAsync(result.FileName, data).ConfigureAwait(false);
        if (IsValidName(previousName) && !string.Equals(previousName, result.FileName, StringComparison.Ordinal))
        {
            await _blobs.DeleteAsync(previousName!).ConfigureAwait(false);
        }

        return result;
    }

    /// <summary>저장 이름으로 읽는다. 규칙에 안 맞는 이름·없는 파일은 null(호출 쪽은 decor none + 알림 1회, 저장값 보존).</summary>
    public async Task<byte[]?> LoadAsync(string? name) =>
        IsValidName(name) ? await _blobs.ReadAsync(name!).ConfigureAwait(false) : null;

    /// <summary>저장 이름을 지운다(규칙에 맞을 때만).</summary>
    public Task DeleteAsync(string? name) => IsValidName(name) ? _blobs.DeleteAsync(name!) : Task.CompletedTask;

    public static string HashName(byte[] data, string format) =>
        Convert.ToHexString(SHA256.HashData(data)).Substring(0, 16).ToLowerInvariant() + "." + format;

    /// <summary>매직 바이트 → "png"·"jpg"·"bmp". 모르면 null.</summary>
    public static string? DetectFormat(byte[] d)
    {
        if (d.Length >= 8 && d[0] == 0x89 && d[1] == 0x50 && d[2] == 0x4E && d[3] == 0x47 && d[4] == 0x0D && d[5] == 0x0A && d[6] == 0x1A && d[7] == 0x0A) { return "png"; }
        if (d.Length >= 3 && d[0] == 0xFF && d[1] == 0xD8 && d[2] == 0xFF) { return "jpg"; }
        if (d.Length >= 2 && d[0] == (byte)'B' && d[1] == (byte)'M') { return "bmp"; }
        return null;
    }

    /// <summary>머리만 읽어 가로세로를 낸다(PNG IHDR·JPEG SOFn·BMP 정보 머리). 못 읽으면 null.</summary>
    public static (int Width, int Height)? ReadDimensions(byte[] d, string format) => format switch
    {
        "png" => d.Length >= 24 && d[12] == (byte)'I' && d[13] == (byte)'H' && d[14] == (byte)'D' && d[15] == (byte)'R'
            ? (ToIntBe(d, 16), ToIntBe(d, 20)) : null,
        "bmp" => d.Length >= 26 ? (BitConverter.ToInt32(d, 18), Math.Abs(BitConverter.ToInt32(d, 22))) : null,
        "jpg" => JpegDimensions(d),
        _ => null,
    };

    private static (int Width, int Height)? JpegDimensions(byte[] d)
    {
        var i = 2;
        while (i + 3 < d.Length)
        {
            if (d[i] != 0xFF) { return null; }
            var marker = d[i + 1];
            if (marker == 0xFF) { i++; continue; }                                    // 채움 바이트
            if (marker == 0xD8 || marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7)) { i += 2; continue; } // 길이 없는 표지
            if (marker == 0xD9 || marker == 0xDA) { return null; }                      // 끝·스캔 시작 전에 SOF 가 없었다
            var len = (d[i + 2] << 8) | d[i + 3];
            if (len < 2) { return null; }
            var isSof = marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC;
            if (isSof)
            {
                if (i + 8 >= d.Length) { return null; }
                return (((d[i + 7] << 8) | d[i + 8]), ((d[i + 5] << 8) | d[i + 6]));
            }

            i += 2 + len;
        }

        return null;
    }

    private static int ToIntBe(byte[] d, int o)
    {
        var v = ((uint)d[o] << 24) | ((uint)d[o + 1] << 16) | ((uint)d[o + 2] << 8) | d[o + 3];
        return v > int.MaxValue ? int.MaxValue : (int)v;
    }

    private static BackgroundImportResult Reject(BackgroundRejectReason reason) => new(reason, null, null, 0, 0);
}
