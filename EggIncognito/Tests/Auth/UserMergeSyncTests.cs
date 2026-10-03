using System.ComponentModel.DataAnnotations.Schema;
using System.Net;
using System.Reflection;
using EggIdentity.Client;
using EggIdentity.Contract;
using EggIncognito.Data.Services;
using EggIncognito.Services.Auth;
using Microsoft.Extensions.Logging.Abstractions;

namespace EggIncognito.Tests.Auth;

public class UserMergeSyncTests {
    private static readonly Guid Kept = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Merged = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset At = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);

    private sealed class FakeRemapper : IUserMergeRemapper {
        public Dictionary<Guid, int> Rows { get; } = [];
        public DateTimeOffset? Watermark { get; set; }

        public Task<DateTimeOffset?> WatermarkAsync(CancellationToken ct) => Task.FromResult(Watermark);

        public Task<int> ApplyAsync(Guid mergedUserId, Guid keptUserId, DateTimeOffset mergedAt, CancellationToken ct) {
            int moved = mergedUserId == keptUserId ? 0 : Rows.GetValueOrDefault(mergedUserId);
            Rows.Remove(mergedUserId);
            if (moved > 0) Rows[keptUserId] = Rows.GetValueOrDefault(keptUserId) + moved;
            Watermark = mergedAt;
            return Task.FromResult(moved);
        }
    }

    private static (IdentityApiClient Client, List<string> Urls) Feed(Func<DateTimeOffset?, IEnumerable<UserMergeResponse>> rows) {
        var urls = new List<string>();
        var handler = new StubHttpMessageHandler(req => {
            urls.Add(req.RequestUri!.PathAndQuery);
            string? raw = System.Web.HttpUtility.ParseQueryString(req.RequestUri.Query)["since"];
            DateTimeOffset? since = raw is null ? null : DateTimeOffset.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);
            var page = rows(since).Where(r => since is null || r.MergedAt > since).OrderBy(r => r.MergedAt)
                .Take(UserMergeSyncService.PageSize).ToList();
            return StubResponses.Json(HttpStatusCode.OK, page);
        });
        return (new IdentityApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://identity") }), urls);
    }

    private static UserMergeResponse Merge(Guid merged, Guid kept, DateTimeOffset at) =>
        new() { MergedUserId = merged, KeptUserId = kept, MergedAt = at };

    [Fact]
    public async Task Sweep_moves_merged_rows_to_kept_and_advances_watermark() {
        var remapper = new FakeRemapper { Rows = { [Merged] = 3, [Kept] = 2 } };
        var (client, _) = Feed(_ => [Merge(Merged, Kept, At)]);

        int moved = await UserMergeSyncService.SweepAsync(client, remapper, NullLogger.Instance, CancellationToken.None);

        Assert.Equal(3, moved);
        Assert.Equal(5, remapper.Rows[Kept]);
        Assert.False(remapper.Rows.ContainsKey(Merged));
        Assert.Equal(At, remapper.Watermark);
    }

    [Fact]
    public async Task Sweep_rerun_is_idempotent() {
        var remapper = new FakeRemapper { Rows = { [Merged] = 3 } };
        var (client, _) = Feed(_ => [Merge(Merged, Kept, At)]);

        await UserMergeSyncService.SweepAsync(client, remapper, NullLogger.Instance, CancellationToken.None);
        int second = await UserMergeSyncService.SweepAsync(client, remapper, NullLogger.Instance, CancellationToken.None);

        Assert.Equal(0, second);
        Assert.Equal(3, remapper.Rows[Kept]);
        Assert.Equal(At, remapper.Watermark);
    }

    [Fact]
    public async Task Sweep_resumes_from_stored_watermark_with_boundary_overlap() {
        var remapper = new FakeRemapper { Watermark = At, Rows = { [Merged] = 1 } };
        var (client, urls) = Feed(_ => [Merge(Merged, Kept, At)]);

        int moved = await UserMergeSyncService.SweepAsync(client, remapper, NullLogger.Instance, CancellationToken.None);

        Assert.Single(urls);
        Assert.Contains("since=", urls[0], StringComparison.Ordinal);
        Assert.Equal(1, moved);
    }

    [Fact]
    public async Task Sweep_pages_until_a_short_page() {
        var remapper = new FakeRemapper();
        var all = Enumerable.Range(0, UserMergeSyncService.PageSize + 3)
            .Select(i => Merge(Guid.NewGuid(), Kept, At.AddSeconds(i)))
            .ToList();
        var (client, urls) = Feed(_ => all);

        await UserMergeSyncService.SweepAsync(client, remapper, NullLogger.Instance, CancellationToken.None);

        Assert.Equal(At.AddSeconds(UserMergeSyncService.PageSize + 2), remapper.Watermark);
        Assert.Equal(2, urls.Count);
    }

    [Fact]
    public void Remap_columns_cover_every_user_id_column_in_the_model() {
        var modelColumns = typeof(EggIncognitoDbContext).Assembly.GetTypes()
            .Where(t => t.Namespace == "EggIncognito.Data.Models" && t.GetCustomAttribute<TableAttribute>() is not null)
            .SelectMany(t => t.GetProperties()
                .Where(p => p.PropertyType == typeof(Guid) || p.PropertyType == typeof(Guid?))
                .Select(p => p.GetCustomAttribute<ColumnAttribute>()?.Name)
                .Where(c => c is not null && (c.EndsWith("user_id", StringComparison.Ordinal) || c == "updated_by"))
                .Select(c => $"{t.GetCustomAttribute<TableAttribute>()!.Name}.{c}"))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(modelColumns, UserMergeRemapper.Columns.Select(c => c.Name).Order(StringComparer.Ordinal));
    }
}
