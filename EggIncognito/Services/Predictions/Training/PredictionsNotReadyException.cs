namespace EggIncognito.Services.Predictions.Training;

public sealed class PredictionsNotReadyException(PredictionDomainKey domain, TrainingStatus status)
    : Exception($"{domain} predictions are not ready yet ({status.State}, {status.Percent:0}%)") {
    public PredictionDomainKey Domain { get; } = domain;
    public TrainingStatus Status { get; } = status;
}
