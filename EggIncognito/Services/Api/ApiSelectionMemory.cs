using EggIncognito.Models.Inspector;

namespace EggIncognito.Services.Api;

public sealed record ApiSelectionMemory(
    ApiSelectionKind Kind,
    string Group,
    string Id,
    string? Sub,
    DocSubjectRef? Docs = null);
