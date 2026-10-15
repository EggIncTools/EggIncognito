using EggIncognito.Data.Services;
using EggIncognito.Services.Admin;
using EggIncognito.Services.Predictions;
using EggIncognito.Services.Predictions.Training;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EggIncognito.Tests.Predictions;

public class PredictionTrainerTests {
    private sealed class NoScopeFactory : IServiceScopeFactory {
        public static readonly NoScopeFactory Instance = new();
        public IServiceScope CreateScope() => throw new NotSupportedException();
    }

    private static PredictionTrainer NewTrainer(EventDataVersion? eventVersion = null, ContractDataVersion? contractVersion = null) =>
        new(
            NoScopeFactory.Instance, eventVersion ?? new EventDataVersion(), contractVersion ?? new ContractDataVersion(),
            new EventPredictionCache(), new ContractPredictionCache(), new PredictionSettings(new ConfigurationBuilder().Build()),
            new AdminNotifier(), TimeProvider.System, NullLogger<PredictionTrainer>.Instance);

    private static (PredictionTrainer Trainer, EventDataVersion EventVersion, ContractDataVersion ContractVersion) NewIntegrationTrainer() {
        var eventVersion = new EventDataVersion();
        var contractVersion = new ContractDataVersion();
        var eventCache = new EventPredictionCache();
        var contractCache = new ContractPredictionCache();
        var notifier = new AdminNotifier();
        var dbOptions = new DbContextOptionsBuilder<EggIncognitoDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=1").Options;

        var services = new ServiceCollection();
        services.AddScoped(_ => new EggIncognitoDbContext(dbOptions));
        services.AddSingleton(eventVersion);
        services.AddSingleton(contractVersion);
        services.AddSingleton(eventCache);
        services.AddSingleton(contractCache);
        services.AddSingleton(notifier);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ILogger<ContractPredictor>>(NullLogger<ContractPredictor>.Instance);
        services.AddScoped<EventPredictor>();
        services.AddScoped<ContractPredictor>();
        services.AddSingleton(sp => new PredictionTrainer(
            sp.GetRequiredService<IServiceScopeFactory>(), eventVersion, contractVersion, eventCache, contractCache,
            new PredictionSettings(new ConfigurationBuilder().Build()), notifier, TimeProvider.System,
            NullLogger<PredictionTrainer>.Instance));

        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<PredictionTrainer>(), eventVersion, contractVersion);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout) {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition()) {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("condition was not met in time");
            await Task.Delay(20);
        }
    }

    [Fact]
    public void Statuses_Initially_AreIdleForAllThreeDomains() {
        var trainer = NewTrainer();

        Assert.Equal(
            [PredictionDomainKey.Events, PredictionDomainKey.Contracts, PredictionDomainKey.Colleggtibles],
            trainer.Statuses.Select(s => s.Domain).ToList());
        Assert.All(trainer.Statuses, s => Assert.Equal(TrainingState.Idle, s.State));
    }

    [Fact]
    public void Request_Events_MovesEventsToQueuedOnly() {
        var trainer = NewTrainer();

        trainer.Request(PredictionDomainKey.Events);

        Assert.Equal(TrainingState.Queued, trainer.Status(PredictionDomainKey.Events).State);
        Assert.Equal(TrainingState.Idle, trainer.Status(PredictionDomainKey.Contracts).State);
        Assert.Equal(TrainingState.Idle, trainer.Status(PredictionDomainKey.Colleggtibles).State);
    }

    [Fact]
    public void Request_Contracts_AlsoQueuesColleggtibles() {
        var trainer = NewTrainer();

        trainer.Request(PredictionDomainKey.Contracts);

        Assert.Equal(TrainingState.Queued, trainer.Status(PredictionDomainKey.Contracts).State);
        Assert.Equal(TrainingState.Queued, trainer.Status(PredictionDomainKey.Colleggtibles).State);
        Assert.Equal(TrainingState.Idle, trainer.Status(PredictionDomainKey.Events).State);
    }

    [Fact]
    public void Cancel_NoRunningJob_DoesNotThrow() => NewTrainer().Cancel(PredictionDomainKey.Events);

    [Fact]
    public void TrainingProgress_Percent_ReachesOneHundredOnLastStrategyLastSlot() =>
        Assert.Equal(100, new TrainingProgress(2, 3, 10, 10).Percent);

    [Fact]
    public void TrainingProgress_Percent_IsZeroAtTheVeryStart() =>
        Assert.Equal(0, new TrainingProgress(0, 3, 0, 10).Percent);

    [Fact]
    public async Task ExecuteAsync_OnStart_RunsEventsAndContractsLanes() {
        var (trainer, _, _) = NewIntegrationTrainer();
        using var cts = new CancellationTokenSource();
        await trainer.StartAsync(cts.Token);
        try {
            await WaitUntilAsync(() => trainer.Status(PredictionDomainKey.Events).State == TrainingState.Failed, TimeSpan.FromSeconds(10));
            await WaitUntilAsync(() => trainer.Status(PredictionDomainKey.Contracts).State == TrainingState.Failed, TimeSpan.FromSeconds(10));
            Assert.NotNull(trainer.Status(PredictionDomainKey.Events).Error);
            Assert.NotNull(trainer.Status(PredictionDomainKey.Contracts).Error);
        } finally {
            cts.Cancel();
            await trainer.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Colleggtibles_WaitsForContractsDone_NeverRunsWhenContractsFails() {
        var (trainer, _, contractVersion) = NewIntegrationTrainer();
        using var cts = new CancellationTokenSource();
        await trainer.StartAsync(cts.Token);
        try {
            await WaitUntilAsync(() => trainer.Status(PredictionDomainKey.Contracts).State == TrainingState.Failed, TimeSpan.FromSeconds(10));
            await Task.Delay(400);
            Assert.Equal(TrainingState.Queued, trainer.Status(PredictionDomainKey.Colleggtibles).State);
            Assert.Equal(contractVersion.Version, trainer.Status(PredictionDomainKey.Contracts).Version);
        } finally {
            cts.Cancel();
            await trainer.StopAsync(CancellationToken.None);
        }
    }
}
