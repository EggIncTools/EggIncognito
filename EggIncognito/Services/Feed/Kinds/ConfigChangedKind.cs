using EggIdentity.Filters;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Services.DataApi;
using Ei;
using Google.Protobuf;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Services.Feed.Kinds;

public sealed record ColleggtibleBuff(GameModifier.Types.GameDimension Dimension, double Value);

public sealed record NewColleggtible(string Identifier, string Name, IReadOnlyList<ColleggtibleBuff> Buffs) {
    public static NewColleggtible From(CustomEgg egg) =>
        new(egg.Identifier, egg.Name, [.. egg.Buffs.Select(b => new ColleggtibleBuff(b.Dimension, b.Value))]);

    public string Text => Buffs.Count == 0
        ? Name
        : $"{Name} ({string.Join(", ", Buffs.Select(b => $"{b.Dimension} x{FeedText.Number(b.Value)}"))})";
}

public sealed record ConfigChangedEvent(
    string Feed,
    string Sha,
    string PageUrl,
    ConfigChangeSummary? Change = null,
    string? DedupSha = null,
    IReadOnlyList<NewColleggtible>? Colleggtibles = null) : INotificationEvent {
    public string Kind => FeedEventKinds.ConfigChanged;
    public string DedupKey => $"{Feed}:{DedupSha ?? Sha}";

    public IReadOnlyList<NewColleggtible> NewColleggtibles => Colleggtibles ?? [];

    public FilterSubjectValue Subject(string field) => field switch {
        ConfigChangedKind.Feed => FilterSubjectValue.Of(Feed),
        ConfigChangedKind.Changed => FilterSubjectValue.Of(Changed),
        ConfigChangedKind.ChangedCount => FilterSubjectValue.Of((double)Changed.Count),
        ConfigChangedKind.IdCount => FilterSubjectValue.Of((double)(Added.Count + Removed.Count)),
        ConfigChangedKind.NewColleggtibleCount => FilterSubjectValue.Of((double)NewColleggtibles.Count),
        ConfigChangedKind.NewColleggtibleBuff => FilterSubjectValue.Of(
            NewColleggtibles.SelectMany(c => c.Buffs).Select(b => b.Dimension.ToString()).Distinct()),
        _ => FilterSubjectValue.Absent
    };

    public string FeedLabel => ConfigFeeds.LabelOf(Feed);

    public IReadOnlyList<string> Changed => Change?.Changed ?? [];
    public IReadOnlyList<string> Added => Change?.Added ?? [];
    public IReadOnlyList<string> Removed => Change?.Removed ?? [];

    public string Summary => NewColleggtibles.Count > 0
        ? $"new colleggtible {string.Join(", ", NewColleggtibles.Select(c => c.Name))}"
        : Changed.Count == 0
            ? $"{FeedLabel} changed"
            : $"{FeedLabel}: {string.Join(", ", Changed)}";

    public IReadOnlyDictionary<string, string> Vars() => new Dictionary<string, string> {
        ["feed"] = Feed,
        ["feedLabel"] = FeedLabel,
        ["sha"] = Sha,
        ["pageUrl"] = PageUrl,
        ["changed"] = FeedText.Joined(Changed),
        ["added"] = FeedText.Joined(Added),
        ["removed"] = FeedText.Joined(Removed),
        ["newColleggtibles"] = FeedText.Joined([.. NewColleggtibles.Select(c => c.Text)])
    };

    public FeedEmbed Embed() {
        var fields = new List<FeedEmbedField> {
            new("Response", FeedLabel, true),
            new("Hash", FeedText.Short(Sha), true)
        };
        if (NewColleggtibles.Count > 0)
            fields.Add(new FeedEmbedField("New colleggtibles", FeedText.Listed([.. NewColleggtibles.Select(c => c.Text)]), false));
        if (Changed.Count > 0) fields.Add(new FeedEmbedField("Changed", FeedText.Listed(Changed), false));
        if (Added.Count > 0) fields.Add(new FeedEmbedField("Added", FeedText.Listed(Added), false));
        if (Removed.Count > 0) fields.Add(new FeedEmbedField("Removed", FeedText.Listed(Removed), false));
        int color = NewColleggtibles.Count > 0 ? 0xe0a030 : Changed.Count == 0 ? 0x5aa9e6 : 0x8b5cf6;
        return new FeedEmbed($"Egg, Inc. {FeedLabel} changed", PageUrl, color, fields);
    }
}

public static class ConfigChangedKind {
    public static string PageUrl(string? baseUrl, string feed) {
        string root = string.IsNullOrEmpty(baseUrl) ? FeedPages.DefaultPageBaseUrl : baseUrl;
        return FeedPages.Url(root, string.Equals(feed, ConfigFeeds.Periodicals, StringComparison.Ordinal) ? "periodicals" : "data");
    }

    public const string Feed = "feed";
    public const string Changed = "changed";
    public const string ChangedCount = "changed_count";
    public const string IdCount = "id_count";
    public const string NewColleggtibleCount = "new_colleggtible_count";
    public const string NewColleggtibleBuff = "new_colleggtible_buff";

    private static readonly string[] Aspects = [
        "sales", "events", "liveConfig", "mail", "contracts", "colleggtibles", "admin", "dlcItems", "shells",
        "shellSets", "decorators", "shellObjects", "shellGroups", "fontPacks", "artifacts", "missions", "crafting",
        "seasons"
    ];

