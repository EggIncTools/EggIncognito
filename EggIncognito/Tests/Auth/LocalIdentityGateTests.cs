using EggIdentity.Contract;
using EggIncognito.Services;
using EggIncognito.Services.Auth;
using Microsoft.Extensions.Configuration;

namespace EggIncognito.Tests.Auth;

public class LocalIdentityGateTests {
    private static IConfiguration Cfg(string? enabled, string? role = null, string? supporter = null) {
        Dictionary<string, string?> values = [];
        if (enabled is not null) values[LocalIdentityGate.EnabledKey] = enabled;
        if (role is not null) values[LocalIdentityGate.RoleKey] = role;
        if (supporter is not null) values[LocalIdentityGate.SupporterKey] = supporter;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    [Fact]
    public void IsOn_OnlyForStagingLocalExplicitTrueAndNoRealAuth() =>
        Assert.True(LocalIdentityGate.IsOn("Staging", AppMode.Local, Cfg("true"), identityConfigured: false));

    [Fact]
    public void IsOn_False_WhenKeyAbsent() =>
        Assert.False(LocalIdentityGate.IsOn("Staging", AppMode.Local, Cfg(null), identityConfigured: false));

    [Fact]
    public void IsOn_False_WhenKeyIsFalse() =>
        Assert.False(LocalIdentityGate.IsOn("Staging", AppMode.Local, Cfg("false"), identityConfigured: false));

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    [InlineData("")]
    [InlineData("staging-2")]
    [InlineData("SubProd")]
    public void IsOn_False_ForAnyEnvironmentThatIsNotStaging(string environment) =>
        Assert.False(LocalIdentityGate.IsOn(environment, AppMode.Local, Cfg("true"), identityConfigured: false));

    [Fact]
    public void IsOn_StagingIsCaseInsensitive() =>
        Assert.True(LocalIdentityGate.IsOn("staging", AppMode.Local, Cfg("true"), identityConfigured: false));

    [Fact]
    public void IsOn_False_WhenModeIsHosted() =>
        Assert.False(LocalIdentityGate.IsOn("Staging", AppMode.Hosted, Cfg("true"), identityConfigured: false));

    [Fact]
    public void IsOn_False_WhenRealAuthIsConfigured() =>
        Assert.False(LocalIdentityGate.IsOn("Staging", AppMode.Local, Cfg("true"), identityConfigured: true));

    [Fact]
    public void Guard_Throws_ForProductionWithTheKeySet() {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            LocalIdentityGate.Guard("Production", AppMode.Local, Cfg("true")));
        Assert.Contains(LocalIdentityGate.EnabledKey, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Guard_Throws_ForHostedWithTheKeySet() {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            LocalIdentityGate.Guard("Staging", AppMode.Hosted, Cfg("true")));
        Assert.Contains(LocalIdentityGate.EnabledKey, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Guard_Throws_ForProductionHosted() =>
        Assert.Throws<InvalidOperationException>(() =>
            LocalIdentityGate.Guard("Production", AppMode.Hosted, Cfg("true")));

    [Fact]
    public void Guard_Silent_WhenTheKeyIsAbsent() {
        LocalIdentityGate.Guard("Production", AppMode.Hosted, Cfg(null));
        LocalIdentityGate.Guard("Production", AppMode.Hosted, Cfg("false"));
    }

    [Fact]
    public void Settings_DefaultToAdminSupporter() {
        var s = LocalIdentitySettings.Bind(Cfg("true"));
        Assert.Equal(UserRole.Admin, s.Role);
        Assert.True(s.Supporter);
        Assert.Equal("admin", s.RoleName);
        Assert.Equal("local-admin", s.Username);
    }

    [Theory]
    [InlineData("viewer", UserRole.Viewer)]
    [InlineData("contributor", UserRole.Contributor)]
    [InlineData("admin", UserRole.Admin)]
    [InlineData("Admin", UserRole.Admin)]
    public void Settings_RoleIsSelectable(string role, UserRole expected) =>
        Assert.Equal(expected, LocalIdentitySettings.Bind(Cfg("true", role)).Role);

    [Fact]
    public void Settings_UnknownRoleFallsBackToViewer() =>
        Assert.Equal(UserRole.Viewer, LocalIdentitySettings.Bind(Cfg("true", "superuser")).Role);

    [Fact]
    public void Settings_SupporterCanBeTurnedOff() =>
        Assert.False(LocalIdentitySettings.Bind(Cfg("true", null, "false")).Supporter);

    [Theory]
    [InlineData("appsettings.json")]
    [InlineData("appsettings.Development.json")]
    public void ShippedAppSettings_NeverDeclareTheLocalIdentity(string file) {
        string path = Path.Combine(TestPaths.WebProjectDir(), file);
        Assert.True(File.Exists(path), path);
        string json = File.ReadAllText(path);
        Assert.DoesNotContain("LocalIdentity", json, StringComparison.OrdinalIgnoreCase);
    }
}
