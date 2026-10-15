using System.Threading.Channels;
using EggIncognito.Services.Admin;
using EggIncognito.Services.Events;
using EggIncognito.Services.Predictions.Contracts;
using EggIncognito.Services.Predictions.Events;

namespace EggIncognito.Services.Predictions.Training;

public sealed class PredictionTrainer(
    IServiceScopeFactory scopes,
    EventDataVersion eventVersion,
    ContractDataVersion contractVersion,
    EventPredictionCache eventCache,
    ContractPredictionCache contractCache,
    AdminNotifier notifier,
    TimeProvider time,
    ILogger<PredictionTrainer> logger) : BackgroundService {
    private const int ContractSlotWeeks = 26;
    private static readonly TimeSpan ColleggtiblePoll = TimeSpan.FromMilliseconds(100);
    private static readonly PredictionDomainKey[] Domains =
        [PredictionDomainKey.Events, PredictionDomainKey.Contracts, PredictionDomainKey.Colleggtibles];

    private readonly Lock _statusGate = new();
    private readonly Dictionary<PredictionDomainKey, TrainingStatus> _statuses =
        Domains.ToDictionary(d => d, d => new TrainingStatus(d, TrainingState.Idle, -1, null, null, 0, null, null, null));
    private readonly Dictionary<PredictionDomainKey, Channel<TrainingRequest>> _channels =
        Domains.ToDictionary(d => d, _ => Channel.CreateBounded<TrainingRequest>(
            new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite }));
    private readonly Dictionary<PredictionDomainKey, CancellationTokenSource> _cancellations = [];

    public TrainingStatus Status(PredictionDomainKey domain) {
        lock (_statusGate) return _statuses[domain];
    }

    public IReadOnlyList<TrainingStatus> Statuses {
        get {
            lock (_statusGate) return [.. Domains.Select(d => _statuses[d])];
        }
    }

    public void Request(PredictionDomainKey domain, bool force = false) {
        EnqueueOnly(domain, force);
        if (domain == PredictionDomainKey.Contracts) EnqueueOnly(PredictionDomainKey.Colleggtibles, force);
    }

    public void Cancel(PredictionDomainKey domain) {
        lock (_statusGate) {
            if (_cancellations.TryGetValue(domain, out var cts)) cts.Cancel();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        eventVersion.Changed += OnEventsChanged;
        contractVersion.Changed += OnContractsChanged;
        try {
            Request(PredictionDomainKey.Events);
            Request(PredictionDomainKey.Contracts);
            await Task.WhenAll(Domains.Select(d => RunLaneAsync(d, stoppingToken)));
        } finally {
            eventVersion.Changed -= OnEventsChanged;
            contractVersion.Changed -= OnContractsChanged;
        }
    }

    private void OnEventsChanged() => Request(PredictionDomainKey.Events);

    private void OnContractsChanged() => Request(PredictionDomainKey.Contracts);

    private void EnqueueOnly(PredictionDomainKey domain, bool force) {
        var status = Status(domain);
        long current = CurrentVersion(domain);
        if (!force && status.State == TrainingState.Done && status.Version == current) return;
        UpdateStatus(domain, s => s with { State = TrainingState.Queued });
        _channels[domain].Writer.TryWrite(new TrainingRequest(domain, force));
    }

    private long CurrentVersion(PredictionDomainKey domain) =>
        domain == PredictionDomainKey.Events ? eventVersion.Version : contractVersion.Version;

    private async Task RunLaneAsync(PredictionDomainKey domain, CancellationToken stoppingToken) {
        var reader = _channels[domain].Reader;
        while (await reader.WaitToReadAsync(stoppingToken)) {
            while (reader.TryRead(out var request)) {
                if (domain == PredictionDomainKey.Colleggtibles && !ContractsReady()) {
                    _channels[domain].Writer.TryWrite(request);
                    await Task.Delay(ColleggtiblePoll, stoppingToken);
                    continue;
                }

                await RunJobAsync(domain, stoppingToken);
            }
        }
    }

    private bool ContractsReady() {
        var status = Status(PredictionDomainKey.Contracts);
        return status.State == TrainingState.Done && status.Version == contractVersion.Version;
    }

    private async Task RunJobAsync(PredictionDomainKey domain, CancellationToken stoppingToken) {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        lock (_statusGate) _cancellations[domain] = cts;
        long version = CurrentVersion(domain);
        UpdateStatus(domain, s => s with {
            State = TrainingState.Running,
            Version = version,
            StartedAt = Now(),
            FinishedAt = null,
            Percent = 0,
            Error = null
        });
        try {
            await using var scope = scopes.CreateAsyncScope();
            var (strategy, runnerUpDelta) = domain switch {
                PredictionDomainKey.Events => await RunEventsAsync(scope.ServiceProvider, cts.Token),
                PredictionDomainKey.Contracts => await RunContractsAsync(scope.ServiceProvider, cts.Token),
                _ => await RunColleggtiblesAsync(scope.ServiceProvider, cts.Token)
            };
            UpdateStatus(domain, s => s with {
                State = TrainingState.Done,
                FinishedAt = Now(),
                Percent = 100,
                Strategy = strategy,
                RunnerUpDelta = runnerUpDelta
            });
        } catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested) {
            UpdateStatus(domain, s => s with { State = TrainingState.Cancelled, FinishedAt = Now() });
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            logger.LogWarning(ex, "Prediction training failed for {Domain}", domain);
            UpdateStatus(domain, s => s with { State = TrainingState.Failed, FinishedAt = Now(), Error = ex.Message });
        } finally {
            lock (_statusGate) _cancellations.Remove(domain);
            cts.Dispose();
        }
    }

    private async Task<(string Strategy, double RunnerUpDelta)> RunEventsAsync(IServiceProvider services, CancellationToken ct) {
        var predictor = services.GetRequiredService<EventPredictor>();
        var rows = await predictor.LoadRowsAsync(ct);
        double asOf = UnixSeconds.FromTime(time.GetUtcNow());
        var tuned = StrategyTuner.Fit(new EventDomain(), rows, asOf, WalkForward.DefaultLagSeconds, Progress(PredictionDomainKey.Events), ct);
        var projection = EventPredictor.Predict(rows, asOf, EventProjector.MaxHorizonDays, tuned.Strategy);
        eventCache.Set(eventVersion.Version, new EventCacheEntry(rows, tuned, projection));
        return (tuned.Strategy.Label, tuned.RunnerUpDelta);
    }

    private async Task<(string Strategy, double RunnerUpDelta)> RunContractsAsync(IServiceProvider services, CancellationToken ct) {
        var predictor = services.GetRequiredService<ContractPredictor>();
        var samples = await predictor.LoadSamplesAsync(ct);
        double now = UnixSeconds.FromTime(time.GetUtcNow());
        var tuned = StrategyTuner.Fit(new ContractDomain(), samples, now, WalkForward.DefaultLagSeconds, Progress(PredictionDomainKey.Contracts), ct);
        var slots = tuned.Model.Slots(time.GetUtcNow(), ContractSlotWeeks * ReleaseGrid.AllKinds.Length);
        var colleggtibles = contractCache.TryGet(contractVersion.Version, out var existing) ? existing.Colleggtibles : null;
        contractCache.Set(contractVersion.Version, new ContractCacheEntry(samples, tuned, colleggtibles, slots));
        return (tuned.Strategy.Label, tuned.RunnerUpDelta);
    }

    private async Task<(string Strategy, double RunnerUpDelta)> RunColleggtiblesAsync(IServiceProvider services, CancellationToken ct) {
        var predictor = services.GetRequiredService<ContractPredictor>();
        var samples = await predictor.LoadSamplesAsync(ct);
        if (!contractCache.TryGet(contractVersion.Version, out var entry))
            throw new InvalidOperationException("contracts entry missing for colleggtible training");
        var colleggtibles = StrategyTuner.Fit(
            new ColleggtibleDomain(entry.Tuned.Strategy), samples, entry.Tuned.Model.TrainedAt, WalkForward.DefaultLagSeconds,
            Progress(PredictionDomainKey.Colleggtibles), ct);
        contractCache.Update(contractVersion.Version, e => e with { Colleggtibles = colleggtibles });
        return (colleggtibles.Strategy.Label, colleggtibles.RunnerUpDelta);
    }

    private Progress<TrainingProgress> Progress(PredictionDomainKey domain) =>
        new(p => UpdateStatus(domain, s => s with { Percent = p.Percent }));

    private void UpdateStatus(PredictionDomainKey domain, Func<TrainingStatus, TrainingStatus> mutate) {
        TrainingStatus updated;
        bool publish;
        lock (_statusGate) {
            var previous = _statuses[domain];
            updated = mutate(previous);
            _statuses[domain] = updated;
            publish = previous.State != updated.State || Math.Abs(updated.Percent - previous.Percent) >= 2;
        }

        if (publish) notifier.Publish(AdminTopics.Predictions);
    }

    private double Now() => UnixSeconds.FromTime(time.GetUtcNow());
}
