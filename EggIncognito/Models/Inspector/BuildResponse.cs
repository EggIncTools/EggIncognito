using System.Text.Json;
using EggIncognito.Core.Services;

namespace EggIncognito.Models.Inspector;

public sealed record BuildResponse(
    List<TransportStage>? Stages,
    string? FinalBase64,
    string? FinalFormBody,
    bool CanSign,
    string? Error,
    string? Resolution,
    JsonElement? Details);
