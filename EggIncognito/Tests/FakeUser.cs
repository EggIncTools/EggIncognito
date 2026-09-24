using EggIdentity.Contract;
using EggIncognito.Services;

namespace EggIncognito.Tests;

public sealed class FakeUser(
    bool authenticated = true,
    UserRole role = UserRole.Viewer,
    string? discordId = "tester",
    bool supporter = false,
    Guid? userId = null) : ICurrentUser {
    public bool IsAuthenticated => authenticated;
    public Guid? UserId => authenticated ? userId : null;
    public string? DiscordId => authenticated ? discordId : null;
    public string? Username => authenticated ? "tester" : null;
    public string? Avatar => null;
    public string? AvatarUrl => null;
    public UserRole Role => role;
    public bool IsSupporter => authenticated && supporter;
    public bool IsAtLeast(UserRole need) => authenticated && UserRoles.IsAtLeast(role, need);
}
