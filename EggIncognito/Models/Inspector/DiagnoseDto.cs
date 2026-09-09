namespace EggIncognito.Models.Inspector;

public sealed record DiagnoseDto(
    bool Ok,
    int TotalLen,
    int NodesWalked,
    DiagnoseError? FirstError,
    HexWindowDto? HexAround,
    List<SalvagedText>? Salvaged,
    List<WireNodeDto>? Tree,
    WireRecovery? Recovered,
    string? Error);
