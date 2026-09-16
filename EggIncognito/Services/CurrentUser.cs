using System.Security.Claims;
using EggIdentity.Auth;
using EggIdentity.Contract;
using EggIncognito.Data.Services;

namespace EggIncognito.Services;

public sealed class CurrentUser(IHttpContextAccessor accessor, AuthState authState) : ICurrentUser {
    private const string SessionSub = "sub";

    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    public Guid? UserId => IsAuthenticated && Guid.TryParse(Find(AuthClaims.UserIdClaim, SessionSub), out var id)
        ? id
        : null;

    public string? DiscordId => IsAuthenticated ? Find(ClaimTypes.NameIdentifier, SessionClaims.DiscordId) : null;
    public string? Username => IsAuthenticated ? Find(ClaimTypes.Name, SessionClaims.Name) : null;
    public string? Avatar => IsAuthenticated ? Find("urn:discord:avatar:hash", SessionClaims.Avatar) : null;

    public string? AvatarUrl => Avatar switch {
        null or "" => null,
        var a when a.StartsWith("http://", StringComparison.Ordinal) ||
                   a.StartsWith("https://", StringComparison.Ordinal) ||
                   a.StartsWith("data:", StringComparison.Ordinal) => a,
        var a when a.StartsWith('/') => string.IsNullOrEmpty(authState.IdentityHostUrl)
            ? a
            : $"{authState.IdentityHostUrl.TrimEnd('/')}{a}",
        var a => $"https://cdn.discordapp.com/avatars/{DiscordId}/{a}.png"
    };

    public UserRole Role => UserRoles.Parse(IsAuthenticated ? Find(AuthClaims.RoleClaim, SessionClaims.Role) : null);

    public bool IsSupporter => IsAuthenticated && Principal!.IsSupporter();

    public bool IsAtLeast(UserRole need) => UserRoles.IsAtLeast(Role, need);

    private string? Find(params string[] types) =>
        types.Select(t => Principal?.FindFirstValue(t)).FirstOrDefault(v => !string.IsNullOrEmpty(v));
}
