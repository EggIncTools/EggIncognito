namespace EggIncognito.Services.Admin;

public sealed record AdminPane(string Key, string Group, string Label);

public static class AdminPanes {
    public const string Traffic = "traffic";
    public const string Users = "users";
    public const string ApiKeys = "api-keys";
    public const string Notifications = "notifications";
    public const string ThemePolicy = "theme-policy";
    public const string DataStatus = "data-status";
    public const string Binaries = "binaries";
    public const string GameData = "game-data";
    public const string Events = "events";
    public const string Contracts = "contracts";
    public const string Apks = "apks";
    public const string Tags = "tags";
    public const string Staged = "staged";
    public const string Contributions = "contributions";
    public const string Sessions = "sessions";
    public const string Console = "console";
    public const string BotConfig = "bot-config";
    public const string Maintenance = "maintenance";
    public const string Deploy = "deploy";
    public const string Settings = "settings";
    public const string Playground = "playground";

    public static readonly IReadOnlyList<AdminPane> All = [
        new(Traffic, "Overview", "Traffic"),
        new(Users, "Access", "Users"),
        new(ApiKeys, "Access", "API keys"),
        new(Notifications, "Access", "Notifications"),
        new(ThemePolicy, "Access", "Theme policy"),
        new(DataStatus, "Data", "Data status"),
        new(Binaries, "Data", "Binaries"),
        new(Apks, "Data", "APKs"),
        new(Tags, "Data", "Tags"),
        new(GameData, "Data", "Game data"),
        new(Events, "Data", "Events"),
        new(Contracts, "Data", "Contracts"),
        new(Staged, "Data", "Staged"),
        new(Contributions, "Data", "Contributions"),
        new(Sessions, "Ops", "Sessions"),
        new(Console, "Ops", "Console"),
        new(BotConfig, "Ops", "Bot config"),
        new(Maintenance, "Ops", "Maintenance"),
        new(Deploy, "Ops", "Deploy"),
        new(Settings, "Ops", "Settings"),
        new(Playground, "Ops", "Playground")
    ];
}
