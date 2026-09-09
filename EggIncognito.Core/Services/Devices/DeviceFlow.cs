namespace EggIncognito.Core.Services.Devices;

public enum DeviceFlowStepKind {
    LaunchApp, Tap, TapPoint, Swipe, WaitForSelector, WaitForText, WaitForTextGone,
    Key, InputText, Sleep, Screenshot, AssertText, ReadField
}

public sealed record DeviceFlowStep(
    DeviceFlowStepKind Kind,
    string? AppRef = null,
    UiSelector? Selector = null,
    string? Text = null,
    DeviceKey? Key = null,
    int? X = null,
    int? Y = null,
    int TimeoutSeconds = 20,
    int PollSeconds = 2,
    bool Required = true,
    string? FieldName = null,
    string? Label = null,
    int? X2 = null,
    int? Y2 = null,
    int DurationMs = 0);

public static class DeviceFlowSteps {
    public static DeviceFlowStep LaunchApp(string appRef) =>
        new(DeviceFlowStepKind.LaunchApp, AppRef: appRef);

    public static DeviceFlowStep Tap(UiSelector selector, bool required = true) =>
        new(DeviceFlowStepKind.Tap, Selector: selector, Required: required);

    public static DeviceFlowStep TapPoint(int x, int y) =>
        new(DeviceFlowStepKind.TapPoint, X: x, Y: y);

    public static DeviceFlowStep Swipe(int x1, int y1, int x2, int y2, int durationMs) =>
        new(DeviceFlowStepKind.Swipe, X: x1, Y: y1, X2: x2, Y2: y2, DurationMs: durationMs);

    public static DeviceFlowStep WaitForSelector(
        UiSelector selector, int timeoutSeconds = 20, int pollSeconds = 2, bool required = true) =>
        new(DeviceFlowStepKind.WaitForSelector, Selector: selector, TimeoutSeconds: timeoutSeconds,
            PollSeconds: pollSeconds, Required: required);

    public static DeviceFlowStep WaitForText(
        string text, int timeoutSeconds = 20, int pollSeconds = 2, bool required = true) =>
        new(DeviceFlowStepKind.WaitForText, Text: text, TimeoutSeconds: timeoutSeconds,
            PollSeconds: pollSeconds, Required: required);

    public static DeviceFlowStep WaitForTextGone(
        string text, int timeoutSeconds = 20, int pollSeconds = 2, bool required = true) =>
        new(DeviceFlowStepKind.WaitForTextGone, Text: text, TimeoutSeconds: timeoutSeconds,
            PollSeconds: pollSeconds, Required: required);

    public static DeviceFlowStep Key(DeviceKey key) =>
        new(DeviceFlowStepKind.Key, Key: key);

    public static DeviceFlowStep InputText(string text) =>
        new(DeviceFlowStepKind.InputText, Text: text);

    public static DeviceFlowStep Sleep(int seconds) =>
        new(DeviceFlowStepKind.Sleep, TimeoutSeconds: seconds);

    public static DeviceFlowStep Screenshot(string label) =>
        new(DeviceFlowStepKind.Screenshot, Label: label);

    public static DeviceFlowStep AssertText(string text) =>
        new(DeviceFlowStepKind.AssertText, Text: text);

    public static DeviceFlowStep ReadField(string fieldName, UiSelector selector) =>
        new(DeviceFlowStepKind.ReadField, FieldName: fieldName, Selector: selector);
}

public sealed record DeviceFlowShot(string Label, byte[] Png);

public sealed record DeviceFlowResult(
    bool Ok,
    IReadOnlyList<string> Log,
    IReadOnlyDictionary<string, string> Fields,
    IReadOnlyList<DeviceFlowShot> Shots,
    string? FailedStep);
