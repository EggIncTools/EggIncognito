namespace EggIncognito.Models.Inspector;

public sealed record WireNodeDto(
    string Path,
    string? ResolvedName,
    int Field,
    string Wire,
    int Offset,
    int? Len,
    bool SchemaMismatch,
    List<WireNodeDto>? Children);
