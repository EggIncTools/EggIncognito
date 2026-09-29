using EggIncognito.Models.Coverage;

namespace EggIncognito.Tests.Coverage;

public class CoverageTargetRequestTests {
    [Fact]
    public void ValidRequest_HasNoError() =>
        Assert.Null(new CoverageTargetRequest("TUNGSTEN_ANKH", "INFERIOR", null, 200, 10, true).Error());

    [Fact]
    public void GlobalRequest_HasNoError() => Assert.Null(new CoverageTargetRequest(null, null, null, 1, 1, false).Error());

    [Fact]
    public void ZeroItemTarget_IsRejected() =>
        Assert.Equal("itemTarget must be at least 1", new CoverageTargetRequest(null, null, null, 0, 10, true).Error());

    [Fact]
    public void ZeroObservationTarget_IsRejected() =>
        Assert.Equal("observationTarget must be at least 1",
            new CoverageTargetRequest(null, null, null, 200, 0, true).Error());

    [Fact]
    public void LowercaseScope_IsRejected() =>
        Assert.Equal("scope values must be proto enum names",
            new CoverageTargetRequest("tungsten_ankh", null, null, 200, 10, true).Error());
}
