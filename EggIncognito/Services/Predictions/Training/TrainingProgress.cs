namespace EggIncognito.Services.Predictions.Training;

public sealed record TrainingProgress(int StrategyIndex, int StrategyCount, int SlotIndex, int SlotCount) {
    public double Percent {
        get {
            if (StrategyCount <= 0) return 0;
            double slotShare = SlotCount <= 0 ? 0 : SlotIndex / (double)SlotCount;
            return Math.Clamp((StrategyIndex + slotShare) / StrategyCount * 100, 0, 100);
        }
    }
}
