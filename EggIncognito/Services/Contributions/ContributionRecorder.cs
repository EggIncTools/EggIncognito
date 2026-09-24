using System.Threading.Channels;
using EggIncognito.Capture;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Services.Contributions;

public sealed class ContributionRecorder(
    IServiceScopeFactory scopes,
    ICaptureContributionKinds kinds,
    ContributionOptions options,
    ILogger<ContributionRecorder> logger,
    TimeProvider time) : IHostedService, IDisposable {
    private readonly Channel<ContributedCapture> _queue =
        Channel.CreateUnbounded<ContributedCapture>(new UnboundedChannelOptions { SingleReader = true });

    private CancellationTokenSource? _cts;
    private Task? _drain;

    public void Record(Guid contributorUserId, DashboardFlow flow) {
        if (!options.Enabled || contributorUserId == Guid.Empty) return;
        if (kinds.For(flow.Path) is not { } kind) return;

        try {
            if (kind.Build(flow) is not { } draft) return;
            _queue.Writer.TryWrite(new ContributedCapture {
                ContributorUserId = contributorUserId,
                Kind = draft.Kind,
                Status = ContributedCaptureStatus.Recorded,
                Summary = draft.Summary,
                Payload = draft.PayloadJson,
                DedupeHash = draft.DedupeHash,
                ClientVersion = draft.ClientVersion,
                RecordedAt = time.GetUtcNow()
            });
        } catch (Exception ex) {
            CaptureDiagnostics.Failed("contribution", flow.Path, ex);
        }
    }

    public Task StartAsync(CancellationToken cancellationToken) {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _drain = Task.Run(() => DrainAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken) {
        _queue.Writer.TryComplete();
        if (_drain is null) return;

        try {
            await _drain.WaitAsync(cancellationToken);
        } catch (OperationCanceledException ex) {
            logger.LogDebug(ex, "contribution drain cancelled with rows still queued");
        } finally {
            if (_cts is not null) await _cts.CancelAsync();
        }
    }

    public void Dispose() => _cts?.Dispose();

    private async Task DrainAsync(CancellationToken ct) {
        try {
            while (await _queue.Reader.WaitToReadAsync(ct)) {
                var batch = new List<ContributedCapture>(options.BatchSize);
                while (batch.Count < options.BatchSize && _queue.Reader.TryRead(out var row)) batch.Add(row);
                if (batch.Count > 0) await WriteBatchAsync(batch, ct);
            }
        } catch (OperationCanceledException ex) {
            logger.LogDebug(ex, "contribution drain stopped");
        }
    }

    private async Task WriteBatchAsync(List<ContributedCapture> batch, CancellationToken ct) {
        try {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<EggIncognitoDbContext>();
            var admitted = await AdmitAsync(db, batch, ct);
            if (admitted.Count == 0) return;

            db.ContributedCaptures.AddRange(admitted);
            try {
                await db.SaveChangesAsync(ct);
            } catch (DbUpdateException) {
                db.ChangeTracker.Clear();
                await WriteIndividuallyAsync(db, admitted, ct);
            }
        } catch (Exception ex) {
            logger.LogWarning(ex, "contribution batch of {Count} failed", batch.Count);
        }
    }

    private async Task<List<ContributedCapture>> AdmitAsync(
        EggIncognitoDbContext db, List<ContributedCapture> batch, CancellationToken ct) {
        var admitted = new List<ContributedCapture>(batch.Count);
        foreach (var group in batch.GroupBy(r => r.ContributorUserId)) {
            int recorded = await db.ContributedCaptures
                .CountAsync(c => c.ContributorUserId == group.Key
                                 && c.Status == ContributedCaptureStatus.Recorded, ct);
            int headroom = options.MaxRecordedPerUser - recorded;
            if (headroom <= 0) {
                logger.LogInformation("contribution quota reached for {User}, dropping {Count}",
                    group.Key, group.Count());
                continue;
            }

            admitted.AddRange(group.Take(headroom));
        }

        return admitted;
    }

    private static async Task WriteIndividuallyAsync(
        EggIncognitoDbContext db, List<ContributedCapture> rows, CancellationToken ct) {
        foreach (var row in rows) {
            try {
                db.ContributedCaptures.Add(row);
                await db.SaveChangesAsync(ct);
            } catch (DbUpdateException) {
                db.ChangeTracker.Clear();
            }
        }
    }
}
