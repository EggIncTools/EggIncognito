using System.Net;
using System.Net.Sockets;
using System.Text;

namespace EggIncognito.Capture;

public sealed class LanForwarder : IAsyncDisposable {
    private static readonly TimeSpan DisconnectGrace = TimeSpan.FromSeconds(45);
    private readonly Dictionary<string, int> _connsPerIp = [with(StringComparer.Ordinal)];
    private readonly CancellationTokenSource _cts = new();
    private readonly Lock _devLock = new();
    private readonly HashSet<string> _disconnectPending = [with(StringComparer.Ordinal)];
    private readonly TcpListener _listener;
    private readonly int _proxyPort;
    private Task? _acceptLoop;

    public LanForwarder(int publicPort, int proxyPort) {
        _listener = new TcpListener(IPAddress.IPv6Any, publicPort);
        _listener.Server.DualMode = true;
        _proxyPort = proxyPort;
    }

    public Action<string>? Trace { get; set; }

    public Action<string, int>? DeviceConnected { get; set; }
    public Action<string, int>? DeviceDisconnected { get; set; }

    public async ValueTask DisposeAsync() {
        await _cts.CancelAsync();
        try {
            _listener.Stop();
        } catch {
            /* already stopped */
        }

        if (_acceptLoop is not null) {
            try {
                await _acceptLoop;
            } catch {
                /* ignored */
            }
        }

        _cts.Dispose();
    }

    private void Log(string m) => Trace?.Invoke(m);

    public void Start() {
        _listener.Start();
        _acceptLoop = AcceptLoopAsync(_cts.Token);
    }

    private async Task AcceptLoopAsync(CancellationToken ct) {
        while (!ct.IsCancellationRequested) {
            TcpClient client;
            try {
                client = await _listener.AcceptTcpClientAsync(ct);
            } catch (OperationCanceledException) {
                break;
            } catch (ObjectDisposedException) {
                break;
            } catch (SocketException ex) {
                if (IsFatalAcceptError(ex, ct.IsCancellationRequested)) break;
                Log($"FWD accept error: {ex.SocketErrorCode} - retrying");
                continue;
            }

            Log($"FWD accept from {client.Client.RemoteEndPoint}");
            _ = HandleAsync(client, ct);
        }
    }

    internal static bool IsFatalAcceptError(SocketException ex, bool cancelRequested) =>
        cancelRequested || ex.SocketErrorCode is SocketError.Interrupted or SocketError.OperationAborted;

    private async Task HandleAsync(TcpClient client, CancellationToken ct) {
        string deviceIp = DeviceIp(client);
        OnDeviceConnectionOpened(deviceIp);
        try {
            using (client) {
                using var upstream = new TcpClient();
                try {
                    await upstream.ConnectAsync(IPAddress.Loopback, _proxyPort, ct);
                } catch (Exception ex) {
                    Log($"FWD upstream connect failed: {ex.Message}");
                    return;
                }

                client.NoDelay = true;
                upstream.NoDelay = true;
                var cs = client.GetStream();
                var us = upstream.GetStream();

                if (!await ForwardCleanedConnectAsync(cs, us, ct))
                    return;

                var c2u = PumpAsync(cs, us, upstream, "c2u", ct);
                var u2c = PumpAsync(us, cs, client, "u2c", ct);
                try {
                    await Task.WhenAll(c2u, u2c);
                } catch {
                    /* reset or cancel - normal teardown */
                }
            }
        } finally {
            OnDeviceConnectionClosed(deviceIp);
        }
    }

    private void OnDeviceConnectionOpened(string ip) {
        bool firstForIp;
        int deviceCount;
        lock (_devLock) {
            firstForIp = !_connsPerIp.TryGetValue(ip, out int c) || c == 0;
            _connsPerIp[ip] = (firstForIp ? 0 : c) + 1;
            deviceCount = _connsPerIp.Count(kv => kv.Value > 0);
        }

        if (firstForIp) DeviceConnected?.Invoke(ip, deviceCount);
    }

    private void OnDeviceConnectionClosed(string ip) {
        lock (_devLock) {
            if (_connsPerIp.TryGetValue(ip, out int c) && c > 0) _connsPerIp[ip] = c - 1;
            if (_connsPerIp.GetValueOrDefault(ip) > 0) return;
            if (!_disconnectPending.Add(ip)) return;
        }

        _ = DebouncedDisconnectAsync(ip);
    }

