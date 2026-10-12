using EggIdentity.Filters;
using EggIncognito.Data.Models;

namespace EggIncognito.Services.Feed.Kinds;

public sealed record GameDataRebuiltEvent(
    string BinaryVersion,
    string? PrevBinaryVersion,
    string Platform,
    string InputSha,
    IReadOnlyList<string> ChangedDocs,
    string PageUrl) : INotificationEvent {
    public string Kind => FeedEventKinds.GameDataRebuilt;
    public string DedupKey => InputSha;

    public bool BinaryMoved => !string.Equals(BinaryVersion, PrevBinaryVersion, StringComparison.Ordinal);

    public string Summary =>
        $"game data {BinaryVersion} ({ChangedDocs.Count} doc{(ChangedDocs.Count == 1 ? "" : "s")})";

    public FilterSubjectValue Subject(string field) => field switch {
        GameDataRebuiltKind.Platform => FilterSubjectValue.Of(Platform),
        GameDataRebuiltKind.BinaryVersionField => FilterSubjectValue.Of(BinaryVersion),
        GameDataRebuiltKind.BinaryMovedField => FilterSubjectValue.Of(BinaryMoved),
        GameDataRebuiltKind.Docs => FilterSubjectValue.Of(ChangedDocs),
        GameDataRebuiltKind.DocCount => FilterSubjectValue.Of((double)ChangedDocs.Count),
        _ => FilterSubjectValue.Absent
    };

    public IReadOnlyDictionary<string, string> Vars() => new Dictionary<string, string> {
        ["binaryVersion"] = BinaryVersion,
        ["prevBinaryVersion"] = PrevBinaryVersion ?? "",
        ["platform"] = Platform,
        ["changedDocs"] = FeedText.Joined(ChangedDocs),
        ["docCount"] = FeedText.Count(ChangedDocs.Count),
        ["inputSha"] = InputSha,
        ["pageUrl"] = PageUrl
    };

    public FeedEmbed Embed() {
        var fields = new List<FeedEmbedField> {
            new("Binary", FeedText.Or(BinaryVersion, "unknown"), true),
            new("Platform", FeedText.Or(Platform, "unknown"), true),
            new("Documents", FeedText.Count(ChangedDocs.Count), true)
        };
        if (!string.IsNullOrEmpty(PrevBinaryVersion) && BinaryMoved)
            fields.Add(new FeedEmbedField("Previous", PrevBinaryVersion, true));
        if (ChangedDocs.Count > 0) fields.Add(new FeedEmbedField("Changed", FeedText.Listed(ChangedDocs), false));
        return new FeedEmbed(
            $"Egg, Inc. game data rebuilt from {FeedText.Or(BinaryVersion, "an unknown binary")}",
            PageUrl, 0x3fa06a, fields);
    }
}

public static class GameDataRebuiltKind {
    public const string Platform = "platform";
    public const string BinaryVersionField = "binary_version";
    public const string BinaryMovedField = "binary_moved";
    public const string Docs = "docs";
    public const string DocCount = "doc_count";

    private const string SampleSha = "4a17bc8f0402d1e6b8c3f95a2e7d40b1c6839fae";
    private const string SampleUrl = FeedDispatcher.DefaultPageBaseUrl + "/data";

    public static readonly FilterSet Defaults = FeedFilter.Rows([FeedFilter.Above(DocCount, 0)]);

    public static FilterSet Legacy(FeedSubscription sub) =>
        sub.Trigger == FeedEventKinds.TriggerBinaryUp
            ? FeedFilter.Rows([FeedFilter.Above(DocCount, 0)], [FeedFilter.Flag(BinaryMovedField, true)])
            : Defaults;

    public static readonly NotificationKind Definition = new(
        FeedEventKinds.GameDataRebuilt, "Game data rebuilt",
        "Extracted game data documents were rebuilt from a game binary and at least one changed.",
        [
            FeedFilter.Select(Platform, "Platform", FeedFilter.Options("android", "ios")),
            FeedFilter.Version(BinaryVersionField, "Binary version"),
            FeedFilter.Bool(BinaryMovedField, "New binary version"),
            FeedFilter.Set(Docs, "Changed document",
                EggIncognito.GameData.GameDataProvider.ImportableIds.Select(id => new FilterOption(id, id))),
            FeedFilter.Number(DocCount, "Changed document count")
        ],
        Defaults,
        Legacy,
        ["binaryVersion", "prevBinaryVersion", "platform", "changedDocs", "docCount", "inputSha", "pageUrl"],
        [
            new FeedSample("binary_up", "New binary, documents rebuilt",
                new GameDataRebuiltEvent("1.37.0", "1.36.4", "android", SampleSha,
                    ["eggs", "research", "habs", "missions"], SampleUrl)),
            new FeedSample("same_binary", "Same binary, one document changed",
                new GameDataRebuiltEvent("1.37.0", "1.37.0", "ios", SampleSha, ["boost-catalog"], SampleUrl))
        ]);
}
