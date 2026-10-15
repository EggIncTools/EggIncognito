using EggIncognito.Models.Contracts;
using EggIncognito.Services.Events;
using EggIncognito.Services.Predictions;
using EggIncognito.Services.Predictions.Contracts;

namespace EggIncognito.Tests.Predictions;

public class ContractDomainTests {
    private const double ShortLagSeconds = 3600d;

    [Fact]
    public void WalkForward_Alternating_BeatsSharedAndHitsFridayTop1() {
        var samples = ContractTemplate.Build(3 * ContractTemplate.PePoolSize);
        double asOf = UnixSeconds.FromTime(ContractTemplate.AsOf);
        var domain = new ContractDomain();
        double evalFrom = samples.Where(s => s.ProphecyEggs > 0).Select(s => s.Start).Order().Skip(2 * 10).First();

        var shared = WalkForward.Run(domain, samples, ContractStrategy.Default, ShortLagSeconds, evalFrom, asOf);
        var alternating = WalkForward.Run(
            domain, samples, ContractStrategy.Default with { PeMembership = PeMembership.Alternating },
            ShortLagSeconds, evalFrom, asOf);

        Assert.True(
            alternating.Total.LogLoss < shared.Total.LogLoss,
            $"alternating {alternating.Total.LogLoss} shared {shared.Total.LogLoss}");

        foreach (var kind in new[] { ContractSlotKind.PeLeggacy, ContractSlotKind.PeLeggacyUltra }) {
            var row = alternating.Kinds.Single(k => k.Kind == kind.ToString());
            Assert.True(row.Slots > 0);
            Assert.Equal(row.Slots, row.Top1);
        }
    }
}
