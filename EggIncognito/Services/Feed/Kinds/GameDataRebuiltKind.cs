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
    string? INotificationEvent.Platform => null;

    public bool BinaryMoved => !string.Equals(BinaryVersion, PrevBinaryVersion, StringComparison.Ordinal);

    public string Summary =>
        $"game data {BinaryVersion} ({ChangedDocs.Count} doc{(ChangedDocs.Count == 1 ? "" : "s")})";

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
    private const string SampleSha = "4a17bc8f0402d1e6b8c3f95a2e7d40b1c6839fae";
    private const string SampleUrl = FeedDispatcher.DefaultPageBaseUrl + "/data";

    private static bool Rebuilt(INotificationEvent evt, Func<GameDataRebuiltEvent, bool> test) =>
        evt is GameDataRebuiltEvent g && g.ChangedDocs.Count > 0 && test(g);

    public static readonly NotificationKind Definition = new(
        FeedEventKinds.GameDataRebuilt, "Game data rebuilt",
        "Extracted game data documents were rebuilt from a game binary and at least one changed.",
        [
            new FeedTriggerOption(FeedEventKinds.TriggerAnyRebuild, "Any document changed", e => Rebuilt(e, _ => true)),
            new FeedTriggerOption(FeedEventKinds.TriggerBinaryUp, "New binary version", e => Rebuilt(e, g => g.BinaryMoved))
        ],
        FeedEventKinds.TriggerAnyRebuild,
        [],
        ["binaryVersion", "prevBinaryVersion", "platform", "changedDocs", "docCount", "inputSha", "pageUrl"],
        false,
        [
            new FeedSample("binary_up", "New binary, documents rebuilt",
                new GameDataRebuiltEvent("1.37.0", "1.36.4", "android", SampleSha,
                    ["eggs", "research", "habs", "missions"], SampleUrl)),
            new FeedSample("same_binary", "Same binary, one document changed",
                new GameDataRebuiltEvent("1.37.0", "1.37.0", "ios", SampleSha, ["boost-catalog"], SampleUrl))
        ],
        []);
}
