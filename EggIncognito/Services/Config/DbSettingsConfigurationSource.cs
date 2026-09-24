using EggIdentity.Settings;
using EggIdentity.Settings.Store;
using Npgsql;

namespace EggIncognito.Services.Config;

public sealed class DbSettingsConfigurationSource(SettingsRegistry registry, string connectionString)
    : IConfigurationSource {
    public IConfigurationProvider Build(IConfigurationBuilder builder) =>
        new DbSettingsConfigurationProvider(registry, connectionString);
}

public sealed class DbSettingsConfigurationProvider(SettingsRegistry registry, string connectionString)
    : ConfigurationProvider {
    public override void Load() => Data = LoadSafely();

    private Dictionary<string, string?> LoadSafely() {
        var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        try {
            using var dataSource = new NpgsqlDataSourceBuilder(connectionString).Build();
            var store = new SettingsStore(dataSource, SecretProtector.FromEnvironment());
            store.MigrateAsync().GetAwaiter().GetResult();
            var stored = store.GetAllAsync().GetAwaiter().GetResult();

            foreach (var d in registry.All) {
                if (d.Tier == ApplyTier.Bootstrap) continue;
                if (!stored.TryGetValue(d.Key, out string? value) || string.IsNullOrEmpty(value)) continue;
                map[ConfigPath(d.EnvKey)] = value;
            }
        } catch (NpgsqlException) {
            return map;
        } catch (InvalidOperationException) {
            return map;
        }

        return map;
    }

    private static string ConfigPath(string envKey) => envKey.Replace("__", ":", StringComparison.Ordinal);
}
