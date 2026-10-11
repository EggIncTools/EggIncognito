using EggIncognito.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Data.Services;

public sealed record ContributionCounts(int Recorded, int Submitted, int Approved, int Rejected);

public sealed record ContributionPage(IReadOnlyList<ContributedCapture> Rows, int Total);

public sealed class ContributionStore(EggIncognitoDbContext db, TimeProvider time) {
    public async Task<ContributionCounts> CountsForAsync(Guid userId, CancellationToken ct) {
        var raw = await db.ContributedCaptures.AsNoTracking()
            .Where(c => c.ContributorUserId == userId)
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

    public Task<List<ContributedCapture>> ApprovedAsync(string kind, CancellationToken ct) =>
        db.ContributedCaptures.AsNoTracking()
            .Where(c => c.Kind == kind && c.Status == ContributedCaptureStatus.Approved)
            .ToListAsync(ct);
}
