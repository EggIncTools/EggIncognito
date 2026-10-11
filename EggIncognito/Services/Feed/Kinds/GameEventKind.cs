using EggIncognito.Services.Events;

namespace EggIncognito.Services.Feed.Kinds;

public sealed record GameEventAddedEvent(
    string EventId,
    string EventType,
    string Message,
    double Multiplier,
    bool Ultra,
    DateTimeOffset Start,
    DateTimeOffset End,
    string Source,
    DateTimeOffset SeenAt,
    string PageUrl) : INotificationEvent {
    public string Kind => FeedEventKinds.GameEvent;
    public string DedupKey => $"{EventId}:{FeedText.Unix(Start)}";
    public string? Platform => null;

    public bool Fresh => (SeenAt - Start).Duration() <= FeedEventKinds.FreshWindow;
    public bool FromDevice => string.Equals(Source, GameEventSources.Device, StringComparison.Ordinal);
    public double DurationHours => (End - Start).TotalHours;

    public string Summary => $"{(Ultra ? "ultra " : "")}{EventType} {FeedText.Stamp(Start)}";

    public IReadOnlyDictionary<string, string> Vars() => new Dictionary<string, string> {
        ["eventId"] = EventId,
        ["eventType"] = EventType,
        ["message"] = Message,
        ["ultra"] = Ultra ? "ultra" : "standard",
        ["start"] = FeedText.Stamp(Start),
        ["end"] = FeedText.Stamp(End),
        ["durationHours"] = FeedText.Number(DurationHours),
        ["multiplier"] = FeedText.Number(Multiplier),
        ["pageUrl"] = PageUrl
    };

    public FeedEmbed Embed() {
        var fields = new List<FeedEmbedField> {
            new("Type", EventType, true),
            new("Tier", Ultra ? "ultra" : "standard", true),
            new("Multiplier", FeedText.Number(Multiplier), true),
            new("Starts", FeedText.Stamp(Start), true),
            new("Ends", FeedText.Stamp(End), true),
            new("Duration", $"{FeedText.Number(DurationHours)} h", true)
        };
        if (Message.Length > 0) fields.Add(new FeedEmbedField("Message", Message, false));
        return new FeedEmbed($"Egg, Inc. event: {Message}", PageUrl, Ultra ? 0xd4a017 : 0x4fa3d1, fields);
    }
}

public static class GameEventKind {
    private static readonly DateTimeOffset SampleStart = new(2026, 10, 12, 16, 0, 0, TimeSpan.Zero);
    private const string SampleUrl = FeedDispatcher.DefaultPageBaseUrl + "/events";

    private static bool Added(INotificationEvent evt, Func<GameEventAddedEvent, bool> test) =>
        evt is GameEventAddedEvent g && test(g);

    private static GameEventAddedEvent Sample(string id, string type, string message, double multiplier, bool ultra,
        string source, TimeSpan age) =>
        new(id, type, message, multiplier, ultra, SampleStart, SampleStart.AddHours(72), source,
            SampleStart + age, SampleUrl);

    public static readonly NotificationKind Definition = new(
        FeedEventKinds.GameEvent, "Game event",
        "A game event first appeared in the periodicals: boosts, sales, research discounts and the like.",
        [
            new FeedTriggerOption(FeedEventKinds.TriggerAny, "Any event", e => Added(e, _ => true)),
            new FeedTriggerOption(FeedEventKinds.TriggerUltraOnly, "Ultra events only", e => Added(e, g => g.Ultra)),
            new FeedTriggerOption(FeedEventKinds.TriggerStandardOnly, "Standard events only", e => Added(e, g => !g.Ultra))
        ],
        FeedEventKinds.TriggerAny,
        [
            new FeedFilterOption(FeedEventKinds.FilterFreshOnly, "Require a current event", true,
                e => e is GameEventAddedEvent { Fresh: false }),
            new FeedFilterOption(FeedEventKinds.FilterDeviceOnly, "Require a device observation", true,
                e => e is GameEventAddedEvent { FromDevice: false })
        ],
        ["eventId", "eventType", "message", "ultra", "start", "end", "durationHours", "multiplier", "pageUrl"],
        false,
        [
            new FeedSample("boost", "Standard event seen live",
                Sample("egg-boost-2026-10-12", "boost-sale", "Boost sale, 30% off", 0.7, false,
                    GameEventSources.Device, TimeSpan.FromMinutes(5))),
            new FeedSample("ultra", "Ultra event seen live",
                Sample("ultra-research-2026-10-12", "research-sale", "Ultra research sale, 60% off", 0.4, true,
                    GameEventSources.Device, TimeSpan.FromMinutes(5))),
            new FeedSample("backfill", "Old event from a history import",
                Sample("egg-boost-2025-01-05", "boost-sale", "Boost sale, 30% off", 0.7, false,
                    GameEventSources.Carpet, TimeSpan.FromDays(640)))
        ],
        []);
}
