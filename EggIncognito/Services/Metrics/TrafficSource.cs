using EggIdentity.Contract;
using EggIdentity.Metrics;

namespace EggIncognito.Services.Metrics;

public sealed class TrafficSource(TrafficReporter reporter) : ITrafficSource {
    public Task<TrafficSnapshot> GetSnapshotAsync(CancellationToken ct) => Task.FromResult(reporter.Snapshot());
}
