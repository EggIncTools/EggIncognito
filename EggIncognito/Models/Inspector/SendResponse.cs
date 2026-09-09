using EggIncognito.Core.Services;

namespace EggIncognito.Models.Inspector;

public sealed record SendResponse(
    int? Status,
    string? RawBase64,
    List<TransportStage>? Stages,
    string? Json,
    string? Error,
    string? Resolution,
    bool WrappedMismatch = false);
