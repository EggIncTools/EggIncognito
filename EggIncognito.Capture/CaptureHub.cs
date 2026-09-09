using System.Net;
using System.Text.Json;
using System.Threading.Channels;

namespace EggIncognito.Capture;

public sealed class CaptureHub {
    private const int BufferCap = 500;
    private const int SubscriberQueueCap = 256;
    private const string KindFlow = "flow";
    private const string KindStats = "stats";
    private const string KindNotice = "notice";
    private readonly LinkedList<DashboardFlow> _buffer = new();
    private readonly Dictionary<string, long> _bytesByEndpoint = [with(StringComparer.Ordinal)];
    private readonly Dictionary<string, Device> _devices = [with(StringComparer.Ordinal)];
    private readonly HashSet<string> _endpoints = [with(StringComparer.Ordinal)];

    private readonly Lock _gate = new();

    private readonly Dictionary<string, RememberedDevice> _known = [with(StringComparer.Ordinal)];
    private readonly List<Channel<CaptureEnvelope>> _subscribers = [];

    private int _activeConnections;
    private long _bytesCaptured;
    private int _capturedAuxbrain;
    private CertState _certState = CertState.Waiting;
    private int _decryptErrors;
    private int _decryptOk;
    private string? _lastError;
    private string? _lastGameVersion;
    private CaptureStats? _mirrored;

    private string? _lastOs;
    private long _nextId;
    private int _passthrough;
    private int _proxyPort;

    private bool _proxyRunning;
    private bool _sawAuxbrainConnect;

    public Action? DevicesChanged { get; set; }

    public event Action? StatsChanged;

    public bool Paused { get; set; }

    public bool HasSubscribers {
        get {
            lock (_gate) return _subscribers.Count > 0;
        }
    }

    public DashboardFlow? Publish(DashboardFlow flow, string timestamp, bool isAuxbrain = true) {
        DashboardFlow? stored = null;
        Channel<CaptureEnvelope>[] targets;
        CaptureEvent? trustNotice = null;

        lock (_gate) {
            if (isAuxbrain) {
                _capturedAuxbrain++;
                _decryptOk++;
                if (!string.IsNullOrEmpty(flow.Path)) _endpoints.Add(flow.Path);
                (string? os, string? gameVersion) = ParseRInfo(flow.RequestJson);
                if (os is not null) _lastOs = os;
                if (gameVersion is not null) _lastGameVersion = gameVersion;

                if (_devices.Count == 1 && (os is not null || gameVersion is not null)) {
                    var only = _devices.Values.First();
                    only.Os = os ?? only.Os;
                    only.GameVersion = gameVersion ?? only.GameVersion;
                }

                long bytes = ApproxBytes(flow);
                _bytesCaptured += bytes;
                if (!string.IsNullOrEmpty(flow.Path))
                    _bytesByEndpoint[flow.Path] = _bytesByEndpoint.GetValueOrDefault(flow.Path) + bytes;

                if (_certState != CertState.Trusted) {
                    _certState = CertState.Trusted;
                    trustNotice = new CaptureEvent("certTrusted", "Cert trusted - capturing!", timestamp);
                }
            } else {
                _passthrough++;
            }

            if (!Paused && isAuxbrain) {
                stored = flow with { Id = ++_nextId, Timestamp = timestamp };
                _buffer.AddLast(stored);
                while (_buffer.Count > BufferCap) _buffer.RemoveFirst();
            }

            targets = [.. _subscribers];
        }

        foreach (var ch in targets) {
            if (stored is not null) ch.Writer.TryWrite(new CaptureEnvelope(KindFlow, stored, null, null));
            if (trustNotice is not null) ch.Writer.TryWrite(new CaptureEnvelope(KindNotice, null, null, trustNotice));
        }

        if (isAuxbrain || trustNotice is not null) BroadcastStats();

        return stored;
    }

    public void RecordConnection(int activeCount, string? ip, string timestamp) {
        CaptureEvent? notice = null;
        bool needRdns = false;
        lock (_gate) {
            _activeConnections = activeCount;
            if (!string.IsNullOrEmpty(ip)) {
                if (!_devices.TryGetValue(ip, out var dev)) {
                    _known.TryGetValue(ip, out var prior);
                    dev = new Device(ip, prior?.FirstSeen ?? timestamp) {
                        Hostname = prior?.Hostname,
                        Os = prior?.Os,
                        GameVersion = prior?.GameVersion,
                        TotalConnections = prior?.TotalConnections ?? 0
                    };
                    _devices[ip] = dev;
                    notice = new CaptureEvent("deviceConnected", $"Device connected {ip}", timestamp);
                    needRdns = prior?.Hostname is null;
                }

                dev.Online = true;
                dev.Connections++;
                dev.TotalConnections++;
                dev.LastSeen = timestamp;
            }
        }

        if (notice is not null) Broadcast(new CaptureEnvelope(KindNotice, null, null, notice));
        if (needRdns && ip is not null) _ = ResolveHostnameAsync(ip);
        DevicesChanged?.Invoke();
        BroadcastStats();
    }

