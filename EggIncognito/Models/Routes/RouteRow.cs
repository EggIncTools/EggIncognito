namespace EggIncognito.Models.Routes;

public sealed record RouteRow(string Path, string Source, EffectiveInfo? Effective, OverrideInfo? Override) {
    public UpsertRouteOverride Draft() => new(
        Override?.Request ?? Effective?.Request,
        Override?.Response ?? Effective?.Response,
        Override?.RequestWrapped ?? Effective?.RequestWrapped ?? false,
        Override?.ResponseWrapped ?? Effective?.ResponseWrapped ?? false,
        Override?.PathParam ?? Effective?.PathParam ?? false);
}
