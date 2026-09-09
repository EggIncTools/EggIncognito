using EggIncognito.Capture;

namespace EggIncognito.Tests;

public sealed class CaptureSessionManagerTests : IDisposable {
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    internal static string RealContentRoot() {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null) {
            string candidate = Path.Combine(dir.FullName, "EggIncognito", "RouteMap", "routes.yaml");
            if (File.Exists(candidate)) return Path.Combine(dir.FullName, "EggIncognito");
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate the EggIncognito project content root.");
    }

    internal static CaptureSession NewSession(TempDir tmp, int port, FakeCaptureProxy? fake = null,
        CaptureTier tier = CaptureTier.Full) {
        string dir = tmp.CreateSubdir();
        var opts = new CaptureSessionOptions(port, null, null,
            false, false, dir, Path.Combine(dir, "ca.cer"),
            false) { Tier = tier };
        return new CaptureSession(RealContentRoot(), opts, _ => fake ?? new FakeCaptureProxy());
    }

    private CaptureSessionManager NewManager(int maxSessions = 10, int poolBase = 24000) =>
        new(HostedCaptureOptions.Defaults() with { MaxConcurrentSessions = maxSessions, PortPoolBase = poolBase },
            (key, basePort, _) => NewSession(_tmp, key == CaptureSessionManager.LocalKey ? 18080 : basePort));

    [Fact]
    public void TwoKeys_GetDistinctSessions_WithDistinctPorts() {
        var m = NewManager();
        var a = m.GetOrCreate("user-a");
        var b = m.GetOrCreate("user-b");
        Assert.NotSame(a, b);
        Assert.NotEqual(a.Port, b.Port);
        Assert.Equal(24000, a.Port);
        Assert.Equal(24003, b.Port);
    }

    [Fact]
    public void SameKey_ReturnsSameSession() {
        var m = NewManager();
        Assert.Same(m.GetOrCreate("user-a"), m.GetOrCreate("user-a"));
    }

    [Fact]
    public void Remove_FreesThePortForReuse() {
        var m = NewManager();
        var a = m.GetOrCreate("user-a");
        m.GetOrCreate("user-b");
        m.Remove("user-a");
        Assert.Null(m.Get("user-a"));
        var c = m.GetOrCreate("user-c");
        Assert.Equal(a.Port, c.Port);
    }

    [Fact]
    public void CapacityCap_Throws() {
        var m = NewManager(2);
        m.GetOrCreate("user-a");
        m.GetOrCreate("user-b");
        Assert.Throws<CaptureCapacityException>(() => m.GetOrCreate("user-c"));
    }

    [Fact]
    public void LocalKey_IsExemptFromCap() {
        var m = NewManager(1);
        m.GetOrCreate("user-a");
        var local = m.GetOrCreate(CaptureSessionManager.LocalKey);
        Assert.NotNull(local);
        Assert.Equal(18080, local.Port);
        Assert.Throws<CaptureCapacityException>(() => m.GetOrCreate("user-b"));
    }
}
