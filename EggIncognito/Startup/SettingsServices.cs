using EggIdentity.Settings;
using EggIdentity.Settings.Store;
using EggIncognito.Services.Config;
using Npgsql;

namespace EggIncognito.Startup;

public static class SettingsServices {
    public static SettingsRegistry AddDbBackedSettings(this WebApplicationBuilder builder) {
        var registry = AppSettingsRegistry.Create();
        string? conn = builder.Configuration.GetConnectionString("Postgres");
        if (!string.IsNullOrWhiteSpace(conn))
            ((IConfigurationBuilder)builder.Configuration).Add(new DbSettingsConfigurationSource(registry, conn));
        return registry;
    }

    public static void AddAppSettingsFramework(
        this WebApplicationBuilder builder, BootFlags boot, SettingsRegistry registry) {
        builder.Services.AddSingleton(registry);

        if (!boot.DbEnabled) return;

        builder.Services.AddSingleton(_ => new NpgsqlDataSourceBuilder(boot.PgConn).Build());
        builder.Services.AddSingleton(sp => new SettingsStore(
            sp.GetRequiredService<NpgsqlDataSource>(), SecretProtector.FromEnvironment()));
        builder.Services.AddSingleton(sp => new SettingsCache(
            sp.GetRequiredService<SettingsRegistry>(),
            sp.GetRequiredService<SettingsStore>(),
            IndexedEnvLookup.For(sp.GetRequiredService<SettingsRegistry>(), sp.GetRequiredService<IConfiguration>())));
        builder.Services.AddSingleton(sp => new SettingsChangeListener(
            sp.GetRequiredService<NpgsqlDataSource>(), sp.GetRequiredService<SettingsCache>()));
        builder.Services.AddSingleton<SettingsAdminService>();
        builder.Services.AddHostedService<SettingsBootstrapService>();
    }
}
