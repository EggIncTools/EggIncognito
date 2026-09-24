using EggIncognito.Core.Services;
using Ei;
using Google.Protobuf;

namespace EggIncognito.Tests;

public class EnumFailoverTests {
    private sealed class StubSource(IReadOnlyList<LatestProtoText> protos) : ILastKnownProtoSource {
        public Task<IReadOnlyList<LatestProtoText>> GetLatestProtosAsync(CancellationToken ct = default) =>
            Task.FromResult(protos);
    }

    private const string ProtoText = """
                                     syntax = "proto2";
                                     package ei;
                                     enum Platform {
                                         UNKNOWN_PLATFORM = 0;
                                         IOS = 1;
                                         DROID = 2;
                                         STEAM = 987654;
                                     }
                                     """;

    [Fact]
    public async Task Apply_SubstitutesLastKnownNameForUnknownEnum() {
        var msg = new EggIncFirstContactRequest { Platform = (Platform)987654 };
        string json = JsonFormatter.Default.Format(msg);

        var failover = new EnumFailover(new StubSource([new LatestProtoText("android", "1.99", ProtoText)]));
        await failover.WarmAsync();
        string result = failover.Apply(msg, json);

        Assert.Contains("\"STEAM\"", result);
        Assert.DoesNotContain("987654", result);
    }

    [Fact]
    public async Task Apply_LeavesKnownEnumUntouched() {
        var msg = new EggIncFirstContactRequest { Platform = Platform.Ios };
        string json = JsonFormatter.Default.Format(msg);

        var failover = new EnumFailover(new StubSource([new LatestProtoText("android", "1.99", ProtoText)]));
        await failover.WarmAsync();
        string result = failover.Apply(msg, json);

        Assert.Contains("\"IOS\"", result);
    }

    [Fact]
    public void ParseEnumIndex_TopLevelAndNested_ProducesFullNameKeys() {
        const string proto = """
                             syntax = "proto2";
                             package ei;
                             enum Platform {
                                 UNKNOWN_PLATFORM = 0;
                                 IOS = 1;
                                 DROID = 2;
                             }
                             message Bar {
                                 enum Foo {
                                     A = 0;
                                     B = 5 [deprecated = true];
                                 }
                                 optional Foo foo = 1;
                             }
                             """;

        var index = ProtoEnumIndex.Parse(proto);

        Assert.True(index.ContainsKey("ei.Platform"));
        Assert.True(index.ContainsKey("ei.Bar.Foo"));
        Assert.Equal("DROID", index["ei.Platform"][2]);
        Assert.Equal("B", index["ei.Bar.Foo"][5]);
        Assert.False(index.ContainsKey("ei.Foo"));
    }
}
