namespace EggIncognito.Services.Predictions.Training;

public sealed record TrainingRequest(PredictionDomainKey Domain, bool Force, Func<CancellationToken, CsvTuneResult>? Csv = null);

public sealed record CsvTuneResult(
    PredictionDomainKey Domain,
    double FinishedAt,
    int Samples,
    IReadOnlyList<StrategyScore> Scoreboard,
    IReadOnlyList<StrategyScore>? Colleggtibles);
