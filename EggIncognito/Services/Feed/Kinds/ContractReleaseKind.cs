using System.Globalization;
using EggIdentity.Filters;
using EggIncognito.Data.Models;
using EggIncognito.Services.Assets;
using EggIncognito.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Services.Feed.Kinds;

public sealed record ContractReleasedEvent(
    string ContractId,
    string Name,
    int Egg,
    string? CustomEggId,
    DateTimeOffset Start,
    DateTimeOffset End,
    double LengthSeconds,
    bool Leggacy,
    bool UltraOnly,
    int ProphecyEggs,
    int MaxCoopSize,
    string Source,
    DateTimeOffset SeenAt,
    string PageUrl) : INotificationEvent {
    public string Kind => FeedEventKinds.ContractRelease;
    public string DedupKey => $"{ContractId}:{FeedText.Unix(Start)}";

    public double AgeHours => (SeenAt - Start).Duration().TotalHours;
    public bool Fresh => AgeHours <= FeedEventKinds.FreshWindow.TotalHours;
    public bool FromDevice => string.Equals(Source, ContractSources.Device, StringComparison.Ordinal);
    public double LengthDays => LengthSeconds / 86400d;
    public string EggLabel => CustomEggId ?? EggIcons.Name((Ei.Egg)Egg);
    public string EggKey => CustomEggId ?? ((Ei.Egg)Egg).ToString();

    public FilterSubjectValue Subject(string field) => field switch {
        ContractReleaseKind.EggField => FilterSubjectValue.Of(EggKey),
        ContractReleaseKind.ContractIdField => FilterSubjectValue.Of(ContractId),
        ContractReleaseKind.LeggacyField => FilterSubjectValue.Of(Leggacy),
        ContractReleaseKind.UltraField => FilterSubjectValue.Of(UltraOnly),
        ContractReleaseKind.Colleggtible => FilterSubjectValue.Of(CustomEggId is not null),
        ContractReleaseKind.ProphecyEggsField => FilterSubjectValue.Of((double)ProphecyEggs),
        ContractReleaseKind.MaxCoopSizeField => FilterSubjectValue.Of((double)MaxCoopSize),
        ContractReleaseKind.LengthDaysField => FilterSubjectValue.Of(LengthDays),
        ContractReleaseKind.Source => FilterSubjectValue.Of(Source),
        ContractReleaseKind.AgeHoursField => FilterSubjectValue.Of(AgeHours),
        _ => FilterSubjectValue.Absent
    };

    public string Summary =>
        $"{Name}{(Leggacy ? " (leggacy)" : "")}{(UltraOnly ? " ultra" : "")} {FeedText.Stamp(Start)}";

    public IReadOnlyDictionary<string, string> Vars() => new Dictionary<string, string> {
        ["contractId"] = ContractId,
        ["name"] = Name,
        ["egg"] = EggLabel,
        ["leggacy"] = Leggacy ? "leggacy" : "new",
        ["ultra"] = UltraOnly ? "ultra" : "standard",
        ["start"] = FeedText.Stamp(Start),
        ["end"] = FeedText.Stamp(End),
        ["lengthDays"] = FeedText.Number(LengthDays),
        ["prophecyEggs"] = FeedText.Count(ProphecyEggs),
        ["maxCoopSize"] = FeedText.Count(MaxCoopSize),
        ["pageUrl"] = PageUrl
    };

    public FeedEmbed Embed() {
        var fields = new List<FeedEmbedField> {
            new("Egg", EggLabel, true),
            new("Release", Leggacy ? "leggacy" : "new", true),
            new("Tier", UltraOnly ? "ultra only" : "standard", true),
            new("Starts", FeedText.Stamp(Start), true),
            new("Length", $"{FeedText.Number(LengthDays)} d", true),
            new("Co-op", FeedText.Count(MaxCoopSize), true)
        };
        if (ProphecyEggs > 0) fields.Add(new FeedEmbedField("Prophecy eggs", FeedText.Count(ProphecyEggs), true));
        int color = UltraOnly ? 0xd4a017 : Leggacy ? 0x8b5cf6 : 0x3fa06a;
        return new FeedEmbed($"Egg, Inc. contract: {Name}", PageUrl, color, fields);
    }
}

public static class ContractReleaseKind {
    public const string EggField = "egg";
    public const string ContractIdField = "contract_id";
    public const string LeggacyField = "leggacy";
    public const string UltraField = "ultra_only";
    public const string Colleggtible = "colleggtible";
    public const string ProphecyEggsField = "prophecy_eggs";
    public const string MaxCoopSizeField = "max_coop_size";
    public const string LengthDaysField = "length_days";
    public const string Source = "source";
    public const string AgeHoursField = "age_hours";

