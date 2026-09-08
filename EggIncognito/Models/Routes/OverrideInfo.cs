namespace EggIncognito.Models.Routes;

public sealed record OverrideInfo(
    string? Request,
    string? Response,
    bool? RequestWrapped,
    bool? ResponseWrapped,
    bool? PathParam,
    DateTimeOffset UpdatedAt,
    Guid? UpdatedBy);
