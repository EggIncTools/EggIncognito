using EggIncognito.Data.Models;

namespace EggIncognito.Tests.Auth;

public sealed class ApiKeyFeatureStatesTests {
    [Theory]
    [InlineData("approve", ApiKeyFeatureStates.Approved)]
    [InlineData("deny", ApiKeyFeatureStates.Denied)]
    [InlineData("revoke", ApiKeyFeatureStates.Revoked)]
    public void ForDecision_MapsVerbs(string verb, string state) =>
        Assert.Equal(state, ApiKeyFeatureStates.ForDecision(verb));

    [Fact]
    public void ForDecision_UnknownVerb_IsNull() => Assert.Null(ApiKeyFeatureStates.ForDecision("grant"));

    [Theory]
    [InlineData(ApiKeyFeatureStates.Pending, ApiKeyFeatureStates.Approved, true)]
    [InlineData(ApiKeyFeatureStates.Pending, ApiKeyFeatureStates.Denied, true)]
    [InlineData(ApiKeyFeatureStates.Approved, ApiKeyFeatureStates.Revoked, true)]
    [InlineData(ApiKeyFeatureStates.Pending, ApiKeyFeatureStates.Revoked, false)]
    [InlineData(ApiKeyFeatureStates.Approved, ApiKeyFeatureStates.Denied, false)]
    [InlineData(ApiKeyFeatureStates.Denied, ApiKeyFeatureStates.Approved, false)]
    [InlineData(ApiKeyFeatureStates.Revoked, ApiKeyFeatureStates.Approved, false)]
    public void CanDecide(string current, string target, bool expected) =>
        Assert.Equal(expected, ApiKeyFeatureStates.CanDecide(current, target));

    [Theory]
    [InlineData(null, true)]
    [InlineData(ApiKeyFeatureStates.Denied, true)]
    [InlineData(ApiKeyFeatureStates.Revoked, true)]
    [InlineData(ApiKeyFeatureStates.Pending, true)]
    [InlineData(ApiKeyFeatureStates.Approved, false)]
    public void Requestable(string? current, bool expected) =>
        Assert.Equal(expected, ApiKeyFeatureStates.Requestable(current));
}
