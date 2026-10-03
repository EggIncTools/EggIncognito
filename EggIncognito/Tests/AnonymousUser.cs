using EggIdentity.Auth;

namespace EggIncognito.Tests;

internal sealed class AnonymousUser : ICurrentUser {
    public CurrentUser Current => CurrentUser.Anonymous;

    public Task<CurrentUser> GetAsync() => Task.FromResult(CurrentUser.Anonymous);
}
