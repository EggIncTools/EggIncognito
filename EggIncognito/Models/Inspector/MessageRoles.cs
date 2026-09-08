namespace EggIncognito.Models.Inspector;

public sealed record MessageRoles(
    IReadOnlyList<string> Request,
    IReadOnlyList<string> Response,
    IReadOnlyList<string> Other,
    IReadOnlyList<string> RequestOthers,
    IReadOnlyList<string> ResponseOthers) {
    public static readonly MessageRoles Empty = new([], [], [], [], []);
}
