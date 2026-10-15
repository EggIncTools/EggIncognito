using System.Threading.Channels;
using EggIncognito.Models.Calendar;
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
    PredictionSettings settings,
    AdminNotifier notifier,
    TimeProvider time,
    ILogger<PredictionTrainer> logger) : BackgroundService {
    private static readonly TimeSpan ColleggtiblePoll = TimeSpan.FromMilliseconds(100);
    private static readonly PredictionDomainKey[] Domains =
        [PredictionDomainKey.Events, PredictionDomainKey.Contracts, PredictionDomainKey.Colleggtibles];

    private readonly Lock _statusGate = new();
    private readonly Dictionary<PredictionDomainKey, TrainingStatus> _statuses =
        Domains.ToDictionary(d => d, d => new TrainingStatus(d, TrainingState.Idle, -1, null, null, 0, null, null, null, null, null));
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

    public bool RequestCsvTune(PredictionDomainKey domain, Func<CancellationToken, CsvTuneResult> tune) {
        if (domain == PredictionDomainKey.Colleggtibles) return false;
        if (!_channels[domain].Writer.TryWrite(new TrainingRequest(domain, true, tune))) return false;
        UpdateStatus(domain, s => s with { State = TrainingState.Queued });
        return true;
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
            await Task.WhenAll(Domains.Select(d => RunLaneAsync(d, stoppingToken)).Append(RefreshLoopAsync(stoppingToken)));
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

                if (request.Csv is { } csv) await RunCsvAsync(domain, csv, stoppingToken);
                else await RunJobAsync(domain, stoppingToken);
            }
        }
    }

    private bool ContractsReady() {
        var status = Status(PredictionDomainKey.Contracts);
        return status.State == TrainingState.Done && status.Version == contractVersion.Version;
    }

    private async Task RefreshLoopAsync(CancellationToken stoppingToken) {
        while (!stoppingToken.IsCancellationRequested) {
            try {
                await Task.Delay(TimeSpan.FromHours(settings.Snapshot().RefreshHours), time, stoppingToken);
            } catch (OperationCanceledException) {
                return;
            }

            Request(PredictionDomainKey.Events, force: true);
            Request(PredictionDomainKey.Contracts, force: true);
        }
    }

    private async Task RunCsvAsync(PredictionDomainKey domain, Func<CancellationToken, CsvTuneResult> tune, CancellationToken stoppingToken) {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        lock (_statusGate) _cancellations[domain] = cts;
        var before = Status(domain);
        UpdateStatus(domain, s => s with { State = TrainingState.Running, StartedAt = Now(), FinishedAt = null, Percent = 0, Error = null });
        try {
            var result = await Task.Run(() => tune(cts.Token), cts.Token);
            UpdateStatus(domain, s => s with {
                State = before.State == TrainingState.Done ? TrainingState.Done : TrainingState.Idle,
                FinishedAt = Now(),
                Percent = 100,
                LastCsvTune = result
            });
        } catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested) {
            UpdateStatus(domain, s => s with { State = TrainingState.Cancelled, FinishedAt = Now() });
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            logger.LogWarning(ex, "CSV tune failed for {Domain}", domain);
            UpdateStatus(domain, s => s with { State = TrainingState.Failed, FinishedAt = Now(), Error = ex.Message });
        } finally {
            lock (_statusGate) _cancellations.Remove(domain);
            cts.Dispose();
        }
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
            var horizons = settings.Snapshot();
            int[] steps = domain == PredictionDomainKey.Events ? [.. settings.EventSteps] : [.. settings.ContractSteps];
            UpdateStatus(domain, s => s with {
                State = TrainingState.Done,
                FinishedAt = Now(),
                Percent = 100,
                Strategy = strategy,
                RunnerUpDelta = runnerUpDelta,
                Horizons = horizons,
                Steps = steps
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
        var horizons = settings.Snapshot();
        var tuned = StrategyTuner.Fit(new EventDomain(), rows, asOf, WalkForward.DefaultLagSeconds, Progress(PredictionDomainKey.Events), ct);
        var projection = EventPredictor.Predict(rows, asOf, horizons.EventDays, tuned.Strategy);
        Dictionary<int, IReadOnlyList<WatchEntry>> watches = [];
        foreach (int step in settings.EventSteps)
            watches[step] = EventPredictor.Watch(rows, EventPredictor.Slice(projection, asOf, asOf + step * EventHistory.Day), asOf);
        eventCache.Set(eventVersion.Version, new EventCacheEntry(rows, tuned, projection, horizons, asOf, watches));
        return (tuned.Strategy.Label, tuned.RunnerUpDelta);
    }

    private async Task<(string Strategy, double RunnerUpDelta)> RunContractsAsync(IServiceProvider services, CancellationToken ct) {
        var predictor = services.GetRequiredService<ContractPredictor>();
        var samples = await predictor.LoadSamplesAsync(ct);
        double now = UnixSeconds.FromTime(time.GetUtcNow());
        var horizons = settings.Snapshot();
        var tuned = StrategyTuner.Fit(new ContractDomain(), samples, now, WalkForward.DefaultLagSeconds, Progress(PredictionDomainKey.Contracts), ct);
        var slots = tuned.Model.Slots(time.GetUtcNow(), horizons.ContractWeeks * ReleaseGrid.AllKinds.Length);
        Dictionary<int, IReadOnlyList<WatchEntry>> watches = [];
        foreach (int weeks in settings.ContractSteps)
            watches[weeks] = ContractPredictor.Watch(samples, tuned.Model, now, weeks);
        bool hadExisting = contractCache.TryGet(contractVersion.Version, out var existing);
        var colleggtibles = hadExisting ? existing.Colleggtibles : null;
        var colleggtibleWatches = hadExisting ? existing.ColleggtibleWatches : null;
        var next = ContractPredictor.BuildNext(samples, slots, colleggtibles, now);
        contractCache.Set(contractVersion.Version, new ContractCacheEntry(
            samples, tuned, colleggtibles, slots, horizons, now, watches, colleggtibleWatches, next));
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
        Dictionary<int, IReadOnlyList<WatchEntry>> colleggtibleWatches = [];
        foreach (int weeks in settings.ContractSteps)
            colleggtibleWatches[weeks] = ContractPredictor.ColleggtibleWatch(samples, colleggtibles.Model, entry.ComputedAt, weeks);
        contractCache.Update(contractVersion.Version, e => e with {
            Colleggtibles = colleggtibles,
            ColleggtibleWatches = colleggtibleWatches,
            Next = ContractPredictor.BuildNext(e.Samples, e.Slots, colleggtibles, e.ComputedAt)
        });
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
