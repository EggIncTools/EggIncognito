namespace EggIncognito.Models.Notifications;

public record PreviewRow(
    string Key, string Label, string Event, bool Matches, List<string> BlockedBy, string? Body);
