namespace EggIncognito.Services;

public sealed record AuthState(
    bool IdentityApiEnabled,
    string? IdentityHostUrl = null,
    string SessionCookieName = "eggidentity_session",
    bool LocalIdentityActive = false,
    bool SessionActive = false) {
    public bool WidgetEnabled =>
        IdentityApiEnabled && SessionActive && !string.IsNullOrWhiteSpace(IdentityHostUrl);

    public bool Enabled => WidgetEnabled;
}
