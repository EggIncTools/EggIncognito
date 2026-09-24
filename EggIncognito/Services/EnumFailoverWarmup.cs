using EggIncognito.Core.Services;

namespace EggIncognito.Services;

public sealed class EnumFailoverWarmup(IEnumFailover failover) : BackgroundService {
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => failover.WarmAsync(stoppingToken);
}
