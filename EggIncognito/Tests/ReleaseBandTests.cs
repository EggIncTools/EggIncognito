using EggIncognito.Models.Registry;
using EggIncognito.Services.Protos;

namespace EggIncognito.Tests;

public class ReleaseBandTests {
    private static long _nextId;

    private static Release Rel(string app, string build, string? client, string? sha, string platform = "android") {
        long id = ++_nextId;
        var row = new ProtoRegistryRow(id, null, platform, app, build, client, null, null, sha, null, null, null);
        return new Release(id, [row]);
    }

    [Fact]
    public void ConsecutiveReleasesWithSameShaAndClientFormOneBand() {
        var ordered = new[] {
            Rel("1.37.2", "111359", "75", "fd91c3"),
            Rel("1.37.1", "1.37.1.1", "75", "fd91c3", "ios"),
            Rel("1.37", "111358", "75", "fd91c3"),
            Rel("1.37", "111357", "75", "25c995"),
        };

        var bands = ReleaseBand.Of(ordered);

        Assert.Equal(2, bands.Count);
        Assert.Equal(3, bands[0].Releases.Count);
        Assert.Equal(ordered[0].Key, bands[0].Key);
        Assert.Single(bands[1].Releases);
    }

    [Fact]
    public void SameShaWithDifferentClientStaysSeparate() {
        var bands = ReleaseBand.Of([Rel("1.36", "111350", "72", "aa"), Rel("1.35", "111344", "71", "aa")]);
        Assert.Equal(2, bands.Count);
    }

    [Fact]
    public void NonAdjacentMatchesDoNotMerge() {
        var bands = ReleaseBand.Of([Rel("1.36", "1", "72", "aa"), Rel("1.35", "2", "72", "bb"), Rel("1.34", "3", "72", "aa")]);
        Assert.Equal(3, bands.Count);
    }

    [Fact]
    public void MissingShaNeverBands() {
        var bands = ReleaseBand.Of([Rel("1.36", "1", "72", null), Rel("1.35", "2", "72", null)]);
        Assert.Equal(2, bands.Count);
    }
}
