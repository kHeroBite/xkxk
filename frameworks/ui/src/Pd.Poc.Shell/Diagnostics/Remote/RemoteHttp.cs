// 원격 제어용 최소 HTTP/1.1 파서·응답기 — TcpListener 연결 1개에서 요청 1건(요청줄·헤더·Content-Length 본문)을 읽고 Connection: close 로 응답
#if PD_DIAGNOSTICS // 진단 빌드 전용(PdDiagnostics=true)
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Pd.Poc.Shell.Diagnostics.Remote;

/// <summary>요청 1건. 헤더 이름은 대소문자 무시, 쿼리 값은 URL 디코딩(+ → 공백).</summary>
public sealed class RemoteHttpRequest
{
    public string Method { get; init; } = "GET";

    public string Path { get; init; } = "/";

    public Dictionary<string, string> Query { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string Body { get; set; } = "";

    public IPEndPoint? Remote { get; init; }
}

/// <summary>
/// 의존성 없는 최소 HTTP. 한 연결에 요청 1건만 처리(keep-alive 없음). 제한: 헤더 64KB, 본문 16MB.
/// 지원하지 않는 것: chunked 요청 본문, 파이프라이닝, TLS. 로컬 루프백 제어 채널 전용이다.
/// </summary>
public static class RemoteHttp
{
    private const int MaxHeaderBytes = 64 * 1024;
    private const int MaxBodyBytes = 16 * 1024 * 1024;

    /// <summary>요청 읽기. 형식이 깨졌거나 한도 초과면 예외(InvalidDataException).</summary>
    public static async Task<RemoteHttpRequest> ReadAsync(Stream s, IPEndPoint? remote, CancellationToken ct)
    {
        var head = new MemoryStream();
        var buf = new byte[4096];
        var bodyStart = -1;
        byte[] all = Array.Empty<byte>();
        while (bodyStart < 0)
        {
            var n = await s.ReadAsync(buf, ct);
            if (n == 0) { throw new InvalidDataException("요청 헤더 전에 연결이 끊겼습니다."); }
            head.Write(buf, 0, n);
            if (head.Length > MaxHeaderBytes + MaxBodyBytes) { throw new InvalidDataException("요청이 너무 큽니다."); }
            all = head.ToArray();
            bodyStart = IndexOfHeaderEnd(all);
            if (bodyStart < 0 && all.Length > MaxHeaderBytes) { throw new InvalidDataException("헤더가 너무 깁니다."); }
        }

        var lines = Encoding.ASCII.GetString(all, 0, bodyStart - 4).Split("\r\n");
        var first = lines[0].Split(' ');
        if (first.Length < 3) { throw new InvalidDataException("요청줄 형식 오류: " + lines[0]); }
        var target = first[1];
        var q = target.IndexOf('?');
        var req = new RemoteHttpRequest
        {
            Method = first[0].ToUpperInvariant(),
            Path = Uri.UnescapeDataString(q >= 0 ? target[..q] : target),
            Remote = remote,
        };
        if (q >= 0) { ParseQuery(target[(q + 1)..], req.Query); }
        for (var i = 1; i < lines.Length; i++)
        {
            var c = lines[i].IndexOf(':');
            if (c > 0) { req.Headers[lines[i][..c].Trim()] = lines[i][(c + 1)..].Trim(); }
        }

        var length = req.Headers.TryGetValue("Content-Length", out var cl) && int.TryParse(cl, out var len) ? len : 0;
        if (length < 0 || length > MaxBodyBytes) { throw new InvalidDataException("본문 길이 오류: " + cl); }
        var body = new MemoryStream();
        body.Write(all, bodyStart, all.Length - bodyStart);
        while (body.Length < length)
        {
            var n = await s.ReadAsync(buf, ct);
            if (n == 0) { break; }
            body.Write(buf, 0, n);
        }

        req.Body = Encoding.UTF8.GetString(body.GetBuffer(), 0, (int)Math.Min(body.Length, length));
        return req;
    }

    /// <summary>응답 쓰기(Connection: close). CORS 헤더는 보내지 않는다.</summary>
    public static async Task WriteAsync(Stream s, int status, string contentType, byte[] body, CancellationToken ct)
    {
        var head = $"HTTP/1.1 {status} {Reason(status)}\r\nContent-Type: {contentType}\r\nContent-Length: {body.Length}\r\n"
            + "Connection: close\r\nCache-Control: no-store\r\n\r\n";
        await s.WriteAsync(Encoding.ASCII.GetBytes(head), ct);
        await s.WriteAsync(body, ct);
        await s.FlushAsync(ct);
    }

    private static int IndexOfHeaderEnd(byte[] b)
    {
        for (var i = 3; i < b.Length; i++)
        {
            if (b[i - 3] == '\r' && b[i - 2] == '\n' && b[i - 1] == '\r' && b[i] == '\n') { return i + 1; }
        }

        return -1;
    }

    private static void ParseQuery(string qs, Dictionary<string, string> into)
    {
        foreach (var part in qs.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            var k = Decode(eq >= 0 ? part[..eq] : part);
            into[k] = eq >= 0 ? Decode(part[(eq + 1)..]) : "";
        }
    }

    private static string Decode(string s) => Uri.UnescapeDataString(s.Replace('+', ' '));

    private static string Reason(int status) => status switch
    {
        200 => "OK",
        400 => "Bad Request",
        401 => "Unauthorized",
        403 => "Forbidden",
        404 => "Not Found",
        _ => "Internal Server Error",
    };
}
#endif
