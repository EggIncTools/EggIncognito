using EggIncognito.Services.Events;
using EggIncognito.Services.Predictions;
using EggIncognito.Services.Predictions.Contracts;

namespace EggIncognito.Tests.Predictions;

public class ColleggtibleDomainTests {
    private const double ShortLagSeconds = 3600d;

    [Fact]
    public void WalkForward_EggRotation_SyntheticRotation_HitsTop1() {
        const int eggCount = 5;
        const int cycles = 30;
        double asOf = UnixSeconds.FromTime(ContractTemplate.AsOf);
        double start = asOf - cycles * 7 * 86400d;
        var samples = new List<ContractReleaseSample>();
        for (int cycle = 0; cycle < cycles; cycle++) {
            int eggIx = cycle % eggCount;
            double releaseTime = start + cycle * 7 * 86400d;
            samples.Add(new ContractReleaseSample(
                $"egg{eggIx}-c{cycle}", $"Egg {eggIx}", releaseTime, 5 * 86400d, true, 0, false, 0, $"egg-{eggIx}"));
        }

        var strategy = ColleggtibleStrategy.Default with { Grouping = ColleggtibleGrouping.EggRotation };
        var domain = new ColleggtibleDomain(ContractStrategy.Default);
        double evalFrom = start + (eggCount + 1) * 7 * 86400d;

        var result = WalkForward.Run(domain, samples, strategy, ShortLagSeconds, evalFrom, asOf);

        Assert.True(result.Total.Slots > 0);
        Assert.Equal(result.Total.Slots, result.Total.Top1);
    }
}
