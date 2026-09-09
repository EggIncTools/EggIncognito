namespace EggIncognito.Models.Devices;

public enum RecordedGestureKind {
    Tap,
    Hold,
    Swipe
}

public sealed record RecordedGesture(
    RecordedGestureKind Kind, int X, int Y, int X2, int Y2, int DurationMs, DateTimeOffset At) {
    public static RecordedGesture Tap(int x, int y) =>
        new(RecordedGestureKind.Tap, x, y, x, y, 0, DateTimeOffset.UtcNow);

    public static RecordedGesture Hold(int x, int y, int durationMs) =>
        new(RecordedGestureKind.Hold, x, y, x, y, durationMs, DateTimeOffset.UtcNow);

    public static RecordedGesture Swipe(int x1, int y1, int x2, int y2, int durationMs) =>
        new(RecordedGestureKind.Swipe, x1, y1, x2, y2, durationMs, DateTimeOffset.UtcNow);

    public string StepLine => Kind == RecordedGestureKind.Tap
        ? $"DeviceFlowSteps.TapPoint({X}, {Y})"
        : $"DeviceFlowSteps.Swipe({X}, {Y}, {X2}, {Y2}, {DurationMs})";

    public string Where => Kind == RecordedGestureKind.Swipe ? $"{X},{Y} to {X2},{Y2}" : $"{X},{Y}";

    public static string FlowText(IReadOnlyList<RecordedGesture> gestures) {
        var lines = new List<string>(gestures.Count * 2);
        for (var i = 0; i < gestures.Count; i++) {
            if (i > 0) {
                var gap = (int)Math.Round((gestures[i].At - gestures[i - 1].At).TotalSeconds);
                if (gap >= 1) lines.Add($"DeviceFlowSteps.Sleep({gap})");
            }

            lines.Add(gestures[i].StepLine);
        }

        return string.Join(",\n", lines);
    }
}
