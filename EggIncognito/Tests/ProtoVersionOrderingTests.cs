using EggIncognito.Core.Services.ProtoExtract;

namespace EggIncognito.Tests;

public class ProtoVersionOrderingTests {
    private sealed record Row(
        string Platform, string AppVersion, string Build, string? Client = null,
        int? Order = null, DateTime? Seen = null, long? Release = null, string? Sha = null);

    private static VersionKey Key(Row r) => new(r.Platform, r.AppVersion, r.Build, r.Client, r.Order, r.Seen, r.Release, r.Sha);

    [Fact]
    public void SortIsNewestFirstByDottedVersion() {
        var rows = new[] {
            new Row("ios", "1.36.0", "1.36.0.2"),
            new Row("ios", "1.37.0", "1.37.0.1"),
            new Row("ios", "1.11.0", "1.11.0.3"),
        };
        var sorted = ProtoVersionOrdering.Sort(rows, Key);
        Assert.Equal(new[] { "1.37.0", "1.36.0", "1.11.0" }, sorted.Select(r => r.AppVersion).ToArray());
    }

    [Fact]
    public void ExplicitSortOrderBreaksVersionTies() {
        var rows = new[] {
            new Row("ios", "1.37.0", "b1", Order: 1),
            new Row("ios", "1.37.0", "b2", Order: 9),
        };
        var sorted = ProtoVersionOrdering.Sort(rows, Key);
        Assert.Equal("b2", sorted[0].Build);
    }

    [Fact]
    public void DetectedAtBreaksRemainingTies() {
        var rows = new[] {
            new Row("ios", "1.37.0", "b1", Seen: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            new Row("ios", "1.37.0", "b2", Seen: new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc)),
        };
        Assert.Equal("b2", ProtoVersionOrdering.Sort(rows, Key)[0].Build);
    }

    [Fact]
    public void PreviousReturnsTheNextOlderSamePlatformEntry() {
        var rows = new[] {
            new Row("ios", "1.37.0", "1.37.0.1"),
            new Row("ios", "1.36.0", "1.36.0.2"),
            new Row("android", "1.365", "365"),
        };
        var prev = ProtoVersionOrdering.Previous(rows[0], rows, Key);
        Assert.Equal("1.36.0", prev!.AppVersion);
    }

    [Fact]
    public void NextReturnsTheNextNewerEntry() {
        var rows = new[] {
            new Row("ios", "1.37.0", "1.37.0.1"),
            new Row("ios", "1.36.0", "1.36.0.2"),
        };
        Assert.Equal("1.37.0", ProtoVersionOrdering.Next(rows[1], rows, Key)!.AppVersion);
    }

    [Fact]
    public void NewestHasNoNextAndOldestHasNoPrevious() {
        var rows = new[] {
            new Row("ios", "1.37.0", "1.37.0.1"),
            new Row("ios", "1.36.0", "1.36.0.2"),
        };
        Assert.Null(ProtoVersionOrdering.Next(rows[0], rows, Key));
        Assert.Null(ProtoVersionOrdering.Previous(rows[1], rows, Key));
    }

    [Fact]
    public void EmptyAndSingleCollectionsAreSafe() {
        var rows = new[] { new Row("ios", "1.37.0", "1.37.0.1") };
        Assert.Empty(ProtoVersionOrdering.Sort(Array.Empty<Row>(), Key));
        Assert.Null(ProtoVersionOrdering.Previous(rows[0], rows, Key));
        Assert.Null(ProtoVersionOrdering.Latest(Array.Empty<Row>(), Key));
        Assert.Empty(ProtoVersionOrdering.LatestByPlatform(Array.Empty<Row>(), Key));
    }

    [Fact]
    public void AMissingValueSkipsItsTierInsteadOfSinkingTheRow() {
        var rows = new[] {
            new Row("ios", "", "1.38.0.1", Client: "80"),
            new Row("ios", "1.36.0", "1.36.0.2", Client: "70"),
        };
        Assert.Equal("1.38.0.1", ProtoVersionOrdering.Sort(rows, Key)[0].Build);
    }

    [Fact]
    public void CompareReleaseIgnoresBuild() {
        var ios = Key(new Row("ios", "1.37.0", "1.37.0.1", Client: "75"));
        var android = Key(new Row("android", "1.37.0", "111358", Client: "75"));
        Assert.Equal(0, ProtoVersionOrdering.CompareRelease(ios, android));
    }

    [Fact]
    public void CompareReleaseRanksTheNewerAppVersionFirst() {
        var ios = Key(new Row("ios", "1.36.0", "1.36.0.2", Client: "74"));
        var android = Key(new Row("android", "1.37.0", "111358", Client: "75"));
        Assert.True(ProtoVersionOrdering.CompareRelease(android, ios) < 0);
    }