    public void RecordDisconnection(int activeCount, string timestamp) {
        lock (_gate) {
            _activeConnections = activeCount;

            if (activeCount == 0) {
                foreach (var d in _devices.Values)
                    d.Online = false;
            }
        }

        Broadcast(new CaptureEnvelope(KindNotice, null, null,
            new CaptureEvent("deviceDisconnected", "Device disconnected", timestamp)));
        DevicesChanged?.Invoke();
        BroadcastStats();
    }

    private async Task ResolveHostnameAsync(string ip) {
        string? host = null;
        try {
            var entry = await Dns.GetHostEntryAsync(ip);
            if (!string.IsNullOrEmpty(entry.HostName) && entry.HostName != ip) host = entry.HostName;
        } catch {
            /* no PTR record or lookup failed */
        }

        if (host is null) return;
        lock (_gate) {
            if (_devices.TryGetValue(ip, out var dev))
                dev.Hostname = host;
        }

        DevicesChanged?.Invoke();
        BroadcastStats();
    }

    public void RecordAuxbrainConnect() {
        lock (_gate) _sawAuxbrainConnect = true;
        BroadcastStats();
    }

    public void RecordDecryptError(string message, string timestamp) {
        lock (_gate) {
            _decryptErrors++;
            _lastError = message;
            if (_certState != CertState.Trusted && (_sawAuxbrainConnect || _activeConnections > 0))
                _certState = CertState.Untrusted;
        }

        Broadcast(new CaptureEnvelope(KindNotice, null, null,
            new CaptureEvent("decryptError", "Decrypt error: " + message, timestamp)));
        BroadcastStats();
    }

    public void RecordTrustRestored(string timestamp) {
        bool changed;
        lock (_gate) {
            changed = _lastError is not null || _certState == CertState.Untrusted;
            _lastError = null;
            if (_certState == CertState.Untrusted) _certState = CertState.Trusted;
        }

        if (!changed) return;
        Broadcast(new CaptureEnvelope(KindNotice, null, null,
            new CaptureEvent("certTrusted", "Decryption recovered - CA is trusted", timestamp)));
        BroadcastStats();
    }

    public IReadOnlyList<DashboardFlow> Snapshot() {
        lock (_gate) return _buffer.ToArray();
    }

    public CaptureStats StatsSnapshot() {
        lock (_gate) return _mirrored ?? BuildStats();
    }

    public void Mirror(CaptureEnvelope env) {
        lock (_gate) {
            if (env.Flow is { } flow) Upsert(flow);
            if (env.Stats is { } stats) _mirrored = stats;
        }

        Broadcast(env);
        if (env.Stats is not null) StatsChanged?.Invoke();
    }

    private void Upsert(DashboardFlow flow) {
        for (var node = _buffer.First; node is not null; node = node.Next) {
            if (node.Value.Id != flow.Id) continue;
            node.Value = flow;
            return;
        }

        _buffer.AddLast(flow);
        while (_buffer.Count > BufferCap) _buffer.RemoveFirst();
    }

    private CaptureStats BuildStats() {
        string? biggest = null;
        long biggestBytes = 0;
        foreach ((string k, long v) in _bytesByEndpoint) {
            if (v > biggestBytes) {
                biggest = k;
                biggestBytes = v;
            }
        }

        bool single = _devices.Count == 1;
        var live = _devices.Values
            .OrderBy(d => d.FirstSeen, StringComparer.Ordinal)
            .Select(d => new DeviceInfo(
                d.Ip, d.Hostname, d.Connections, d.FirstSeen, d.LastSeen,
                d.Os ?? (single ? _lastOs : null),
                d.GameVersion ?? (single ? _lastGameVersion : null),
                d.Online,
                d.TotalConnections));

        var offline = _known.Values
            .Where(k => !_devices.ContainsKey(k.Ip))
            .Select(k => new DeviceInfo(
                k.Ip, k.Hostname, 0, k.FirstSeen, k.LastSeen, k.Os, k.GameVersion,
                false, k.TotalConnections));
        var devices = live.Concat(offline).ToArray();

        return new CaptureStats(
            _activeConnections,
            _devices.Count,
            devices,
            _capturedAuxbrain,
            _passthrough,
            _endpoints.Count,
            _decryptOk,
            _decryptErrors,
            _lastError,
            _bytesCaptured,
            biggest,
            biggestBytes,
            _certState.ToString(),
            _proxyRunning,
            _proxyPort);
    }