    public static readonly FilterSet Defaults = FeedFilter.Rows([FeedFilter.Above(ChangedCount, 0)]);

    public static FilterSet Legacy(FeedSubscription sub) {
        if (string.Equals(sub.EventKind, FeedEventKinds.LegacyColleggtible, StringComparison.Ordinal))
            return FeedFilter.Rows([FeedFilter.Above(NewColleggtibleCount, 0)]);
        var rows = new List<FilterCondition[]>();
        if (ConfigFeeds.ById(sub.Trigger) is { } feed) rows.Add([FeedFilter.Is(Feed, feed.Id)]);
        if (sub.Filters.Contains(FeedEventKinds.FilterRequireAspects)) rows.Add([FeedFilter.Above(ChangedCount, 0)]);
        if (sub.Filters.Contains(FeedEventKinds.FilterRequireIds)) rows.Add([FeedFilter.Above(IdCount, 0)]);
        return FeedFilter.Rows(rows);
    }

    public static async Task<IReadOnlyList<FeedSample>> LoadSamples(FeedSampleContext ctx, CancellationToken ct) {
        var snaps = await ctx.Db.PeriodicalsSnapshots.AsNoTracking()
            .OrderByDescending(s => s.CapturedAt)
            .Take(FeedSampleSource.PerKind + 1)
            .ToListAsync(ct);
        var newest = await NewestColleggtibleAsync(ctx.Db, ct);
        string pageUrl = ctx.Pages.PageUrl("periodicals");

        var samples = new List<FeedSample>();
        for (int i = 0; i + 1 < snaps.Count; i++) {
            var snap = snaps[i];
            var change = ConfigAspects.Diff(ConfigFeeds.Periodicals, snaps[i + 1].ResponseJson, snap.ResponseJson);
            IReadOnlyList<NewColleggtible> eggs = newest is { } n && newest.Value.SeenIn == snap.Sha ? [n.Egg] : [];
            var evt = new ConfigChangedEvent(ConfigFeeds.Periodicals, snap.Sha, pageUrl, change, null, eggs);
            samples.Add(new FeedSample(snap.Sha, $"Periodicals {FeedText.Stamp(snap.CapturedAt)}", evt));
        }

        if (newest is { } found && samples.All(s => ((ConfigChangedEvent)s.Event).NewColleggtibles.Count == 0)) {
            samples.Insert(0, new FeedSample($"colleggtible:{found.Egg.Identifier}", $"New colleggtible {found.Egg.Name}",
                new ConfigChangedEvent(ConfigFeeds.Periodicals, found.SeenIn ?? "", pageUrl,
                    new ConfigChangeSummary(["colleggtibles"], [$"colleggtible:{found.Egg.Identifier}"], []), null,
                    [found.Egg])));
        }

        return samples;
    }

    private static async Task<(NewColleggtible Egg, string? SeenIn)?> NewestColleggtibleAsync(
        EggIncognitoDbContext db, CancellationToken ct) {
        var first = await db.ContractReleases.AsNoTracking()
            .Where(r => r.CustomEggId != null)
            .GroupBy(r => r.CustomEggId!)
            .Select(g => new { Id = g.Key, First = g.Min(r => r.StartTime) })
            .OrderByDescending(g => g.First)
            .FirstOrDefaultAsync(ct);
        if (first is null) return null;

        var snaps = await db.PeriodicalsSnapshots.AsNoTracking()
            .Where(s => s.CapturedAt >= first.First)
            .OrderBy(s => s.CapturedAt)
            .Take(8)
            .ToListAsync(ct);
        foreach (var snap in snaps) {
            PeriodicalsResponse response;
            try {
                response = (PeriodicalsResponse)JsonParser.Default.Parse(snap.ResponseJson, PeriodicalsResponse.Descriptor);
            } catch (InvalidJsonException) {
                continue;
            } catch (InvalidProtocolBufferException) {
                continue;
            }

            if (response.Contracts?.CustomEggs.FirstOrDefault(e => e.Identifier == first.Id) is { } egg)
                return (NewColleggtible.From(egg), snap.Sha);
        }

        return null;
    }

    public static readonly NotificationKind Definition = new(
        FeedEventKinds.ConfigChanged, "Config changed",
        "A live server response changed.",
        [
            FeedFilter.Select(Feed, "Response", ConfigFeeds.All.Select(f => new FilterOption(f.Id, f.Label))),
            FeedFilter.Set(Changed, "Changed aspect", FeedFilter.Options(Aspects)),
            FeedFilter.Number(ChangedCount, "Changed aspect count"),
            FeedFilter.Number(IdCount, "Added or removed entry count"),
            FeedFilter.Number(NewColleggtibleCount, "New colleggtibles"),
            FeedFilter.Set(NewColleggtibleBuff, "New colleggtible buff",
                Enum.GetValues<GameModifier.Types.GameDimension>()
                    .Where(d => d != GameModifier.Types.GameDimension.Invalid)
                    .Select(d => new FilterOption(d.ToString(), FeedVars.Label(d.ToString()))))
        ],
        Defaults,
        Legacy,
        ["feed", "feedLabel", "sha", "pageUrl", "changed", "added", "removed", "newColleggtibles"],
        LoadSamples);
}
