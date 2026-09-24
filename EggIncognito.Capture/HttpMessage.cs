using System.Globalization;
using System.Text;

namespace EggIncognito.Capture;

internal sealed record HttpMessage {
    public required string StartLine { get; init; }
    public required List<HttpHeader> Headers { get; init; }
    public byte[]? Body { get; init; }

    public string Method => StartLine.Split(' ') is { Length: > 0 } p ? p[0] : "";
    public string Path => StartLine.Split(' ') is { Length: > 1 } p ? p[1] : "/";

    public int StatusCode {
        get {
            string[] parts = StartLine.Split(' ');
            return parts.Length > 1 && int.TryParse(parts[1], out int c) ? c : 0;
        }
    }

    public bool IsConnectionClose =>
        Headers.Exists(h => h.Name.Equals("Connection", StringComparison.OrdinalIgnoreCase) &&
                         h.Value.Contains("close", StringComparison.OrdinalIgnoreCase));

    private bool IsRequest => !StartLine.StartsWith("HTTP/", StringComparison.OrdinalIgnoreCase);

    public static async Task<HttpMessage?> ReadAsync(Stream s, CancellationToken ct) {
        byte[]? headBytes = await ReadHeadAsync(s, ct);
        if (headBytes is null) return null;

        string headText = Encoding.ASCII.GetString(headBytes);
        string[] lines = headText.Split("\r\n");
        string startLine = lines[0];
        var headers = new List<HttpHeader>();
        for (int i = 1; i < lines.Length; i++) {
            if (lines[i].Length == 0) continue;
            int idx = lines[i].IndexOf(':');
            if (idx <= 0) continue;
            headers.Add(new HttpHeader(lines[i][..idx].Trim(), lines[i][(idx + 1)..].Trim()));
        }

        byte[] body = await ReadBodyAsync(s, headers, ct);
        return new HttpMessage { StartLine = startLine, Headers = headers, Body = body };
    }

    public async Task WriteAsync(Stream s, CancellationToken ct) {
        var sb = new StringBuilder();
        sb.Append(StartLine).Append("\r\n");
        int bodyLen = Body?.Length ?? 0;
        bool wroteLen = false;
        foreach (var h in Headers) {
            if (h.Name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)) continue;
            if (h.Name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) {
                sb.Append("Content-Length: ").Append(bodyLen).Append("\r\n");
                wroteLen = true;
                continue;
            }

            sb.Append(h.Name).Append(": ").Append(h.Value).Append("\r\n");
        }

        if (!wroteLen && (bodyLen > 0 || HasBodySemantics()))
            sb.Append("Content-Length: ").Append(bodyLen).Append("\r\n");
        sb.Append("\r\n");

        await s.WriteAsync(Encoding.ASCII.GetBytes(sb.ToString()), ct);
        if (bodyLen > 0) await s.WriteAsync(Body.AsMemory(0, bodyLen), ct);
        await s.FlushAsync(ct);
    }

    private bool HasBodySemantics() =>
        IsRequest ? Method is "POST" or "PUT" or "PATCH" : StatusCode is not (204 or 304);

    private static async Task<byte[]?> ReadHeadAsync(Stream s, CancellationToken ct) {
        var buf = new List<byte>(1024);
        byte[] one = new byte[1];
        int matched = 0;
        while (true) {
            int n = await s.ReadAsync(one, ct);
            if (n == 0) return buf.Count > 0 ? [.. buf] : null;
            byte b = one[0];
            buf.Add(b);
            matched = (matched, b) switch {
                (0, 13) => 1,
                (1, 10) => 2,
                (2, 13) => 3,
                (3, 10) => 4,
                _ => b == 13 ? 1 : 0
            };
            if (matched == 4) return [.. buf];
            if (buf.Count > 64 * 1024) return [.. buf];
        }
    }

    private static async Task<byte[]> ReadBodyAsync(Stream s, List<HttpHeader> headers, CancellationToken ct) {
        string? te = Get(headers, "Transfer-Encoding");
        if (te is not null && te.Contains("chunked", StringComparison.OrdinalIgnoreCase))
            return await ReadChunkedAsync(s, ct);

        string? clRaw = Get(headers, "Content-Length");
        return clRaw is not null &&
               long.TryParse(clRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long cl) && cl > 0
            ? await ReadExactAsync(s, (int)cl, ct)
            : [];
    }

    private static async Task<byte[]> ReadExactAsync(Stream s, int count, CancellationToken ct) {
        byte[] buf = new byte[count];
        int read = 0;
        while (read < count) {
            int n = await s.ReadAsync(buf.AsMemory(read, count - read), ct);
            if (n == 0) break;
            read += n;
        }

        return read == count ? buf : buf[..read];
    }

    private static async Task<byte[]> ReadChunkedAsync(Stream s, CancellationToken ct) {
        var outBuf = new List<byte>();
        while (true) {
            string? sizeLine = await ReadLineAsync(s, ct);
            if (sizeLine is null) break;
            int semi = sizeLine.IndexOf(';');
            string hex = (semi >= 0 ? sizeLine[..semi] : sizeLine).Trim();
            if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int size)) break;
            if (size == 0) {
                await ReadLineAsync(s, ct);
                break;
            }

            byte[] chunk = await ReadExactAsync(s, size, ct);
            outBuf.AddRange(chunk);
            await ReadLineAsync(s, ct);
        }

        return [.. outBuf];
    }

    private static async Task<string?> ReadLineAsync(Stream s, CancellationToken ct) {
        var sb = new List<byte>();
        byte[] one = new byte[1];
        while (true) {
            int n = await s.ReadAsync(one, ct);
            if (n == 0) return sb.Count > 0 ? Encoding.ASCII.GetString([.. sb]) : null;
            if (one[0] == 13) continue;
            if (one[0] == 10) return Encoding.ASCII.GetString([.. sb]);
            sb.Add(one[0]);
        }
    }

    private static string? Get(List<HttpHeader> headers, string name) =>
        headers.Find(h => h.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value;
}
