using System.Collections;

namespace EggIncognito.Tests;

public class AmbientEnvironmentTests {
    [Fact]
    public void NoAppConfigurationKey_SurvivesInTheProcessEnvironment() {
        var leaked = new List<string>();
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables()) {
            if (entry.Key is string name && TestHostInit.IsAmbientAppConfig(name)) leaked.Add(name);
        }

        Assert.True(leaked.Count == 0,
            "ambient environment reached the test host, so results depend on the developer's shell: " +
            string.Join(", ", leaked.Order()));
    }

    [Theory]
    [InlineData("Devices__Fake__Enabled")]
    [InlineData("Devices:Fake:Enabled")]
    [InlineData("Auth__LocalIdentity__Enabled")]
    [InlineData("Identity__ApiSecret")]
    [InlineData("Discord__BotToken")]
    [InlineData("DeviceSync__Enabled")]
    [InlineData("DeviceProbe__TimeoutSeconds")]
    [InlineData("Capture__AddressSecret")]
    [InlineData("EGG_INC_API_SALT")]
    [InlineData("EGGIDENTITY_SESSION_SECRET")]
    [InlineData("AppMode")]
    [InlineData("HostedCaptureEnabled")]
    [InlineData("ASPNETCORE_ENVIRONMENT")]
    [InlineData("ASPNETCORE_URLS")]
    [InlineData("DOTNET_ENVIRONMENT")]
    public void KnownAmbientKey_IsRecognised(string name) => Assert.True(TestHostInit.IsAmbientAppConfig(name));

    [Theory]
    [InlineData("PATH")]
    [InlineData("ASPNETCORE_TEST_CONTENTROOT_EGGINCOGNITO_TESTS")]
    [InlineData("DOTNET_ROOT")]
    [InlineData("EGGINCOGNITO_TEST_DBFREE")]
    [InlineData("=C:")]
    [InlineData("")]
    public void UnrelatedVariable_IsLeftAlone(string name) => Assert.False(TestHostInit.IsAmbientAppConfig(name));
}
