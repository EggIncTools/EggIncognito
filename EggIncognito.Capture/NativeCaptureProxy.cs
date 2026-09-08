using System.Buffers;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using EggIncognito.Core.Services;

namespace EggIncognito.Capture;

public sealed class NativeCaptureProxy(bool verbose = false) : ICaptureProxy {
    private const string RootCaName = "EggIncognito Capture Root";

    private static readonly TimeSpan TrustGrace = TimeSpan.FromSeconds(25);
    private readonly ConcurrentDictionary<string, X509Certificate2> _leafCache = new();
    private readonly Lock _trustGate = new();
    private Task? _acceptLoop;
    private CancellationTokenSource? _cts;
    private LanForwarder? _forwarder;

    private TcpListener? _listener;

    private X509Certificate2? _rootCa;
    private X509Certificate2? _rootCaPublic;
    private bool _trustAdded;
    private bool _trustProven;
    private Timer? _trustTimer;
    private bool _untrustedReported;

    public bool LanForwarderEnabled { get; init; } = true;
    public bool TrustCaInOsStore { get; init; } = true;
    public ICaptureResponseSource? ResponseSource { get; init; }
    public ICaptureResponseTransform? ResponseTransform { get; init; }

    public bool FreshCa { get; private set; }
    public string? RootThumbprint => _rootCa?.Thumbprint;

    public event Action<CapturedFlow>? FlowCaptured;
    public event Action<int, string?>? ClientConnected;
    public event Action<int, string?>? ClientDisconnected;
    public event Action? AuxbrainConnect;
    public event Action<string>? DecryptError;
    public event Action? TrustRestored;
    public event Action<string, bool>? ConnectSeen;
    public event Action<string>? Trace;
    public bool Verbose { get; set; } = verbose;

    public Task StartAsync(int port, string caPath, CancellationToken ct) {
        string certDir = Path.GetDirectoryName(Path.GetFullPath(caPath))!;
        Directory.CreateDirectory(certDir);
        string caCacheDir = Path.Combine(certDir, ".ca");
        Directory.CreateDirectory(caCacheDir);
        string pfxPath = Path.Combine(caCacheDir, "root.pfx");

        FreshCa = !File.Exists(pfxPath);
        _rootCa = LoadOrCreateRoot(pfxPath);
        _rootCaPublic = X509CertificateLoader.LoadCertificate(_rootCa.Export(X509ContentType.Cert));
        File.WriteAllBytes(caPath, _rootCaPublic.Export(X509ContentType.Cert));
        if (TrustCaInOsStore) TrustRootCa(_rootCaPublic);

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _cts.Token;

        int bindPort = port + 1;
        _listener = new TcpListener(IPAddress.Loopback, bindPort);
        _listener.Start();
        _acceptLoop = AcceptLoopAsync(token);

        if (LanForwarderEnabled) {
            _forwarder = new LanForwarder(port, bindPort) {
                Trace = Log,
                DeviceConnected = (ip, n) => ClientConnected?.Invoke(n, ip),
                DeviceDisconnected = (ip, n) => ClientDisconnected?.Invoke(n, ip)
            };
            _forwarder.Start();
        }

        Log($"native proxy listening on loopback:{bindPort} (LAN {port}), freshCa={FreshCa}");
        return Task.CompletedTask;
    }

    public async Task StopAsync() {
        UntrustRootCa();
        lock (_trustGate) {
            _trustTimer?.Dispose();
            _trustTimer = null;
        }

        try {
            _cts?.Cancel();
        } catch (Exception ex) when (ex is ObjectDisposedException or AggregateException) {
            Log($"stop: cancel failed: {ex.Message}");
        }

        if (_forwarder is not null) {
            var f = _forwarder;
            _forwarder = null;
            try {
                await f.DisposeAsync();
            } catch (Exception ex) {
                Log($"stop: LAN forwarder dispose failed: {ex.Message}");
            }
        }

        try {
            _listener?.Stop();
        } catch (Exception ex) when (ex is SocketException or ObjectDisposedException) {
            Log($"stop: listener stop failed: {ex.Message}");
        }

        if (_acceptLoop is not null) {
            try {
                await _acceptLoop;
            } catch (Exception ex) {
                Log($"stop: accept loop faulted: {ex.Message}");
            }
        }
    }

