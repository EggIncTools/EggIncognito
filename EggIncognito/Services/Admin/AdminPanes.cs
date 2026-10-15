namespace EggIncognito.Services.Admin;

public sealed record AdminPane(string Key, string Group, string Label, string? Meta = null);

public static class AdminPanes {
    public const string Traffic = "traffic";
    public const string Users = "users";
    public const string ApiKeys = "api-keys";
    public const string Notifications = "notifications";
    public const string GameData = "game-data";
    public const string Binaries = "binaries";
    public const string Events = "events";
    public const string Contracts = "contracts";
    public const string Tags = "tags";
    public const string Sessions = "sessions";
    public const string Console = "console";
    public const string Maintenance = "maintenance";

    public static readonly IReadOnlyList<AdminPane> All = [
        new(Traffic, "Overview", "Traffic", "Live request volume and recent calls"),
        new(Users, "Access", "Users", "Accounts, roles and bans"),
        new(ApiKeys, "Access", "API keys", "Issued keys and grants"),
        new(Notifications, "Access", "Notifications", "Webhook feed subscriptions"),
        new(GameData, "Data", "Game data", "Extracted game_data_documents"),
        new(Binaries, "Data", "Binaries", "Stored app binaries"),
        new(Tags, "Data", "Tags", "Content tagging rules"),
        new(Events, "Data", "Events", "Scheduled and live events"),
        new(Contracts, "Data", "Contracts", "Co-op contract catalog"),
        new(Sessions, "Ops", "Sessions", "Active user sessions"),
        new(Console, "Ops", "Console", "Raw endpoint console"),
        new(Maintenance, "Ops", "Maintenance", "Maintenance tools and jobs")
    ];
}
