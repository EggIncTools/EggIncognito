using EggIncognito.Data.Models;
using EggIncognito.Services.Auth;

namespace EggIncognito.Tests.Auth;

public sealed class ApiFeatureGateTests {
    [Fact]
    public void Admin_IsAllowed() => Assert.True(ApiFeatureGate.Evaluate(true, null, null).Allowed);

    [Fact]
    public void CookieWithoutKey_Is403() {
        var v = ApiFeatureGate.Evaluate(false, null, null);
        Assert.Equal((403, "api key required"), (v.Status, v.Error));
    }

    [Fact]
    public void KeyWithoutGrant_Is403None() {
        var v = ApiFeatureGate.Evaluate(false, 7, null);
        Assert.Equal((403, "none"), (v.Status, v.State));
    }

    [Theory]
    [InlineData(ApiKeyFeatureStates.Pending)]
    [InlineData(ApiKeyFeatureStates.Denied)]
    [InlineData(ApiKeyFeatureStates.Revoked)]
    public void UnapprovedGrant_Is403WithState(string state) {
        var v = ApiFeatureGate.Evaluate(false, 7, state);
        Assert.Equal((403, state), (v.Status, v.State));
    }

    [Fact]
    public void ApprovedGrant_IsAllowed() =>
        Assert.True(ApiFeatureGate.Evaluate(false, 7, ApiKeyFeatureStates.Approved).Allowed);
}
