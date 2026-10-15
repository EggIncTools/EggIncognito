using EggIdentity.Settings;

namespace EggIncognito.Services.Config;

public sealed class PlatformSettingsProvider : ISettingsProvider {
    private const string Data = "Data";
    private const string Web = "Web";
    private const string Assets = "Assets and decomp";
    private const string Predictions = "Predictions";

    private static readonly IReadOnlyList<SettingDescriptor> Descriptors = [
        new("gamedata.auto_rebuild.enabled", "GameData__AutoRebuild__Enabled", "Game data auto rebuild", Data,
            SettingKind.Bool, ApplyTier.RestartRequired, Sensitivity.Plain) { Default = "true" },
        new("gamedata.auto_rebuild.interval_minutes", "GameData__AutoRebuild__IntervalMinutes",
            "Game data rebuild interval (minutes)", Data,
            SettingKind.Number, ApplyTier.RestartRequired, Sensitivity.Plain) { Default = "5" },
        new("routes.auto_refresh.enabled", "Routes__AutoRefresh__Enabled", "Endpoint catalog auto refresh", Data,
            SettingKind.Bool, ApplyTier.RestartRequired, Sensitivity.Plain) { Default = "true" },
        new("routes.auto_refresh.interval_minutes", "Routes__AutoRefresh__IntervalMinutes",
            "Endpoint catalog refresh interval (minutes)", Data,
            SettingKind.Number, ApplyTier.RestartRequired, Sensitivity.Plain) { Default = "60" },
        new("contributions.enabled", "Contributions__Enabled", "Contributions enabled", Data,
            SettingKind.Bool, ApplyTier.RestartRequired, Sensitivity.Plain) { Default = "true" },
        new("contributions.max_recorded_per_user", "Contributions__MaxRecordedPerUser",
            "Max recorded per user", Data,
            SettingKind.Number, ApplyTier.RestartRequired, Sensitivity.Plain) { Default = "5000" },
        new("contributions.max_submitted_per_user", "Contributions__MaxSubmittedPerUser",
            "Max submitted per user", Data,
            SettingKind.Number, ApplyTier.RestartRequired, Sensitivity.Plain) { Default = "20000" },
        new("contributions.batch_size", "Contributions__BatchSize", "Contribution batch size", Data,
            SettingKind.Number, ApplyTier.RestartRequired, Sensitivity.Plain) { Default = "200" },

        new(SettingKeys.FeedPageBaseUrl, "Feed__PageBaseUrl", "Feed page base URL", Web,
            SettingKind.Url, ApplyTier.Live, Sensitivity.Plain) {
            Description = "Read per dispatch, so a change applies to the next notification."
        },
        new(SettingKeys.ThemeCustomCss, "Theme__CustomCss", "Allow custom theme CSS", Web,
            SettingKind.Bool, ApplyTier.Live, Sensitivity.Plain) {
            Default = "true", Description = "Read per request."
        },
        new(SettingKeys.ApiKeysMaxPerUser, "ApiKeys__MaxPerUser", "API keys per user", Web,
            SettingKind.Number, ApplyTier.Live, Sensitivity.Plain) {
            Default = "2", Description = "Read per request."
        },
        new("security.csp", "Security__Csp", "Content security policy mode", Web,
            SettingKind.Enum, ApplyTier.RestartRequired, Sensitivity.Plain) {
            EnumValues = ["off", "enforce"], Default = "off"
        },

        new("ship_assets.output_dir", "ShipAssets__OutputDir", "Ship assets directory", Assets,
            SettingKind.Path, ApplyTier.RestartRequired, Sensitivity.Plain),
        new("config_store.dir", "ConfigStore__Dir", "Config store directory", Assets,
            SettingKind.Path, ApplyTier.RestartRequired, Sensitivity.Plain) {
            Description = "Falls back to the ship assets directory when unset."
        },
        new("decomp.symbolized_ipa_dir", "Decomp__SymbolizedIpaDir", "Symbolized IPA directory", Assets,
            SettingKind.Path, ApplyTier.RestartRequired, Sensitivity.Plain) { Default = "captures/ipas" },
        new("decomp.binary_path", "Decomp__BinaryPath", "Decomp binary path", Assets,
            SettingKind.Path, ApplyTier.RestartRequired, Sensitivity.Plain) {
            Description = "Optional override. Extraction prefers the device-harvested binary for the version."
        },
        new("decomp.stripped_target_path", "Decomp__StrippedTargetPath", "Stripped target path", Assets,
            SettingKind.Path, ApplyTier.RestartRequired, Sensitivity.Plain),

        new(SettingKeys.PredictionsEventsMaxHorizonDays, "Predictions__Events__MaxHorizonDays",
            "Event projection horizon (days)", Predictions,
            SettingKind.Number, ApplyTier.Live, Sensitivity.Plain) {
            Default = "183", Description = "Read per watch request. Clamped 30..365."
        },
        new(SettingKeys.PredictionsContractsMaxHorizonWeeks, "Predictions__Contracts__MaxHorizonWeeks",
            "Contract slot projection horizon (weeks)", Predictions,
            SettingKind.Number, ApplyTier.Live, Sensitivity.Plain) {
            Default = "52", Description = "Read per watch request. Clamped 4..104."
        },
        new(SettingKeys.PredictionsHorizonStepDays, "Predictions__HorizonStepDays",
            "Precomputed watch step size (days)", Predictions,
            SettingKind.Number, ApplyTier.Live, Sensitivity.Plain) {
            Default = "30", Description = "Read at training job start."
        },
        new(SettingKeys.PredictionsRefreshHours, "Predictions__RefreshHours",
            "Prediction retrain interval (hours)", Predictions,
            SettingKind.Number, ApplyTier.Live, Sensitivity.Plain) {
            Default = "24", Description = "Read at training job start."
        }
    ];

    public IReadOnlyList<SettingDescriptor> Describe() => Descriptors;
}