    public async ValueTask DisposeAsync() {
        await StopAsync();
        _cts?.Dispose();
        foreach (var leaf in _leafCache.Values) leaf.Dispose();
        _rootCa?.Dispose();
        _rootCaPublic?.Dispose();
    }

    private void Log(string m) {
        if (Verbose) Trace?.Invoke(m);
    }

    private async Task AcceptLoopAsync(CancellationToken ct) {
        var listener = _listener!;
        while (!ct.IsCancellationRequested) {
            TcpClient client;
            try {
                client = await listener.AcceptTcpClientAsync(ct);
            } catch (OperationCanceledException) {
                break;
            } catch (ObjectDisposedException) {
                break;
            } catch (SocketException) {
                break;
            }

            _ = Task.Run(() => HandleConnectionAsync(client, ct), CancellationToken.None);
        }
    }

    private async Task HandleConnectionAsync(TcpClient client, CancellationToken ct) {
        using var _ = client;
        client.NoDelay = true;
        var net = client.GetStream();
        try {
            (string? head, byte[] leftover) = await ReadHeadAsync(net, ct);
            if (head is null) return;
            (string method, string target) = ParseRequestLine(head);
            if (!method.Equals("CONNECT", StringComparison.OrdinalIgnoreCase)) {
                if (Uri.TryCreate(target, UriKind.Absolute, out var plain)
                    && plain.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase)) {
                    await RelayPlainAsync(net, plain, head, leftover, ct);
                    return;
                }

                await WriteAsciiAsync(net, "HTTP/1.1 405 Method Not Allowed\r\nConnection: close\r\n\r\n", ct);
                return;
            }

            string host = AuxbrainHosts.NormalizeHost(target);
            int portNum = PortOf(target);
            ConnectSeen?.Invoke(target, AuxbrainHosts.IsAuxbrain(target));

            await WriteAsciiAsync(net, "HTTP/1.1 200 Connection Established\r\n\r\n", ct);

            if (AuxbrainHosts.IsAuxbrain(target)) {
                AuxbrainConnect?.Invoke();
                ArmTrustInference(host);
                await MitmAsync(net, host, portNum, ct);
            } else {
                await RawTunnelAsync(net, host, portNum, ct);
            }
        } catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException) {
            Log($"conn closed: {ex.GetType().Name}");
        } catch (Exception ex) {
            Log($"conn error: {ex.Message}");
        }
    }

    private async Task MitmAsync(NetworkStream deviceNet, string host, int port, CancellationToken ct) {
        var leaf = GetLeaf(host);
        await using var deviceTls = new SslStream(deviceNet, false);
        try {
            await deviceTls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions {
                ServerCertificate = leaf,
                ClientCertificateRequired = false,

                ApplicationProtocols = [SslApplicationProtocol.Http11],
                EnabledSslProtocols = SslProtocols.None
            }, ct);
        } catch (Exception ex) {
            string inner = ex.InnerException is { } ie ? $" | inner: {ie.GetType().Name}: {ie.Message}" : "";
            Log($"MITM handshake failed for {host}: {ex.Message}{inner} | leaf hasKey={leaf.HasPrivateKey}");
            return;
        }

        using var upstream = new TcpClient { NoDelay = true };
        try {
            await upstream.ConnectAsync(host, port, ct);
        } catch (Exception ex) {
            Log($"upstream connect {host}:{port} failed: {ex.Message}");
            return;
        }

        await using var upstreamTls = new SslStream(upstream.GetStream(), false);
        try {
            await upstreamTls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions {
                TargetHost = host,
                ApplicationProtocols = [SslApplicationProtocol.Http11]
            }, ct);
        } catch (Exception ex) {
            Log($"upstream TLS {host} failed: {ex.Message}");
            return;
        }

        while (!ct.IsCancellationRequested) {
            var req = await HttpMessage.ReadAsync(deviceTls, ct);
            if (req is null) break;

            var resp = await AnswerLocallyAsync(host, req, ct);
            HttpMessage toDevice;
            if (resp is null) {
                await req.WriteAsync(upstreamTls, ct);
                resp = await HttpMessage.ReadAsync(upstreamTls, ct);
                if (resp is null) break;
                toDevice = await TransformAsync(host, req, resp, ct) ?? resp;
            } else {
                toDevice = resp;
            }

            await toDevice.WriteAsync(deviceTls, ct);

            MarkTrustProven();
            EmitFlow(host, req, resp);

            if (req.IsConnectionClose || resp.IsConnectionClose) break;
        }
    }

    private async Task<HttpMessage?> TransformAsync(
        string host, HttpMessage req, HttpMessage resp, CancellationToken ct) {
        if (ResponseTransform is not { } transform) return null;

        byte[]? replaced;
        try {
            string reqText = req.Body is { Length: > 0 } ? Encoding.UTF8.GetString(req.Body) : "";
            string? contentType = resp.Headers
                .FirstOrDefault(h => h.Name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))?.Value;
            replaced = await transform.TransformAsync(
                new CaptureOverrideRequest(host, req.Method, req.Path, WireBody.ExtractDataParam(reqText), req.Body),
                new CaptureUpstreamResponse(resp.StatusCode, contentType, resp.Body ?? []),
                ct);
        } catch (Exception ex) {
            Log($"response transform failed for {host}{req.Path}, relaying upstream body: {ex.Message}");
            return null;
        }

        if (replaced is null) return null;

        Log($"rewrote {host}{req.Path} response, {resp.Body?.Length ?? 0} -> {replaced.Length} bytes");
        return new HttpMessage {
            StartLine = resp.StartLine,
            Headers = [.. resp.Headers.Where(h => !h.Name.Equals("Content-Encoding", StringComparison.OrdinalIgnoreCase))],
            Body = replaced
        };
    }

    private async Task<HttpMessage?> AnswerLocallyAsync(string host, HttpMessage req, CancellationToken ct) {
        if (ResponseSource is not { } source) return null;

        CaptureOverrideResponse? answer;
        try {
            string reqText = req.Body is { Length: > 0 } ? Encoding.UTF8.GetString(req.Body) : "";
            answer = await source.TryAnswerAsync(
                new CaptureOverrideRequest(host, req.Method, req.Path, WireBody.ExtractDataParam(reqText), req.Body),
                ct);
        } catch (Exception ex) {
            Log($"response source failed for {host}{req.Path}, forwarding upstream: {ex.Message}");
            return null;
        }

        if (answer is null) return null;

        Log($"answering {host}{req.Path} locally, {answer.Body.Length} bytes");
        return new HttpMessage {
            StartLine = $"HTTP/1.1 {answer.StatusCode} OK",
            Headers = [
                new HttpHeader("Content-Type", answer.ContentType),
                new HttpHeader("Content-Length", answer.Body.Length.ToString(CultureInfo.InvariantCulture))
            ],
            Body = answer.Body
        };
    }

    private void EmitFlow(string host, HttpMessage req, HttpMessage resp) {
        try {
            string reqText = req.Body is { Length: > 0 } ? Encoding.UTF8.GetString(req.Body) : "";
            string? data = WireBody.ExtractDataParam(reqText);
            (string responseB64, _) = WireBody.Normalize(resp.Body ?? []);
            string url = $"https://{host}{req.Path}";
            FlowCaptured?.Invoke(new CapturedFlow(
                url, req.Method, resp.StatusCode, data, responseB64,
                req.Headers, resp.Headers));
        } catch (Exception ex) {
            Log($"emit flow error: {ex.Message}");
        }
    }

    private static async Task RelayPlainAsync(
        NetworkStream deviceNet, Uri target, string head, byte[] leftover, CancellationToken ct) {
        using var upstream = new TcpClient { NoDelay = true };
        try {
            await upstream.ConnectAsync(target.Host, target.Port, ct);
        } catch {
            await WriteAsciiAsync(deviceNet, "HTTP/1.1 502 Bad Gateway\r\nConnection: close\r\n\r\n", ct);
            return;
        }

        var up = upstream.GetStream();
        await WriteAsciiAsync(up, head + "\r\n\r\n", ct);
        if (leftover.Length > 0) await up.WriteAsync(leftover, ct);
        var a = PumpAsync(deviceNet, up, ct);
        var b = PumpAsync(up, deviceNet, ct);
        await Task.WhenAll(a, b);
    }

    private static async Task RawTunnelAsync(NetworkStream deviceNet, string host, int port, CancellationToken ct) {
        using var upstream = new TcpClient { NoDelay = true };
        try {
            await upstream.ConnectAsync(host, port, ct);
        } catch {
            return;
        }

        var up = upstream.GetStream();
        var a = PumpAsync(deviceNet, up, ct);
        var b = PumpAsync(up, deviceNet, ct);
        await Task.WhenAll(a, b);
    }

    private static async Task PumpAsync(Stream from, Stream to, CancellationToken ct) {
        byte[] buf = ArrayPool<byte>.Shared.Rent(16 * 1024);
        try {
            int n;
            while ((n = await from.ReadAsync(buf, ct)) > 0)
                await to.WriteAsync(buf.AsMemory(0, n), ct);
        } catch {
            /* peer closed / cancel */
        } finally {
            ArrayPool<byte>.Shared.Return(buf);
        }
    }

    private static X509Certificate2 LoadOrCreateRoot(string pfxPath) {
        if (File.Exists(pfxPath)) {
            return X509CertificateLoader.LoadPkcs12(File.ReadAllBytes(pfxPath), null,
                X509KeyStorageFlags.Exportable);
        }

        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest($"CN={RootCaName}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign | X509KeyUsageFlags.DigitalSignature, true));
        req.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(req.PublicKey, false));
        var notBefore = DateTimeOffset.UtcNow.AddDays(-1);
        var notAfter = DateTimeOffset.UtcNow.AddYears(10);
        using var cert = req.CreateSelfSigned(notBefore, notAfter);
        byte[] pfx = cert.Export(X509ContentType.Pkcs12);
        File.WriteAllBytes(pfxPath, pfx);
        return X509CertificateLoader.LoadPkcs12(pfx, null, X509KeyStorageFlags.Exportable);
    }

    private X509Certificate2 GetLeaf(string host) => _leafCache.GetOrAdd(host, MintLeaf);

    internal static X509Certificate2 MintLeafForTest(string host, out X509Certificate2 root) {
        var p = new NativeCaptureProxy();
        string tmp = Path.Combine(Path.GetTempPath(), "egi-native-catest-" + Guid.NewGuid().ToString("N"));
        root = LoadOrCreateRoot(tmp);
        try {
            p._rootCa = root;
            return p.MintLeaf(host);
        } finally {
            try {
                File.Delete(tmp);
            } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
                p.Log($"temp root CA cleanup failed for {tmp}: {ex.Message}");
            }
        }
    }

    private X509Certificate2 MintLeaf(string host) {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest($"CN={host}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        req.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName(host);
        req.CertificateExtensions.Add(san.Build());
        req.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(req.PublicKey, false));

        var notBefore = DateTimeOffset.UtcNow.AddDays(-1);

        var notAfter = DateTimeOffset.UtcNow.AddDays(300);
        var rootExpiry = new DateTimeOffset(_rootCa!.NotAfter).AddMinutes(-5);
        if (notAfter > rootExpiry) notAfter = rootExpiry;
        if (notAfter <= notBefore) notBefore = notAfter.AddDays(-1);
        byte[] serial = new byte[8];
        RandomNumberGenerator.Fill(serial);
        using var signed = req.Create(_rootCa, notBefore, notAfter, serial);

        using var withKey = signed.CopyWithPrivateKey(rsa);
        return X509CertificateLoader.LoadPkcs12(
            withKey.Export(X509ContentType.Pkcs12), null, X509KeyStorageFlags.Exportable);
    }

    private void TrustRootCa(X509Certificate2 cert) {
        try {
            using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);
            foreach (var stale in store.Certificates.Find(X509FindType.FindBySubjectName, RootCaName, false)) {
                if (!stale.Thumbprint.Equals(cert.Thumbprint, StringComparison.OrdinalIgnoreCase)) {
                    try {
                        store.Remove(stale);
                    } catch (Exception ex) {
                        Log($"TRUST-ERR stale removal {stale.Thumbprint}: {ex.Message}");
                    }
                }

                stale.Dispose();
            }

            if (!store.Certificates.Contains(cert)) store.Add(cert);
            _trustAdded = true;
        } catch (Exception ex) {
            Log($"TRUST-ERR {ex.Message}");
        }
    }

    private void UntrustRootCa() {
        if (!_trustAdded || _rootCaPublic is null) return;
        _trustAdded = false;
        try {
            using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);
            store.Remove(_rootCaPublic);
        } catch (Exception ex) {
            Log($"TRUST-ERR {ex.Message}");
        }
    }

    private void ArmTrustInference(string host) {
        lock (_trustGate) {
            if (_trustProven || _untrustedReported) return;
            _trustTimer?.Dispose();
            _trustTimer = new Timer(_ => OnTrustGraceElapsed(host), null, TrustGrace, Timeout.InfiniteTimeSpan);
        }
    }

    private void MarkTrustProven() {
        bool wasReported;
        lock (_trustGate) {
            if (_trustProven) return;
            _trustProven = true;
            wasReported = _untrustedReported;
            _untrustedReported = false;
            _trustTimer?.Dispose();
            _trustTimer = null;
        }

        if (wasReported) TrustRestored?.Invoke();
    }

    private void OnTrustGraceElapsed(string host) {
        lock (_trustGate) {
            if (_trustProven || _untrustedReported) return;
            _untrustedReported = true;
            _trustTimer?.Dispose();
            _trustTimer = null;
        }

        Log($"TRUST infer: no decrypted traffic from {host} within grace - CA likely untrusted");
        DecryptError?.Invoke(
            $"No decrypted traffic after connecting to {host} - is the CA installed and trusted on the device?");
    }

    private static async Task<(string? Head, byte[] Leftover)> ReadHeadAsync(NetworkStream net, CancellationToken ct) {
        byte[] buf = new byte[8192];
        int len = 0, end = -1;
        while (end < 0) {
            if (len == buf.Length) return (null, []);
            int n = await net.ReadAsync(buf.AsMemory(len), ct);
            if (n == 0) return (len > 0 ? Encoding.ASCII.GetString(buf, 0, len) : null, []);
            len += n;
            for (int i = 0; i + 3 < len; i++) {
                if (buf[i] == 13 && buf[i + 1] == 10 && buf[i + 2] == 13 && buf[i + 3] == 10) {
                    end = i;
                    break;
                }
            }
        }

        int bodyStart = end + 4;
        return (Encoding.ASCII.GetString(buf, 0, end), bodyStart < len ? buf[bodyStart..len] : []);
    }

    private static (string Method, string Target) ParseRequestLine(string head) {
        string firstLine = head.Split("\r\n")[0];
        string[] parts = firstLine.Split(' ');
        return (parts.Length > 0 ? parts[0] : "", parts.Length > 1 ? parts[1] : "");
    }

    private static int PortOf(string authority) {
        int colon = authority.LastIndexOf(':');
        return colon > 0 && colon < authority.Length - 1 && int.TryParse(authority[(colon + 1)..], NumberStyles.Integer,
            CultureInfo.InvariantCulture, out int p)
            ? p
            : 443;
    }

    private static Task WriteAsciiAsync(Stream s, string text, CancellationToken ct) =>
        s.WriteAsync(Encoding.ASCII.GetBytes(text), ct).AsTask();
}