    [Fact]
    public void CompareReleaseFallsBackToClientVersionWhenAppVersionIsMissing() {
        var ios = Key(new Row("ios", "", "1.36.0.2", Client: "74"));
        var android = Key(new Row("android", "1.37.0", "111358", Client: "75"));
        Assert.True(ProtoVersionOrdering.CompareRelease(android, ios) < 0);
    }

    [Fact]
    public void LatestForPicksTheNewestOnThatPlatformOnly() {
        var rows = new[] {
            new Row("ios", "1.36.0", "1.36.0.2"),
            new Row("ios", "1.37.0", "1.37.0.1"),
            new Row("android", "1.38.0", "111400"),
        };
        Assert.Equal("1.37.0.1", ProtoVersionOrdering.LatestFor("ios", rows, Key)!.Build);
        Assert.Equal("111400", ProtoVersionOrdering.LatestFor("android", rows, Key)!.Build);
        Assert.Null(ProtoVersionOrdering.LatestFor("web", rows, Key));
    }

    [Fact]
    public void LatestByPlatformReturnsOneRowPerPlatformIosFirst() {
        var rows = new[] {
            new Row("android", "1.37.0", "111358", Client: "75"),
            new Row("android", "1.36.0", "111341", Client: "74"),
            new Row("ios", "1.37.0", "1.37.0.1", Client: "75"),
        };
        var latest = ProtoVersionOrdering.LatestByPlatform(rows, Key);
        Assert.Equal(new[] { "ios", "android" }, latest.Select(r => r.Platform).ToArray());
        Assert.Equal(new[] { "1.37.0.1", "111358" }, latest.Select(r => r.Build).ToArray());
    }

    [Fact]
    public void LatestByPlatformOrderIsFixedNotWhicheverIsAhead() {
        var rows = new[] {
            new Row("ios", "1.36.0", "1.36.0.2"),
            new Row("android", "1.38.0", "111400"),
        };
        Assert.Equal("ios", ProtoVersionOrdering.LatestByPlatform(rows, Key)[0].Platform);
    }

    [Fact]
    public void ADottedIosBuildNeverLosesToAnAndroidVersionCode() {
        var rows = new[] {
            new Row("android", "1.36.0", "111341", Client: "74"),
            new Row("ios", "1.37.0", "1.37.0.1", Client: "75"),
        };
        Assert.Equal("ios", ProtoVersionOrdering.Latest(rows, Key)!.Platform);
    }

    [Fact]
    public void LatestPicksWhicheverPlatformShippedTheNewerRelease() {
        var rows = new[] {
            new Row("ios", "1.36.0", "1.36.0.2", Client: "74"),
            new Row("android", "1.38.0", "111400", Client: "78"),
        };
        Assert.Equal("android", ProtoVersionOrdering.Latest(rows, Key)!.Platform);
    }

    [Fact]
    public void SortGroupsPlatformsInFixedOrder() {
        var rows = new[] {
            new Row("android", "1.36.0", "111341"),
            new Row("ios", "1.35.0", "1.35.0.1"),
            new Row("ios", "1.37.0", "1.37.0.1"),
        };
        var sorted = ProtoVersionOrdering.Sort(rows, Key);
        Assert.Equal(new[] { "ios", "ios", "android" }, sorted.Select(r => r.Platform).ToArray());
        Assert.Equal("1.37.0.1", sorted[0].Build);
    }

    [Fact]
    public void SortByReleasePutsBothPlatformsOfOneReleaseAdjacentWithIosFirst() {
        var rows = new[] {
            new Row("android", "1.36.0", "111341", Client: "74"),
            new Row("ios", "1.37.0", "1.37.0.1", Client: "75"),
            new Row("android", "1.37.0", "111358", Client: "75"),
            new Row("ios", "1.36.0", "1.36.0.2", Client: "74"),
        };
        var sorted = ProtoVersionOrdering.SortByRelease(rows, Key);
        Assert.Equal(new[] { "ios", "android", "ios", "android" }, sorted.Select(r => r.Platform).ToArray());
        Assert.Equal(new[] { "1.37.0", "1.37.0", "1.36.0", "1.36.0" }, sorted.Select(r => r.AppVersion).ToArray());
    }

    [Fact]
    public void SortByReleaseRanksTheNewerReleaseFirstWhicheverPlatformShippedIt() {
        var rows = new[] {
            new Row("ios", "1.36.0", "1.36.0.2", Client: "74"),
            new Row("android", "1.38.0", "111400", Client: "78"),
        };
        Assert.Equal("android", ProtoVersionOrdering.SortByRelease(rows, Key)[0].Platform);
    }
}
