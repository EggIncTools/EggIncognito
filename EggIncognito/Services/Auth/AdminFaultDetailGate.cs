using EggIdentity.Auth;
using EggIdentity.Contract;
using EggIdentity.UI;

namespace EggIncognito.Services.Auth;

public sealed class AdminFaultDetailGate(ICurrentUser user) : IFaultDetailGate {
    public ValueTask<bool> CanSeeDetailAsync() => ValueTask.FromResult(user.Current.IsAtLeast(UserRole.Admin));
}
