namespace EggIncognito.Services.Predictions.Training;

public sealed record TrainingStatus(
    PredictionDomainKey Domain,
    TrainingState State,
    long Version,
    double? StartedAt,
    double? FinishedAt,
    double Percent,
    string? Strategy,
    double? RunnerUpDelta,
    string? Error);