    private static (string? Os, string? GameVersion) ParseRInfo(string? requestJson) {
        if (string.IsNullOrEmpty(requestJson)) return (null, null);
        try {
            using var doc = JsonDocument.Parse(requestJson);
            if (!doc.RootElement.TryGetProperty("rinfo", out var rinfo)) return (null, null);

            string? os = null;
            if (rinfo.TryGetProperty("platform", out var p) && p.ValueKind == JsonValueKind.String)
                os = OsLabel(p.GetString());

            string? version = null;
            if (rinfo.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.String)
                version = v.GetString();

            return (os, version);
        } catch (JsonException) {
            return (null, null);
        }
    }

    private static string OsLabel(string? platform) => platform?.ToUpperInvariant() switch {
        "IOS" => "iOS",
        "DROID" or "ANDROID" => "Android",
        null or "" => "Unknown OS",
        _ => platform
    };

    public void SeedKnownDevices(IReadOnlyList<RememberedDevice> devices) {
        lock (_gate) {
            _known.Clear();
            foreach (var d in devices) _known[d.Ip] = d;
        }
    }

    public IReadOnlyList<RememberedDevice> SnapshotRememberedDevices() {
        lock (_gate) {
            bool single = _devices.Count == 1;
            var merged = new Dictionary<string, RememberedDevice>(_known, StringComparer.Ordinal);
            foreach (var d in _devices.Values) {
                merged[d.Ip] = new RememberedDevice(
                    d.Ip, d.Hostname,
                    d.Os ?? (single ? _lastOs : merged.GetValueOrDefault(d.Ip)?.Os),
                    d.GameVersion ?? (single ? _lastGameVersion : merged.GetValueOrDefault(d.Ip)?.GameVersion),
                    d.FirstSeen, d.LastSeen, d.TotalConnections);
            }

            return merged.Values.ToList();
        }
    }

    public void Clear() {
        lock (_gate) _buffer.Clear();
    }

    public DashboardFlow? Find(long id) {
        lock (_gate) {
            foreach (var f in _buffer) {
                if (f.Id == id)
                    return f;
            }

            return null;
        }
    }

    public void MarkSaved(long id) {
        DashboardFlow? updated = null;
        lock (_gate) {
            var node = _buffer.First;
            while (node is not null) {
                if (node.Value.Id == id) {
                    node.Value = node.Value with { Saved = true };
                    updated = node.Value;
                    break;
                }

                node = node.Next;
            }
        }

        if (updated is not null) Broadcast(new CaptureEnvelope(KindFlow, updated, null, null));
    }

    public void SetProxyState(bool running, int port) {
        lock (_gate) {
            _proxyRunning = running;
            _proxyPort = port;
        }

        BroadcastStats();
    }

    public void PostNotice(CaptureEvent notice) =>
        Broadcast(new CaptureEnvelope(KindNotice, null, null, notice));

    public (ChannelReader<CaptureEnvelope> Reader, IDisposable Subscription) Subscribe() {
        var ch = Channel.CreateBounded<CaptureEnvelope>(new BoundedChannelOptions(SubscriberQueueCap) {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
        lock (_gate) _subscribers.Add(ch);
        return (ch.Reader, new Subscription(this, ch));
    }

    private void Broadcast(CaptureEnvelope env) {
        Channel<CaptureEnvelope>[] targets;
        lock (_gate) targets = [.. _subscribers];
        foreach (var ch in targets) ch.Writer.TryWrite(env);
    }

    private void BroadcastStats() {
        CaptureEnvelope env;
        Channel<CaptureEnvelope>[] targets;
        lock (_gate) {
            env = new CaptureEnvelope(KindStats, null, BuildStats(), null);
            targets = [.. _subscribers];
        }

        foreach (var ch in targets) ch.Writer.TryWrite(env);
        StatsChanged?.Invoke();
    }

    private void Unsubscribe(Channel<CaptureEnvelope> ch) {
        lock (_gate) _subscribers.Remove(ch);
        ch.Writer.TryComplete();
    }

    private static long ApproxBytes(DashboardFlow flow) =>
        (flow.ResponseB64?.Length ?? 0) + (flow.RequestDataB64?.Length ?? 0);

    private sealed class Device(string ip, string firstSeen) {
        public string Ip { get; } = ip;
        public string FirstSeen { get; } = firstSeen;
        public string LastSeen { get; set; } = firstSeen;
        public string? Hostname { get; set; }
        public int Connections { get; set; }
        public int TotalConnections { get; set; }
        public bool Online { get; set; } = true;
        public string? Os { get; set; }
        public string? GameVersion { get; set; }
    }

    private sealed class Subscription(CaptureHub hub, Channel<CaptureEnvelope> ch) : IDisposable {
        private bool _disposed;

        public void Dispose() {
            if (_disposed) return;
            _disposed = true;
            hub.Unsubscribe(ch);
        }
    }
}