    private async Task DebouncedDisconnectAsync(string ip) {
        try {
            await Task.Delay(DisconnectGrace, _cts.Token);
        } catch (OperationCanceledException) {
            return;
        }

        int deviceCount;
        lock (_devLock) {
            _disconnectPending.Remove(ip);
            if (_connsPerIp.GetValueOrDefault(ip) > 0) return;
            _connsPerIp.Remove(ip);
            deviceCount = _connsPerIp.Count(kv => kv.Value > 0);
        }

        DeviceDisconnected?.Invoke(ip, deviceCount);
    }

    private async Task<bool> ForwardCleanedConnectAsync(NetworkStream cs, NetworkStream us, CancellationToken ct) {
        byte[] buf = new byte[8192];
        int len = 0;
        int headerEnd = -1;
        while (headerEnd < 0) {
            if (len == buf.Length) {
                Log("FWD head too large");
                return false;
            }

            int n = await cs.ReadAsync(buf.AsMemory(len, buf.Length - len), ct);
            if (n == 0) return false;
            len += n;
            headerEnd = IndexOfDoubleCrlf(buf, len);
        }

        string head = Encoding.ASCII.GetString(buf, 0, headerEnd);
        string cleaned = CleanConnectHead(head);
        byte[] cleanedBytes = Encoding.ASCII.GetBytes(cleaned);
        await us.WriteAsync(cleanedBytes, ct);

        int rest = len - (headerEnd + 4);
        if (rest > 0) await us.WriteAsync(buf.AsMemory(headerEnd + 4, rest), ct);
        await us.FlushAsync(ct);
        Log($"FWD CONNECT raw-head[{head.Replace("\r", "\\r").Replace("\n", "\\n")}]");
        Log($"FWD CONNECT sent[{cleaned.Replace("\r", "\\r").Replace("\n", "\\n")}]");
        return true;
    }

    internal static string CleanConnectHead(string head) {
        string[] lines = head.Split("\r\n");
        string requestLine = lines[0];
        string[] parts = requestLine.Split(' ');
        string method = parts.Length >= 1 ? parts[0] : "";
        string target = parts.Length >= 2 ? parts[1] : "";

        string authority = method.Equals("CONNECT", StringComparison.OrdinalIgnoreCase)
            ? target
            : Uri.TryCreate(target, UriKind.Absolute, out var uri)
                ? uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}"
                : "";

        var kept = new List<string> { requestLine };
        bool hostWritten = false;
        foreach (string line in lines.Skip(1)) {
            string name = line.Split(':', 2)[0].Trim();

            if (name.Equals("Connection", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Proxy-Connection", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Keep-Alive", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            if (name.Equals("Host", StringComparison.OrdinalIgnoreCase)) {
                kept.Add(authority.Length > 0 ? $"Host: {authority}" : line);
                hostWritten = true;
                continue;
            }

            kept.Add(line);
        }

        if (!hostWritten && authority.Length > 0) kept.Add($"Host: {authority}");

        return string.Join("\r\n", kept) + "\r\n\r\n";
    }

    private static string DeviceIp(TcpClient client) =>
        client.Client.RemoteEndPoint is IPEndPoint ep ? DeviceIp(ep.Address) : "unknown";

    internal static string DeviceIp(IPAddress addr) =>
        (addr.IsIPv4MappedToIPv6 ? addr.MapToIPv4() : addr).ToString();

    internal static int IndexOfDoubleCrlf(byte[] b, int len) {
        for (int i = 0; i + 3 < len; i++) {
            if (b[i] == 13 && b[i + 1] == 10 && b[i + 2] == 13 && b[i + 3] == 10)
                return i;
        }

        return -1;
    }

    private async Task PumpAsync(NetworkStream src, NetworkStream dst, TcpClient dstClient, string dir,
        CancellationToken ct) {
        byte[] buffer = new byte[16 * 1024];
        long total = 0;
        bool first = true;
        try {
            int n;
            while ((n = await src.ReadAsync(buffer, ct)) > 0) {
                if (first) {
                    Log($"FWD {dir} first {n}B: {Preview(buffer, n)}");
                    first = false;
                }

                total += n;
                await dst.WriteAsync(buffer.AsMemory(0, n), ct);
                await dst.FlushAsync(ct);
            }
        } catch (Exception ex) {
            Log($"FWD {dir} pump error after {total}B: {ex.Message}");
        } finally {
            if (total == 0) Log($"FWD {dir} closed with 0 bytes");
            try {
                dstClient.Client.Shutdown(SocketShutdown.Send);
            } catch {
                /* already closed */
            }
        }
    }

    private static string Preview(byte[] b, int n) {
        int len = Math.Min(n, 300);
        char[] chars = new char[len];
        for (int i = 0; i < len; i++) chars[i] = b[i] is >= 32 and < 127 ? (char)b[i] : '.';
        return new string(chars);
    }
}
