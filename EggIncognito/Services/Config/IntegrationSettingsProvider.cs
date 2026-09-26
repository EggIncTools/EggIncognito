using EggIdentity.Settings;

namespace EggIncognito.Services.Config;

public sealed class IntegrationSettingsProvider : ISettingsProvider {
    private const string Discord = "Discord";
    private const string Egress = "Egress";

    private static readonly IReadOnlyList<SettingDescriptor> Descriptors = [
        new("discord.bot_token", "Discord__BotToken", "Bot token", Discord,
            SettingKind.Secret, ApplyTier.RestartRequired, Sensitivity.Secret) {
            Description = "Empty disables the bot entirely."
        },
        new("discord.client_id", "Discord__ClientId", "Client id", Discord,
            SettingKind.Snowflake, ApplyTier.RestartRequired, Sensitivity.Plain),
        new("discord.guild_id", "Discord__GuildId", "Guild id", Discord,
            SettingKind.Snowflake, ApplyTier.RestartRequired, Sensitivity.Plain),
        new("discord.shared_role_id", "SHARED_ROLE_ID", "Shared role id", Discord,
            SettingKind.Snowflake, ApplyTier.RestartRequired, Sensitivity.Plain) {
            Description = "Role the bot self-assigns on ready. Shared with EggLedger in the same stack."
        },
        new("discord.dashboard_channel_id", "Discord__DashboardChannelId", "Dashboard channel id", Discord,
            SettingKind.Snowflake, ApplyTier.RestartRequired, Sensitivity.Plain),
        new("discord.invite_url", "Discord__InviteUrl", "Invite URL", Discord,
            SettingKind.Url, ApplyTier.RestartRequired, Sensitivity.Plain),

        new("sync_event.secret", "SyncEvent__EventSecret", "Sync ingest secret", Egress,
            SettingKind.Secret, ApplyTier.RestartRequired, Sensitivity.Secret) {
            Description = "Empty disables the sync ingest endpoint."
        },
        new("transport.api_salt", "EGG_INC_API_SALT", "Request signing salt", Egress,
            SettingKind.Secret, ApplyTier.RestartRequired, Sensitivity.Secret) {
            Description = "Unset disables request signing for Inspector live sends. Not required to boot."
        },
        new("sealed_proxy.upstream_url", "SealedProxy__UpstreamUrl", "Sealed proxy upstream", Egress,
            SettingKind.Url, ApplyTier.RestartRequired, Sensitivity.Plain) {
            Description = "Unset disables the sealed egress proxy."
        },
        new("sealed_proxy.username", "SealedProxy__Username", "Sealed proxy username", Egress,
            SettingKind.Secret, ApplyTier.RestartRequired, Sensitivity.Secret),
        new("sealed_proxy.password", "SealedProxy__Password", "Sealed proxy password", Egress,
            SettingKind.Secret, ApplyTier.RestartRequired, Sensitivity.Secret)
    ];

    public IReadOnlyList<SettingDescriptor> Describe() => Descriptors;
}
