namespace EggIncognito.Services.Predictions.Training;

public enum TrainingState {
    Idle,
    Queued,
    Running,
    Done,
    Failed,
    Cancelled
}
