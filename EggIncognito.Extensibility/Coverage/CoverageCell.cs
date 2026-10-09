namespace EggIncognito.Models.Coverage;

public sealed record CoverageCell(
    string Rarity,
    int AfxRarity,
    int Items,
    int Observations,
    int PendingItems,
    int PendingObservations,
    int ItemTarget,
    int ObservationTarget,
    bool InScope,
    double Validity,
    int ItemsShort,
    int ObservationsShort,
    int SuggestedBatch,
    long ResolvedTargetId,
    long? CellTargetId);
