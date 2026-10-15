namespace EggIncognito.Services.Predictions.Training;

public sealed record TrainingRequest(PredictionDomainKey Domain, bool Force);
