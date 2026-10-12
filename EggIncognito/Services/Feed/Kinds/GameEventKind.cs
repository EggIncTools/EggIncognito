using System.Globalization;
using EggIdentity.Filters;
using EggIncognito.Data.Models;
using EggIncognito.Services.Events;
using Microsoft.EntityFrameworkCore;

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

    public double AgeHours => (SeenAt - Start).Duration().TotalHours;
    public bool Fresh => AgeHours <= FeedEventKinds.FreshWindow.TotalHours;
    public bool FromDevice => string.Equals(Source, GameEventSources.Device, StringComparison.Ordinal);
    public double DurationHours => (End - Start).TotalHours;

    public FilterSubjectValue Subject(string field) => field switch {
        GameEventKind.EventType => FilterSubjectValue.Of(EventType),
        GameEventKind.Ultra => FilterSubjectValue.Of(Ultra),
        GameEventKind.Multiplier => FilterSubjectValue.Of(Multiplier),
        GameEventKind.DurationHours => FilterSubjectValue.Of(DurationHours),
        GameEventKind.Source => FilterSubjectValue.Of(Source),
        GameEventKind.AgeHours => FilterSubjectValue.Of(AgeHours),
        _ => FilterSubjectValue.Absent
    };

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
    public const string EventType = "event_type";
    public const string Ultra = "ultra";
    public const string Multiplier = "multiplier";
    public const string DurationHours = "duration_hours";
    public const string Source = "source";
    public const string AgeHours = "age_hours";

    public static readonly FilterSet Defaults = FeedFilter.Rows(
        [FeedFilter.AtMost(AgeHours, FeedEventKinds.FreshWindow.TotalHours)],
        [FeedFilter.Is(Source, GameEventSources.Device)]);

    public static FilterSet Legacy(FeedSubscription sub) {
        var rows = new List<FilterCondition[]>();
        if (sub.Trigger == FeedEventKinds.TriggerUltraOnly) rows.Add([FeedFilter.Flag(Ultra, true)]);
        if (sub.Trigger == FeedEventKinds.TriggerStandardOnly) rows.Add([FeedFilter.Flag(Ultra, false)]);
        rows.AddRange(FeedEventKinds.IngestLegacyFilters(sub, AgeHours, Source, GameEventSources.Device));
        return FeedFilter.Rows(rows);
    }

    public static async Task<IReadOnlyList<FeedSample>> LoadSamples(FeedSampleContext ctx, CancellationToken ct) {
        var rows = await ctx.Db.GameEvents.AsNoTracking()
            .OrderByDescending(e => e.StartTime)
            .Take(FeedSampleSource.PerKind)
            .ToListAsync(ct);
        string pageUrl = ctx.Pages.PageUrl("events");
        return [.. rows.Select(r => new FeedSample(r.Id.ToString(CultureInfo.InvariantCulture),
            $"{r.Message} ({r.Source})", GameEventIngestor.ToEvent(r, r.FirstSeenAt ?? r.StartTime, pageUrl)))];
    }

    public static readonly NotificationKind Definition = new(
        FeedEventKinds.GameEvent, "Game event",
        "A game event appeared in the periodicals.",
        [
            FeedFilter.Text(EventType, "Event type"),
            FeedFilter.Bool(Ultra, "Ultra"),
            FeedFilter.Number(Multiplier, "Multiplier"),
            FeedFilter.Number(DurationHours, "Duration in hours"),
            FeedFilter.Select(Source, "Seen by", FeedFilter.Sources),
            FeedFilter.Number(AgeHours, "Hours between start and sighting")
        ],
        Defaults,
        Legacy,
        ["eventId", "eventType", "message", "ultra", "start", "end", "durationHours", "multiplier", "pageUrl"],
        LoadSamples);
}