    private static IEnumerable<FilterOption> EggOptions() =>
        Enum.GetValues<Ei.Egg>()
            .Where(e => e is not (Ei.Egg.CustomEgg or Ei.Egg.Unknown))
            .Select(e => new FilterOption(e.ToString(), EggIcons.Label((int)e, null)) {
                Image = EggIcons.Url((int)e, null)
            });

    public static readonly FilterSet Defaults = FeedFilter.Rows(
        [FeedFilter.AtMost(AgeHoursField, FeedEventKinds.FreshWindow.TotalHours)],
        [FeedFilter.Is(Source, ContractSources.Device)]);

    public static FilterSet Legacy(FeedSubscription sub) {
        var rows = new List<FilterCondition[]>();
        switch (sub.Trigger) {
            case FeedEventKinds.TriggerNewOnly:
                rows.Add([FeedFilter.Flag(LeggacyField, false)]);
                break;
            case FeedEventKinds.TriggerLeggacyOnly:
                rows.Add([FeedFilter.Flag(LeggacyField, true)]);
                break;
            case FeedEventKinds.TriggerUltraOnly:
                rows.Add([FeedFilter.Flag(UltraField, true)]);
                break;
        }

        rows.AddRange(FeedEventKinds.IngestLegacyFilters(sub, AgeHoursField, Source, ContractSources.Device));
        return FeedFilter.Rows(rows);
    }

    public static async Task<IReadOnlyList<FeedSample>> LoadSamples(FeedSampleContext ctx, CancellationToken ct) {
        var rows = await ctx.Db.ContractReleases.AsNoTracking()
            .OrderByDescending(r => r.StartTime)
            .Take(FeedSampleSource.PerKind)
            .ToListAsync(ct);
        string pageUrl = ctx.Pages.PageUrl("contracts");
        return [.. rows.Select(r => new FeedSample(r.Id.ToString(CultureInfo.InvariantCulture),
            $"{r.Name}{(r.Leggacy ? " (leggacy)" : "")}", ContractIngestor.ToEvent(r, r.FirstSeenAt ?? r.StartTime, pageUrl)))];
    }

    public static readonly NotificationKind Definition = new(
        FeedEventKinds.ContractRelease, "Contract release",
        "A contract went live, new or leggacy.",
        [
            FeedFilter.Searchable(EggField, "Egg", EggOptions()),
            FeedFilter.Searchable(ContractIdField, "Contract", []),
            FeedFilter.Bool(LeggacyField, "Leggacy"),
            FeedFilter.Bool(UltraField, "Ultra only"),
            FeedFilter.Bool(Colleggtible, "Carries a colleggtible"),
            FeedFilter.Number(ProphecyEggsField, "Prophecy eggs"),
            FeedFilter.Number(MaxCoopSizeField, "Co-op size"),
            FeedFilter.Number(LengthDaysField, "Length in days"),
            FeedFilter.Select(Source, "Seen by", FeedFilter.Sources),
            FeedFilter.Number(AgeHoursField, "Hours between start and sighting")
        ],
        Defaults,
        Legacy,
        [
            "contractId", "name", "egg", "leggacy", "ultra", "start", "end", "lengthDays", "prophecyEggs",
            "maxCoopSize", "pageUrl"
        ],
        LoadSamples,
        LoadOptions);

    public static async Task<IReadOnlyDictionary<string, IReadOnlyList<FilterOption>>> LoadOptions(
        FeedSampleContext ctx, CancellationToken ct) {
        var contracts = await ctx.Db.ContractReleases.AsNoTracking()
            .GroupBy(r => r.ContractId)
            .Select(g => new { Id = g.Key, Name = g.OrderByDescending(r => r.StartTime).Select(r => r.Name).First() })
            .ToListAsync(ct);
        var customEggs = await ctx.Db.ContractReleases.AsNoTracking()
            .Where(r => r.CustomEggId != null)
            .Select(r => r.CustomEggId!)
            .Distinct()
            .ToListAsync(ct);
        return new Dictionary<string, IReadOnlyList<FilterOption>> {
            [ContractIdField] = [.. contracts
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .Select(c => new FilterOption(c.Id, c.Name) { Badge = c.Id })],
            [EggField] = [.. EggOptions(), .. customEggs.Order(StringComparer.Ordinal)
                .Select(id => new FilterOption(id, EggIcons.Label((int)Ei.Egg.CustomEgg, id)) {
                    Group = "Colleggtibles",
                    Image = EggIcons.Url((int)Ei.Egg.CustomEgg, id)
                })]
        };
    }
}
