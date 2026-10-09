using EggIncognito.Core.Services.Devices;
using EggIncognito.Models.Devices;

namespace EggIncognito.Services.Devices;

public static class AndroidRecertFlow {
    public static IReadOnlyList<DeviceFlowStep> BuildPrimary(DeviceRecertConfig c) {
        var steps = new List<DeviceFlowStep> {
            DeviceFlowSteps.Key(DeviceKey.Wake),
            DeviceFlowSteps.Key(DeviceKey.DismissKeyguard),
            DeviceFlowSteps.LaunchApp(c.KsuWebUiPackage),
            DeviceFlowSteps.WaitForText(c.IntegrityHubLabel, timeoutSeconds: 30)
        };

        if (ExpirySelector(c) is { } expirySelector)
            steps.Add(DeviceFlowSteps.ReadField(c.ExpiryFieldName, expirySelector));

        steps.Add(DeviceFlowSteps.Tap(UiSelector.Text(c.RepairModeLabel)));
        steps.AddRange(PowerButtonSteps(c));
        steps.Add(DeviceFlowSteps.WaitForText(
            c.RepairCompleteText, timeoutSeconds: c.RepairTimeoutSeconds, pollSeconds: 5));
        steps.Add(DeviceFlowSteps.Screenshot("after-repair"));
        return steps;
    }

    public static IReadOnlyList<DeviceFlowStep> BuildFallback(DeviceRecertConfig c) {
        var steps = new List<DeviceFlowStep> {
            DeviceFlowSteps.LaunchApp(c.MagiskPackage),
            DeviceFlowSteps.Tap(UiSelector.Text(c.MagiskModulesLabel)),
            DeviceFlowSteps.Tap(UiSelector.Text(c.IntegrityBoxLabel)),
            DeviceFlowSteps.Tap(UiSelector.Text(c.MagiskActionLabel)),
            DeviceFlowSteps.WaitForText(c.RepairCompleteText, required: false),
            DeviceFlowSteps.Sleep(c.MagiskActionWaitSeconds)
        };

        if (MagiskCloseSelector(c) is { } closeSelector)
            steps.Add(DeviceFlowSteps.Tap(closeSelector, required: false));
        else if (c.MagiskCloseX is { } cx && c.MagiskCloseY is { } cy)
            steps.Add(DeviceFlowSteps.TapPoint(cx, cy));

        steps.Add(DeviceFlowSteps.Screenshot("after-magisk"));
        return steps;
    }

    public static IReadOnlyList<DeviceFlowStep> BuildVerify(DeviceRecertConfig c) {
        if (!c.VerifyCert) return [];
        if (c.ProfileDesc is not { Length: > 0 } profileDesc ||
            c.SettingsLabel is not { Length: > 0 } settingsLabel ||
            c.AboutLabel is not { Length: > 0 } aboutLabel) {
            return [];
        }

        return [
            DeviceFlowSteps.LaunchApp(c.PlayPackage) with { Required = false },
            DeviceFlowSteps.Tap(UiSelector.Desc(profileDesc), required: false),
            DeviceFlowSteps.Tap(UiSelector.Text(settingsLabel), required: false),
            DeviceFlowSteps.Tap(UiSelector.Text(aboutLabel), required: false),
            DeviceFlowSteps.AssertText(c.PlayProtectCertifiedText) with { Required = false }
        ];
    }

    private static UiSelector? ExpirySelector(DeviceRecertConfig c) {
        if (c.ExpiryFieldResourceId is { Length: > 0 } id) return UiSelector.Id(id);
        if (c.ExpiryFieldText is { Length: > 0 } text) return UiSelector.Text(text);
        return null;
    }

    private static UiSelector? PowerButtonSelector(DeviceRecertConfig c) {
        if (c.PowerButtonResourceId is { Length: > 0 } id) return UiSelector.Id(id);
        if (c.PowerButtonDesc is { Length: > 0 } desc) return UiSelector.Desc(desc);
        return null;
    }

    private static UiSelector? MagiskCloseSelector(DeviceRecertConfig c) =>
        c.MagiskCloseResourceId is { Length: > 0 } id ? UiSelector.Id(id) : null;

    private static IEnumerable<DeviceFlowStep> PowerButtonSteps(DeviceRecertConfig c) {
        if (PowerButtonSelector(c) is { } selector) {
            yield return DeviceFlowSteps.WaitForSelector(selector, timeoutSeconds: 20);
            yield return DeviceFlowSteps.Tap(selector);
            yield break;
        }

        if (c.PowerButtonX is { } x && c.PowerButtonY is { } y) {
            yield return DeviceFlowSteps.TapPoint(x, y);
            yield break;
        }

        if (c.PowerButtonText is { Length: > 0 } text) {
            var byText = UiSelector.Text(text);
            yield return DeviceFlowSteps.WaitForSelector(byText, timeoutSeconds: 20);
            yield return DeviceFlowSteps.Tap(byText);
            yield break;
        }

        yield return DeviceFlowSteps.AssertText("recert: no PowerButton selector/point configured");
    }
}
