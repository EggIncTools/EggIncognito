using EggIncognito.Core.Services;
using Microsoft.Extensions.Time.Testing;

namespace EggIncognito.Tests;

public sealed class RouteOverrideCacheTests {
    private static RouteOverrideInfo Info(string path) =>
        new(path, "ReqType", "RespType", true, false, null, DateTimeOffset.UnixEpoch, null);

    private static Dictionary<string, RouteOverrideInfo> Dict(params RouteOverrideInfo[] infos) =>
        infos.ToDictionary(i => i.Path, StringComparer.Ordinal);

    [Fact]
    public void Snapshot_BeforeTtlElapses_DoesNotRefetch() {
        var time = new FakeTimeProvider(Start);
        int calls = 0;
        var provider = new CachedRouteOverrideProvider(() => {
            calls++;
            return Dict(Info("a"));
        }, TimeSpan.FromSeconds(10), time);

        provider.Snapshot();
        provider.Snapshot();
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Invalidate_ForcesRefetch_EvenWithinTtl() {
        var time = new FakeTimeProvider(Start);
        int calls = 0;
        var provider = new CachedRouteOverrideProvider(() => {
            calls++;
            return Dict(Info("a"));
        }, TimeSpan.FromSeconds(10), time);

        provider.Snapshot();
        provider.Invalidate();
        provider.Snapshot();
        Assert.Equal(2, calls);
    }

    [Fact]
    public void Snapshot_FirstFetchThrows_YieldsEmptyDict() {
        var time = new FakeTimeProvider(Start);
        var provider = new CachedRouteOverrideProvider(
            () => throw new InvalidOperationException("db down"),
            TimeSpan.FromSeconds(10), time);

        var snapshot = provider.Snapshot();
        Assert.Empty(snapshot);
    }

    [Fact]
    public void Snapshot_KeysByInfoPath_NotFetchDictKey() {
        var time = new FakeTimeProvider(Start);
        var mismatched = new Dictionary<string, RouteOverrideInfo> {
            ["wrong-key"] = Info("actual/path")
        };
        var provider = new CachedRouteOverrideProvider(() => mismatched, TimeSpan.FromSeconds(10), time);

        var snapshot = provider.Snapshot();
        Assert.True(snapshot.ContainsKey("actual/path"));
        Assert.False(snapshot.ContainsKey("wrong-key"));
    }

    private static readonly DateTimeOffset Start = new(2026, 8, 4, 0, 0, 0, TimeSpan.Zero);
}
