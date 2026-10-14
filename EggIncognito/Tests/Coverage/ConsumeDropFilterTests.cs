using EggIncognito.Data.Models;
using EggIncognito.Services.Coverage;

namespace EggIncognito.Tests.Coverage;

public class ConsumeDropFilterTests {
    private static readonly Func<ArtifactConsumeObservation, bool> Counted = ConsumeDropFilter.Counted.Compile();

    private static ArtifactConsumeObservation Consume(bool serverDenied) => new() {
        Action = ConsumeDropFilter.ConsumeAction,
        SpecName = "LUNAR_TOTEM",
        SpecLevel = "GREATER",
        SpecRarity = "LEGENDARY",
        ServerDenied = serverDenied,
        Success = true
    };

    [Fact]
    public void ServerDenied_IsNotCounted() {
        Assert.False(Counted(Consume(serverDenied: true)));
    }

    [Fact]
    public void NotServerDenied_IsCounted() {
        Assert.True(Counted(Consume(serverDenied: false)));
    }

    [Fact]
    public void Unsuccessful_IsNotCountedEvenWhenNotServerDenied() {
        var row = Consume(serverDenied: false);
        row.Success = false;
        Assert.False(Counted(row));
    }
}
