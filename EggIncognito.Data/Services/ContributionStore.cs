using EggIncognito.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Data.Services;

public sealed record ContributionCounts(int Recorded, int Submitted, int Approved, int Rejected);

public sealed record ContributionPage(IReadOnlyList<ContributedCapture> Rows, int Total);

public sealed record ContributorTally(Guid ContributorUserId, string Kind, int Submitted, DateTimeOffset Oldest);

public sealed class ContributionStore(EggIncognitoDbContext db, TimeProvider time) {
    public async Task<ContributionCounts> CountsForAsync(Guid userId, CancellationToken ct) {
        var raw = await db.ContributedCaptures.AsNoTracking()
            .Where(c => c.ContributorUserId == userId)
            .GroupBy(c => c.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        return Counts(raw.Select(r => (r.Status, r.Count)));
    }

    public async Task<ContributionCounts> CountsAllAsync(CancellationToken ct) {
        var raw = await db.ContributedCaptures.AsNoTracking()
            .GroupBy(c => c.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        return Counts(raw.Select(r => (r.Status, r.Count)));
    }

    private static ContributionCounts Counts(IEnumerable<(string Status, int Count)> raw) {
        var map = raw.ToDictionary(r => r.Status, r => r.Count, StringComparer.Ordinal);
        return new ContributionCounts(
            map.GetValueOrDefault(ContributedCaptureStatus.Recorded),
            map.GetValueOrDefault(ContributedCaptureStatus.Submitted),
            map.GetValueOrDefault(ContributedCaptureStatus.Approved),
            map.GetValueOrDefault(ContributedCaptureStatus.Rejected));
    }

    public async Task<ContributionPage> MineAsync(
        Guid userId, string? status, int skip, int take, CancellationToken ct) {
        var query = db.ContributedCaptures.AsNoTracking().Where(c => c.ContributorUserId == userId);
        if (!string.IsNullOrEmpty(status)) query = query.Where(c => c.Status == status);
        int total = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(c => c.RecordedAt).ThenByDescending(c => c.Id)
            .Skip(skip).Take(take).ToListAsync(ct);
        return new ContributionPage(rows, total);
    }

    public Task<int> SubmitAsync(Guid userId, CancellationToken ct) =>
        db.ContributedCaptures
            .Where(c => c.ContributorUserId == userId && c.Status == ContributedCaptureStatus.Recorded)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Status, ContributedCaptureStatus.Submitted)
                .SetProperty(c => c.SubmittedAt, time.GetUtcNow()), ct);

    public Task<int> DiscardAsync(Guid userId, CancellationToken ct) =>
        db.ContributedCaptures
            .Where(c => c.ContributorUserId == userId && c.Status == ContributedCaptureStatus.Recorded)
            .ExecuteDeleteAsync(ct);

    public async Task<ContributionPage> PendingAsync(string? kind, int skip, int take, CancellationToken ct) {
        var query = db.ContributedCaptures.AsNoTracking()
            .Where(c => c.Status == ContributedCaptureStatus.Submitted);
        if (!string.IsNullOrEmpty(kind)) query = query.Where(c => c.Kind == kind);
        int total = await query.CountAsync(ct);
        var rows = await query
            .OrderBy(c => c.SubmittedAt).ThenBy(c => c.Id)
            .Skip(skip).Take(take).ToListAsync(ct);
        return new ContributionPage(rows, total);
    }

    public async Task<List<ContributorTally>> PendingTalliesAsync(int take, CancellationToken ct) {
        var raw = await db.ContributedCaptures.AsNoTracking()
            .Where(c => c.Status == ContributedCaptureStatus.Submitted && c.SubmittedAt != null)
            .GroupBy(c => new { c.ContributorUserId, c.Kind })
            .Select(g => new {
                g.Key.ContributorUserId,
                g.Key.Kind,
                Submitted = g.Count(),
                Oldest = g.Min(x => x.SubmittedAt)
            })
            .OrderByDescending(t => t.Submitted)
            .Take(take)
            .ToListAsync(ct);

        return [
            .. raw.Select(r => new ContributorTally(r.ContributorUserId, r.Kind, r.Submitted, r.Oldest ?? default))
        ];
    }

    public Task<int> ReviewAsync(
        IReadOnlyList<long> ids, bool approve, string reviewer, string? note, CancellationToken ct) {
        string status = approve ? ContributedCaptureStatus.Approved : ContributedCaptureStatus.Rejected;
        return db.ContributedCaptures
            .Where(c => ids.Contains(c.Id) && c.Status == ContributedCaptureStatus.Submitted)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Status, status)
                .SetProperty(c => c.ReviewedBy, reviewer)
                .SetProperty(c => c.ReviewedAt, time.GetUtcNow())
                .SetProperty(c => c.ReviewNote, note), ct);
    }

    public Task<int> ReviewContributorAsync(
        Guid contributorUserId, string kind, bool approve, string reviewer, string? note, CancellationToken ct) {
        string status = approve ? ContributedCaptureStatus.Approved : ContributedCaptureStatus.Rejected;
        return db.ContributedCaptures
            .Where(c => c.ContributorUserId == contributorUserId
                        && c.Kind == kind
                        && c.Status == ContributedCaptureStatus.Submitted)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Status, status)
                .SetProperty(c => c.ReviewedBy, reviewer)
                .SetProperty(c => c.ReviewedAt, time.GetUtcNow())
                .SetProperty(c => c.ReviewNote, note), ct);
    }

    public Task<List<ContributedCapture>> ApprovedAsync(string kind, CancellationToken ct) =>
        db.ContributedCaptures.AsNoTracking()
            .Where(c => c.Kind == kind && c.Status == ContributedCaptureStatus.Approved)
            .ToListAsync(ct);
}
