using EggIncognito.Core.Services;

namespace EggIncognito.Models.Docs;

public sealed record AuxbrainRouteWire(
    string Path,
    string Namespace,
    string? RequestType,
    string? ResponseType,
    bool RequestWrapped,
    bool ResponseWrapped,
    bool PathParam,
    string Status,
    IReadOnlyList<string> Aliases) {
    public static AuxbrainRouteWire From(AuxbrainEntry e) =>
        new(e.Path, e.Namespace, e.RequestType, e.ResponseType, e.RequestWrapped, e.ResponseWrapped, e.PathParam,
            AuxbrainCatalog.Label(e.Status), e.Aliases);
}
