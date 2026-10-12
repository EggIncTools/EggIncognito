using EggIdentity.Filters;
using Ei;

namespace EggIncognito.Services.Feed.Kinds;

public sealed record ColleggtibleBuff(GameModifier.Types.GameDimension Dimension, double Value);

public sealed record ColleggtibleAddedEvent(
    string Identifier,
    string Name,
    string Description,
    IReadOnlyList<ColleggtibleBuff> Buffs,
    IReadOnlyList<string> ContractIds,
    string PageUrl) : INotificationEvent {
    public string Kind => FeedEventKinds.Colleggtible;
    public string DedupKey => Identifier;

    public string Summary => $"new colleggtible {Name}";

    public string BuffText => Buffs.Count == 0
        ? "none"
        : string.Join(", ", Buffs.Select(b => $"{b.Dimension} x{FeedText.Number(b.Value)}"));

    public FilterSubjectValue Subject(string field) => field switch {
        ColleggtibleKind.BuffDimension => FilterSubjectValue.Of(Buffs.Select(b => b.Dimension.ToString())),
        ColleggtibleKind.BuffCount => FilterSubjectValue.Of((double)Buffs.Count),
        _ => FilterSubjectValue.Absent
    };

    public IReadOnlyDictionary<string, string> Vars() => new Dictionary<string, string> {
        ["identifier"] = Identifier,
        ["name"] = Name,
        ["description"] = Description,
        ["buffs"] = BuffText,
        ["contracts"] = FeedText.Joined(ContractIds),
        ["pageUrl"] = PageUrl
    };

    public FeedEmbed Embed() {
        var fields = new List<FeedEmbedField> {
            new("Buffs", BuffText, false),
            new("Contracts", FeedText.Or(FeedText.Listed(ContractIds), "none"), false)
        };
        if (Description.Length > 0) fields.Insert(0, new FeedEmbedField("Description", Description, false));
        return new FeedEmbed($"Egg, Inc. new colleggtible: {Name}", PageUrl, 0xe0a030, fields);
    }
}

public static class ColleggtibleKind {
    public const string BuffDimension = "buff_dimension";
    public const string BuffCount = "buff_count";

    private const string SampleUrl = FeedDispatcher.DefaultPageBaseUrl + "/periodicals";

    public static ColleggtibleAddedEvent From(CustomEgg egg, IReadOnlyList<string> contractIds, string pageUrl) =>
        new(egg.Identifier, egg.Name, egg.Description,
            [.. egg.Buffs.Select(b => new ColleggtibleBuff(b.Dimension, b.Value))], contractIds, pageUrl);

    public static readonly NotificationKind Definition = new(
        FeedEventKinds.Colleggtible, "New colleggtible",
        "A custom egg was carried by a contract for the first time ever. Returning colleggtibles on leggacy runs never fire.",
        [
            FeedFilter.Set(BuffDimension, "Buff dimension",
                Enum.GetValues<GameModifier.Types.GameDimension>()
                    .Where(d => d != GameModifier.Types.GameDimension.Invalid)
                    .Select(d => new FilterOption(d.ToString(), FeedVars.Label(d.ToString())))),
            FeedFilter.Number(BuffCount, "Buff count")
        ],
        FilterSet.Empty,
        _ => FilterSet.Empty,
        ["identifier", "name", "description", "buffs", "contracts", "pageUrl"],
        [
            new FeedSample("new", "New colleggtible with a contract",
                new ColleggtibleAddedEvent("glacier", "Glacier Egg", "Cold, clear and very rare.",
                    [new ColleggtibleBuff(GameModifier.Types.GameDimension.ShippingCapacity, 1.05)],
                    ["hab-rush-2026"], SampleUrl))
        ]);
}
