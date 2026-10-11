namespace EggIncognito.Services.Admin;

public sealed record AdminPane(string Key, string Group, string Label);

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
        new(Traffic, "Overview", "Traffic"),
        new(Users, "Access", "Users"),
        new(ApiKeys, "Access", "API keys"),
        new(Notifications, "Access", "Notifications"),
        new(GameData, "Data", "Game data"),
        new(Binaries, "Data", "Binaries"),
        new(Tags, "Data", "Tags"),
        new(Events, "Data", "Events"),
        new(Contracts, "Data", "Contracts"),
        new(Sessions, "Ops", "Sessions"),
        new(Console, "Ops", "Console"),
        new(Maintenance, "Ops", "Maintenance")
    ];
}
