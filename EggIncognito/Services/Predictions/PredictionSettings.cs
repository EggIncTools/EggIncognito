namespace EggIncognito.Services.Predictions;

public sealed record PredictionHorizons(int EventDays, int ContractWeeks, int StepDays, int RefreshHours);

public sealed class PredictionSettings(IConfiguration config) {
    private const string EventDaysPath = "Predictions:Events:MaxHorizonDays";
    private const string ContractWeeksPath = "Predictions:Contracts:MaxHorizonWeeks";
    private const string StepDaysPath = "Predictions:HorizonStepDays";
    private const string RefreshHoursPath = "Predictions:RefreshHours";

    public PredictionHorizons Snapshot() => new(
        Math.Clamp(config.GetValue(EventDaysPath, 183), 30, 365),
        Math.Clamp(config.GetValue(ContractWeeksPath, 52), 4, 104),
        Math.Max(1, config.GetValue(StepDaysPath, 30)),
        Math.Max(1, config.GetValue(RefreshHoursPath, 24)));

    public IReadOnlyList<int> EventSteps {
        get {
            var snapshot = Snapshot();
            return Steps(snapshot.EventDays, snapshot.StepDays);
        }
    }

    public IReadOnlyList<int> ContractSteps {
        get {
            var snapshot = Snapshot();
            return Steps(snapshot.ContractWeeks, Math.Max(1, snapshot.StepDays / 7));
        }
    }

    private static List<int> Steps(int max, int step) {
        List<int> steps = [];
        for (int v = step; v < max; v += step) steps.Add(v);
        steps.Add(max);
        return steps;
    }
}
