using EggIdentity.Settings;

namespace EggIncognito.Services.Config;

public sealed class BootstrapSettingsProvider : ISettingsProvider {
    private const string Host = "Host";
    private const string Paths = "Paths";
    private const string Identity = "Identity";
    private const string Storage = "Storage";

    private static readonly IReadOnlyList<SettingDescriptor> Descriptors = [
        new("database.postgres", "ConnectionStrings__Postgres", "Postgres connection", Host,
            SettingKind.Secret, ApplyTier.Bootstrap, Sensitivity.Secret) {
            Description = "Empty runs the app database-free. Read before DI exists."
        },
        new("app.mode", "AppMode", "App mode", Host, SettingKind.Enum, ApplyTier.Bootstrap, Sensitivity.Plain) {
            EnumValues = ["Local", "Hosted"], Default = "Local",
            Description = "Steers the whole boot shape, including auth and capture gating."
        },
        new("host.aspnetcore_urls", "ASPNETCORE_URLS", "Listen URLs", Host,
            SettingKind.Text, ApplyTier.Bootstrap, Sensitivity.Plain),
        new("host.environment", "ASPNETCORE_ENVIRONMENT", "Environment name", Host,
            SettingKind.Text, ApplyTier.Bootstrap, Sensitivity.Plain),
        new("host.http_port", "HttpPort", "HTTP port", Host,
            SettingKind.Number, ApplyTier.Bootstrap, Sensitivity.Plain) {
            Default = "8080", Description = "Only bound when a TLS cert pair exists at the certs path."
        },
        new("host.https_port", "HttpsPort", "HTTPS port", Host,
            SettingKind.Number, ApplyTier.Bootstrap, Sensitivity.Plain) { Default = "8443" },
        new("host.no_browser", "NoBrowser", "Suppress browser launch", Host,
            SettingKind.Bool, ApplyTier.Bootstrap, Sensitivity.Plain) { Default = "false" },
        new("build.git_sha", "GIT_SHA", "Build commit", Host,
            SettingKind.ReadOnly, ApplyTier.Bootstrap, Sensitivity.Plain),

        new("paths.content_root", "ContentRoot", "Content root", Paths,
            SettingKind.Path, ApplyTier.Bootstrap, Sensitivity.Plain) {
            Description = "Base for captures, config store and asset paths. Defaults to the app base directory."
        },
        new("paths.endpoints", "EndpointsPath", "Endpoints directory", Paths,
            SettingKind.Path, ApplyTier.Bootstrap, Sensitivity.Plain) { Default = "Endpoints" },
        new("paths.certs", "CertsPath", "Certs directory", Paths,
            SettingKind.Path, ApplyTier.Bootstrap, Sensitivity.Plain) { Default = "certs" },
        new("paths.logs", "LogsPath", "Logs directory", Paths,
            SettingKind.Path, ApplyTier.Bootstrap, Sensitivity.Plain) { Default = "logs" },
        new("paths.routes_yaml", "RoutesYamlPath", "routes.yaml path", Paths,
            SettingKind.Path, ApplyTier.Bootstrap, Sensitivity.Plain) { Default = "routes.yaml" },

        new(SettingKeys.BlobOffloadEnabled, "Storage__BlobOffloadEnabled", "Move blobs to disk", Storage,
            SettingKind.Bool, ApplyTier.RestartRequired, Sensitivity.Plain) {
            Default = "false",
            Description =
                "Moves device assets, build blobs, binaries, APKs and modules out of Postgres into "
                + "{content root}/blobs, one row at a time. Resumable; rows already on disk are read from there "
                + "either way. Off until a first pass has been watched deliberately."
        },

        new("identity.api_url", "Identity__ApiUrl", "Identity API URL", Identity,
            SettingKind.Url, ApplyTier.Bootstrap, Sensitivity.Plain),
        new("identity.api_secret", "Identity__ApiSecret", "Identity API secret", Identity,
            SettingKind.Secret, ApplyTier.Bootstrap, Sensitivity.Secret),
        new("identity.widget_url", "Identity__WidgetUrl", "Identity widget URL", Identity,
            SettingKind.Url, ApplyTier.Bootstrap, Sensitivity.Plain),
        new("admin.api_secret", "ADMIN_API_SECRET", "Admin API secret", Identity,
            SettingKind.Secret, ApplyTier.Bootstrap, Sensitivity.Secret) {
            Description = "Bearer the EggIncTools hub presents to administer this app. Empty disables the surface."
        },
        new("session.secret", "EGGIDENTITY_SESSION_SECRET", "Session secret", Identity,
            SettingKind.Secret, ApplyTier.Bootstrap, Sensitivity.Secret),
        new("session.cookie_domain", "EGGIDENTITY_SESSION_COOKIE_DOMAIN", "Session cookie domain", Identity,
            SettingKind.Text, ApplyTier.Bootstrap, Sensitivity.Plain),
        new("auth.local_identity_enabled", "Auth__LocalIdentity__Enabled", "Local identity", Identity,
            SettingKind.Bool, ApplyTier.Bootstrap, Sensitivity.Plain) {
            Default = "false",
            Description = "Development shortcut. The gate refuses Production and refuses AppMode=Hosted."
        },
        new("auth.local_identity_role", "Auth__LocalIdentity__Role", "Local identity role", Identity,
            SettingKind.Text, ApplyTier.Bootstrap, Sensitivity.Plain) { Default = "Admin" },
        new("auth.local_identity_supporter", "Auth__LocalIdentity__Supporter", "Local identity supporter", Identity,
            SettingKind.Bool, ApplyTier.Bootstrap, Sensitivity.Plain) { Default = "true" }
    ];

    public IReadOnlyList<SettingDescriptor> Describe() => Descriptors;
}
