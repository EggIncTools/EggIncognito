using EggIncognito.Data.Models;

namespace EggIncognito.Services.Feed;

public interface INotificationEvent {
    string Kind { get; }
    string DedupKey { get; }
    string Summary { get; }
    string? Platform { get; }
    IReadOnlyDictionary<string, string> Vars();
    FeedEmbed Embed();
}

public sealed record FeedEmbedField(string Name, string Value, bool Inline);

public sealed record FeedEmbed(string Title, string Url, int Color, IReadOnlyList<FeedEmbedField> Fields);

public sealed record FeedTriggerOption(string Value, string Label, Func<INotificationEvent, bool> Matches);

public sealed record FeedFilterOption(string Key, string Label, bool DefaultOn, Func<INotificationEvent, bool> Blocks);

public sealed record FeedSample(string Key, string Label, INotificationEvent Event);

public sealed record NotificationKind(
    string Key,
    string Label,
    string Description,
    IReadOnlyList<FeedTriggerOption> Triggers,
    string DefaultTrigger,
    IReadOnlyList<FeedFilterOption> Filters,
    IReadOnlyList<string> Vars,
    bool PlatformScoped,
    IReadOnlyList<FeedSample> Samples,
    IReadOnlyList<string> BypassFilters) {
    public FeedTriggerOption? Trigger(string? value) => Triggers.FirstOrDefault(t => t.Value == value);

    public bool Matches(INotificationEvent evt, FeedSubscription sub) {
        if (!string.Equals(evt.Kind, Key, StringComparison.Ordinal)) return false;
        if (PlatformScoped && (evt.Platform is null || !sub.Platforms.Contains(evt.Platform))) return false;
        return Trigger(sub.Trigger)?.Matches(evt) ?? false;
    }

    public IReadOnlyList<string> BlockedBy(INotificationEvent evt, FeedSubscription sub) {
        if (BypassFilters.Contains(sub.Trigger, StringComparer.Ordinal)) return [];
        return [.. Filters
            .Where(f => sub.Filters.Contains(f.Key, StringComparer.Ordinal) && f.Blocks(evt))
            .Select(f => f.Key)];
    }
}
