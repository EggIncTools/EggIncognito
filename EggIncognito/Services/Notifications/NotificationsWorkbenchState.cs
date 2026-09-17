using EggIdentity.UI;
using EggIncognito.Services.Feed;

namespace EggIncognito.Services.Notifications;

public sealed class NotificationDraft {
    public string Url { get; set; } = "";
    public string EventKind { get; set; } = FeedEventKinds.ProtoBuild;
    public string Trigger { get; set; } = FeedEventKinds.Proto.DefaultTrigger;
    public bool Android { get; set; } = true;
    public bool Ios { get; set; } = true;
    public bool Active { get; set; } = true;
    public string MessageTemplate { get; set; } = "";
    public HashSet<string> Filters { get; set; } = [];
}

public sealed class NotificationsWorkbenchState : WorkbenchStateBase {
    public override IReadOnlyList<(string Key, string Label, int? Count)> Modes { get; } = [];

    public override string HashPrefix => "notify";

    public int? SelectedId { get; set; }
    public bool Creating { get; set; } = true;
    public NotificationDraft NewDraft { get; } = new();
    public Dictionary<int, NotificationDraft> Edits { get; } = [];

    public NotificationDraft Draft(int? id) =>
        id is { } key ? Edits.TryGetValue(key, out var d) ? d : Edits[key] = new NotificationDraft() : NewDraft;

    public NotificationDraft Active() => Creating ? NewDraft : Draft(SelectedId);

    public void ResetNew() {
        NewDraft.Url = "";
        NewDraft.EventKind = FeedEventKinds.ProtoBuild;
        NewDraft.Trigger = FeedEventKinds.Proto.DefaultTrigger;
        NewDraft.Android = true;
        NewDraft.Ios = true;
        NewDraft.Active = true;
        NewDraft.MessageTemplate = "";
        NewDraft.Filters = [];
    }

    public override string? Hash() {
        if (Creating) return "notify";
        return SelectedId is { } id ? $"notify_{id}" : "notify";
    }

    public override bool ApplyHash(string? hash) {
        (bool match, int? id) = ParseHash(hash);
        if (!match) return false;
        Creating = id is null;
        SelectedId = id;
        return true;
    }

    public static (bool Match, int? Id) ParseHash(string? hash) {
        string body = (hash ?? "").TrimStart('#');
        if (body.Length == 0) return (false, null);
        string[] parts = body.Split('_');
        if (parts[0] != "notify") return (false, null);
        if (parts.Length < 2) return (true, null);
        return int.TryParse(parts[1], out int id) ? (true, id) : (true, null);
    }
}
