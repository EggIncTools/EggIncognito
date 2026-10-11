using EggIncognito.Services.Contracts;

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
    public string? Platform => null;

    public bool Fresh => (SeenAt - Start).Duration() <= FeedEventKinds.FreshWindow;
    public bool FromDevice => string.Equals(Source, ContractSources.Device, StringComparison.Ordinal);
    public double LengthDays => LengthSeconds / 86400d;
    public string EggLabel => CustomEggId ?? ((Ei.Egg)Egg).ToString();

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
    private static readonly DateTimeOffset SampleStart = new(2026, 10, 12, 16, 0, 0, TimeSpan.Zero);
    private const string SampleUrl = FeedDispatcher.DefaultPageBaseUrl + "/contracts";

    private static bool Released(INotificationEvent evt, Func<ContractReleasedEvent, bool> test) =>
        evt is ContractReleasedEvent c && test(c);

    private static ContractReleasedEvent Sample(string id, string name, int egg, bool leggacy, bool ultra,
        int prophecy, string source, TimeSpan age) =>
        new(id, name, egg, null, SampleStart, SampleStart.AddDays(10), 10 * 86400d, leggacy, ultra,
            prophecy, 10, source, SampleStart + age, SampleUrl);

    public static readonly NotificationKind Definition = new(
        FeedEventKinds.ContractRelease, "Contract release",
        "A contract went live in the periodicals, whether a first release or a leggacy run.",
        [
            new FeedTriggerOption(FeedEventKinds.TriggerAny, "Any release", e => Released(e, _ => true)),
            new FeedTriggerOption(FeedEventKinds.TriggerNewOnly, "First releases only", e => Released(e, c => !c.Leggacy)),
            new FeedTriggerOption(FeedEventKinds.TriggerLeggacyOnly, "Leggacies only", e => Released(e, c => c.Leggacy)),
            new FeedTriggerOption(FeedEventKinds.TriggerUltraOnly, "Ultra only releases", e => Released(e, c => c.UltraOnly))
        ],
        FeedEventKinds.TriggerAny,
        [
            new FeedFilterOption(FeedEventKinds.FilterFreshOnly, "Require a current release", true,
                e => e is ContractReleasedEvent { Fresh: false }),
            new FeedFilterOption(FeedEventKinds.FilterDeviceOnly, "Require a device observation", true,
                e => e is ContractReleasedEvent { FromDevice: false })
        ],
        [
            "contractId", "name", "egg", "leggacy", "ultra", "start", "end", "lengthDays", "prophecyEggs",
            "maxCoopSize", "pageUrl"
        ],
        false,
        [
            new FeedSample("new", "New contract seen live",
                Sample("hab-rush-2026", "Hab Rush", (int)Ei.Egg.Fusion, false, false, 1,
                    ContractSources.Device, TimeSpan.FromMinutes(5))),
            new FeedSample("leggacy", "Leggacy run seen live",
                Sample("winter-warmup-2024", "Winter Warmup", (int)Ei.Egg.Tachyon, true, false, 0,
                    ContractSources.Device, TimeSpan.FromMinutes(5))),
            new FeedSample("ultra", "Ultra only leggacy seen live",
                Sample("sweet-dreams-2023", "Sweet Dreams", (int)Ei.Egg.Dilithium, true, true, 1,
                    ContractSources.Device, TimeSpan.FromMinutes(5))),
            new FeedSample("backfill", "Old release from a history import",
                Sample("hab-rush-2023", "Hab Rush", (int)Ei.Egg.Fusion, false, false, 1,
                    ContractSources.Carpet, TimeSpan.FromDays(900)))
        ],
        []);
}
