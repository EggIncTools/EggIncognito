using EggIncognito.Capture;
using EggIncognito.Core.Services;

namespace EggIncognito.Tests;

public sealed class LiveVersionStoreTests : IDisposable {
    private readonly TempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    private string NewDir() => _tmp.CreateSubdir();

    [Fact]
    public void Load_MissingFile_ReturnsEmpty() => Assert.Empty(new LiveVersionStore(NewDir()).Load());

    [Fact]
    public void Observe_ThenLatest_RoundTrips() {
        string dir = NewDir();
        var store = new LiveVersionStore(dir);
        store.Observe(new RinfoHarvester.ObservedVersion("IOS", "1.35.6", "111341", 72), "2026-06-16T10:00:00Z");

        var v = new LiveVersionStore(dir).Latest("ios");
        Assert.NotNull(v);
        Assert.Equal("ios", v.Platform);
        Assert.Equal("1.35.6", v.Version);
        Assert.Equal("111341", v.Build);
        Assert.Equal(72, v.ClientVersion);
    }

    [Fact]
    public void Observe_LaterThinObservation_KeepsPriorFields() {
        string dir = NewDir();
        var store = new LiveVersionStore(dir);
        store.Observe(new RinfoHarvester.ObservedVersion("IOS", "1.35.6", "111341", 72), "t1");

        store.Observe(new RinfoHarvester.ObservedVersion("IOS", null, null, 72), "t2");

        var v = store.Latest("ios");
        Assert.Equal("1.35.6", v!.Version);
        Assert.Equal("111341", v.Build);
        Assert.Equal(72, v.ClientVersion);
        Assert.Equal("t2", v.LastSeen);
    }

    [Fact]
    public void Observe_NewerClientVersion_Wins() {
        string dir = NewDir();
        var store = new LiveVersionStore(dir);
        store.Observe(new RinfoHarvester.ObservedVersion("IOS", "1.35.6", "111341", 72), "t1");
        store.Observe(new RinfoHarvester.ObservedVersion("IOS", "1.35.7", "111343", 73), "t2");

        var v = store.Latest("ios");
        Assert.Equal(73, v!.ClientVersion);
        Assert.Equal("1.35.7", v.Version);
        Assert.Equal("111343", v.Build);
    }

    [Fact]
    public void Observe_SeparatePlatforms_KeptIndependently() {
        string dir = NewDir();
        var store = new LiveVersionStore(dir);
        store.Observe(new RinfoHarvester.ObservedVersion("IOS", "1.35.6", "111341", 72), "t1");
        store.Observe(new RinfoHarvester.ObservedVersion("ANDROID", "1.35.5", "111334", 71), "t1");

        Assert.Equal(72, store.Latest("ios")!.ClientVersion);
        Assert.Equal(71, store.Latest("android")!.ClientVersion);
    }

    [Fact]
    public void Observe_NoPlatform_Ignored() {
        string dir = NewDir();
        var store = new LiveVersionStore(dir);
        store.Observe(new RinfoHarvester.ObservedVersion("", null, null, 72), "t1");
        Assert.Empty(store.Load());
    }
}
