using EggIncognito.Core.Services;

namespace EggIncognito.Models.Routes;

public sealed record EffectiveInfo(
    string? Request,
    string? Response,
    bool RequestWrapped,
    bool ResponseWrapped,
    bool PathParam) {
    public static EffectiveInfo From(RouteInfo r) =>
        new(r.Request, r.Response, r.RequestWrapped, r.ResponseWrapped, r.PathParam);
}
