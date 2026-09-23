using EggIdentity.Settings;
using EggIncognito.Services.Config;

namespace EggIncognito.Tests.Config;

public class SettingsRegistryTests {
    private static readonly SettingsRegistry Registry = AppSettingsRegistry.Create();

    [Fact]
    public void Registry_BuildsWithoutDuplicateKeys() {
        Assert.NotEmpty(Registry.All);
        Assert.Equal(Registry.All.Count, Registry.All.Select(d => d.Key).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void EveryDescriptor_HasKeyEnvKeyAndLabel() {
        foreach (var d in Registry.All) {
            Assert.False(string.IsNullOrWhiteSpace(d.Key));
            Assert.False(string.IsNullOrWhiteSpace(d.EnvKey));
            Assert.False(string.IsNullOrWhiteSpace(d.Label));
            Assert.False(string.IsNullOrWhiteSpace(d.Category));
        }
    }

    [Fact]
    public void EnumDescriptors_DeclareTheirValues() {
        foreach (var d in Registry.All.Where(d => d.Kind == SettingKind.Enum))
            Assert.NotEmpty(d.EnumValues);
    }

    [Fact]
    public void EnumDefaults_AreAmongTheDeclaredValues() {
        foreach (var d in Registry.All.Where(d => d.Kind == SettingKind.Enum && d.Default is not null))
            Assert.Contains(d.Default, d.EnumValues, StringComparer.Ordinal);
    }

    [Fact]
    public void LiveTier_IsLimitedToTheKnownReReadSet() {
        string[] expected = [
            SettingKeys.ApiKeysMaxPerUser,
            SettingKeys.DeviceSyncAutoPublish,
            SettingKeys.DeviceSyncEnabled,
            SettingKeys.DeviceSyncRetryBackoffMinutes,
            SettingKeys.DeviceSyncStoreProbeIntervalMinutes,
            SettingKeys.FeedPageBaseUrl,
            SettingKeys.ThemeCustomCss,
            SettingKeys.VirtualImageOverride
        ];
        var actual = Registry.WithTier(ApplyTier.Live).Select(d => d.Key).Order(StringComparer.Ordinal);
        Assert.Equal(expected.Order(StringComparer.Ordinal), actual);
    }

    [Fact]
    public void BootstrapDescriptors_AreNotAdminEditable() {
        foreach (var d in Registry.WithTier(ApplyTier.Bootstrap))
            Assert.False(d.Editable);
    }

    [Theory]
    [InlineData("Decomp__LiveDevicePull")]
    [InlineData("DEPLOY_NOTIFY_SECRET")]
    [InlineData("DeviceCheck__Android__PollSeconds")]
    [InlineData("Devices__Virtual__Build__GappsUrl")]
    [InlineData("Devices__Virtual__Integrity__Modules__0__Name")]
    public void DeadAndOutOfScopeKeys_StayUnregistered(string envKey) =>
        Assert.DoesNotContain(Registry.All, d => string.Equals(d.EnvKey, envKey, StringComparison.Ordinal));
}
