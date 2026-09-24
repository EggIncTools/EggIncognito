using System.Net;
using System.Net.Sockets;
using System.Text;
using EggIncognito.Core.Services;
using Microsoft.Extensions.Hosting;

namespace EggIncognito.Capture;

public sealed class ProxyFrontDoor(
    HostedCaptureOptions opts,
    CaptureSessionManager sessions,
    Func<IPAddress, Task<string?>> addrToUser,
    Action<string>? log = null) : IHostedService, IAsyncDisposable {
    private const int MaxFirstRequestBytes = 16 * 1024;
    private static readonly TimeSpan FirstRequestTimeout = TimeSpan.FromSeconds(10);
    private Task? _acceptLoop;
    private CancellationTokenSource? _cts;

    private TcpListener? _listener;

    public int Port { get; private set; }

    public async ValueTask DisposeAsync() {
        await StopAsync(CancellationToken.None);
        _cts?.Dispose();
        _cts = null;
    }

    public Task StartAsync(CancellationToken cancellationToken) {
        var listener = new TcpListener(IPAddress.IPv6Any, opts.FrontDoorPort);
        listener.Server.DualMode = true;
        listener.Start();
        _listener = listener;
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _acceptLoop = Task.Run(async () => {
            while (!ct.IsCancellationRequested) {
                TcpClient client;
                try {
                    client = await listener.AcceptTcpClientAsync(ct);
                } catch (OperationCanceledException) {
                    break;
                } catch (SocketException) {
                    break;
                }

                _ = Task.Run(() => HandleAsync(client, ct), CancellationToken.None);
            }
        }, CancellationToken.None);
        log?.Invoke($"capture-frontdoor: listening on {Port}");
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken) {
        try {
            if (_cts is not null) await _cts.CancelAsync();
        } catch (ObjectDisposedException) {
            /* already torn down */
        }

        _listener?.Stop();
        var loop = _acceptLoop;
        _acceptLoop = null;
        if (loop is not null) {
            try {
                await loop;
            } catch {
                /* shutdown */
            }
        }
    }

    private async Task HandleAsync(TcpClient client, CancellationToken ct) {
        using var c = client;
        c.NoDelay = true;
        var stream = c.GetStream();
        try {
            var destAddr = (c.Client.LocalEndPoint as IPEndPoint)?.Address;
            if (destAddr is null || destAddr.Equals(IPAddress.IPv6Any) || destAddr.Equals(IPAddress.Any) ||
                destAddr.IsIPv4MappedToIPv6) {
                GracefulClose(c);
                return;
            }

            string? user = await addrToUser(destAddr);
            if (user is null) {
                GracefulClose(c);
                return;
            }

            (var first, byte[] raw, int rawLen) = await ReadFirstRequestAsync(stream, ct);
            if (first is null) return;

            if (!AuxbrainHosts.IsAuxbrain(first.TargetHost) && !opts.IsExtraAllowed(first.TargetHost)) {
                log?.Invoke($"capture-frontdoor: {user} rejected host {first.TargetHost}");
                await WriteAsciiAsync(stream, "HTTP/1.1 403 Forbidden\r\nConnection: close\r\n\r\n", ct);
                return;
            }

            var session = sessions.Get(user);
            if (session is null || session.State != CaptureState.Running) {
                const string body = "start a capture session on /capture first";
                await WriteAsciiAsync(stream,
                    $"HTTP/1.1 503 Service Unavailable\r\nContent-Type: text/plain\r\n" +
                    $"Content-Length: {body.Length}\r\nConnection: close\r\n\r\n{body}", ct);
                return;
            }

            await TunnelAsync(stream, session, first, raw, rawLen, user, ct);
        } catch (OperationCanceledException) {
            /* shutdown */
        } catch (IOException) {
            /* peer reset */
        } catch (SocketException) {
            /* peer reset */
        }
    }

    private async Task TunnelAsync(
        NetworkStream stream, CaptureSession session, ProxyFirstRequest first,
        byte[] raw, int rawLen, string user, CancellationToken ct) {
        using var inner = new TcpClient { NoDelay = true };
        await inner.ConnectAsync(IPAddress.Loopback, session.Port + 1, ct);
        var innerStream = inner.GetStream();

        byte[] cleanConnect = Encoding.ASCII.GetBytes(
            $"CONNECT {first.TargetHost}:{first.TargetPort} HTTP/1.1\r\nHost: {first.TargetHost}:{first.TargetPort}\r\n\r\n");
        await innerStream.WriteAsync(cleanConnect, ct);

        byte[] respBuf = new byte[8 * 1024];
        int respLen = 0;
        while (respLen < respBuf.Length) {
            int n = await innerStream.ReadAsync(respBuf.AsMemory(respLen), ct);
            if (n == 0) break;
            respLen += n;
            if (FindHeaderEnd(respBuf.AsSpan(0, respLen)) >= 0) break;
        }

        if (respLen == 0) return;
        await stream.WriteAsync(respBuf.AsMemory(0, respLen), ct);

        long up = cleanConnect.Length, down = respLen;

        int connectHeaderLen = first.RawBytes.Length;
        int leftover = rawLen - connectHeaderLen;
        if (leftover > 0) {
            await innerStream.WriteAsync(raw.AsMemory(connectHeaderLen, leftover), ct);
            up += leftover;
        }

        var pumpUp = PumpAsync(stream, innerStream, inner.Client, n => Interlocked.Add(ref up, n), ct);
        var pumpDown = PumpAsync(innerStream, stream, stream.Socket, n => Interlocked.Add(ref down, n), ct);
        await Task.WhenAll(pumpUp, pumpDown);
        log?.Invoke(
            $"capture-frontdoor: {user} {first.TargetHost} {Interlocked.Read(ref up)}/{Interlocked.Read(ref down)}");
    }

    private static int FindHeaderEnd(ReadOnlySpan<byte> b) {
        for (int i = 0; i + 3 < b.Length; i++) {
            if (b[i] == '\r' && b[i + 1] == '\n' && b[i + 2] == '\r' && b[i + 3] == '\n')
                return i;
        }

        return -1;
    }

    private async Task<(ProxyFirstRequest? First, byte[] Buffer, int Length)> ReadFirstRequestAsync(
        NetworkStream stream, CancellationToken ct) {
        using var timed = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timed.CancelAfter(FirstRequestTimeout);
        byte[] buffer = new byte[MaxFirstRequestBytes];
        int total = 0;
        try {
            while (total < buffer.Length) {
                int n = await stream.ReadAsync(buffer.AsMemory(total), timed.Token);
                if (n == 0) return (null, buffer, total);
                total += n;
                if (ProxyRequestParser.TryParse(buffer.AsSpan(0, total)) is { } first)
                    return (first, buffer, total);
            }
        } catch (OperationCanceledException) when (!ct.IsCancellationRequested) {
            log?.Invoke($"capture-frontdoor: first request timed out after {total} bytes");
        }

        return (null, buffer, total);
    }

    private static async Task PumpAsync(Stream from, Stream to, Socket dstSocket, Action<long> count,
        CancellationToken ct) {
        byte[] buf = new byte[8 * 1024];
        try {
            while (true) {
                int n = await from.ReadAsync(buf, ct);
                if (n == 0) return;
                await to.WriteAsync(buf.AsMemory(0, n), ct);
                count(n);
            }
        } catch (OperationCanceledException) {
            /* torn down */
        } catch (IOException) {
            /* peer closed */
        } catch (ObjectDisposedException) {
            /* torn down */
        } finally {
            try {
                dstSocket.Shutdown(SocketShutdown.Send);
            } catch {
                /* already closed */
            }
        }
    }

    private static Task WriteAsciiAsync(Stream stream, string text, CancellationToken ct) =>
        stream.WriteAsync(Encoding.ASCII.GetBytes(text), ct).AsTask();

    private static void GracefulClose(TcpClient c) {
        try {
            c.Client.Shutdown(SocketShutdown.Send);
        } catch (SocketException) {
            /* already gone */
        } catch (ObjectDisposedException) {
            /* already gone */
        }
    }
}
