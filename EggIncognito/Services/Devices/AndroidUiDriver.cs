using System.Text;
using System.Xml;
using System.Xml.Linq;
using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices;

public sealed class AndroidUiDriver(IDeviceConnectionFactory connections) : IDeviceUiDriver {
    private const string ShellSpecials = "`()<>|;&*\\~\"'$";

    public string Platform => Platforms.Android;

    public async Task<DeviceResult<UiTree>> DumpAsync(DeviceTarget target, CancellationToken ct) {
        var conn = connections.For(target)!;
        var dump = await conn.ShellAsync("uiautomator dump /sdcard/egi-ui.xml", ct);
        if (dump.ExitCode != 0)
            return DeviceResult<UiTree>.Unreachable(DeviceParsing.TrimNote(dump.Stderr + dump.Stdout));

        var cat = await conn.ShellAsync("cat /sdcard/egi-ui.xml", ct);
        if (cat.ExitCode != 0 || string.IsNullOrWhiteSpace(cat.Stdout))
            return DeviceResult<UiTree>.Error("empty ui dump");

        try {
            var doc = XDocument.Parse(cat.Stdout);
            var hierarchy = doc.Root;
            if (hierarchy is null)
                return DeviceResult<UiTree>.Error("ui dump parse failed: no root element");

            var topNodes = hierarchy.Elements("node").Select(BuildNode).ToList();
            UiNode root = topNodes.Count == 1
                ? topNodes[0]
                : new UiNode(null, null, null, null, null, default, false, false, topNodes);

            return DeviceResult<UiTree>.Success(new UiTree(root, cat.Stdout));
        } catch (XmlException ex) {
            return DeviceResult<UiTree>.Error($"ui dump parse failed: {ex.Message}");
        }
    }

    public async Task<DeviceResult<byte[]>> ScreenshotAsync(DeviceTarget target, CancellationToken ct) {
        var conn = connections.For(target)!;
        if (conn.SupportsExecOut) {
            var raw = await conn.ExecOutAsync("screencap -p", ct);
            if (raw.ExitCode != 0)
                return DeviceResult<byte[]>.Unreachable(DeviceParsing.TrimNote(raw.Stderr));
            return raw.Stdout.Length > 0
                ? DeviceResult<byte[]>.Success(raw.Stdout)
                : DeviceResult<byte[]>.Error("screencap produced no bytes");
        }

        var cap = await conn.ShellAsync("screencap -p /sdcard/egi-screen.png", ct);
        if (cap.ExitCode != 0)
            return DeviceResult<byte[]>.Unreachable(DeviceParsing.TrimNote(cap.Stderr + cap.Stdout));

        byte[]? png = await conn.PullBytesAsync("/sdcard/egi-screen.png", ct);
        return png is null
            ? DeviceResult<byte[]>.Error("screencap pull failed")
            : DeviceResult<byte[]>.Success(png);
    }

    public async Task<DeviceResult> TapAsync(DeviceTarget target, UiSelector selector, CancellationToken ct) {
        var dump = await DumpAsync(target, ct);
        if (!dump.Ok)
            return new DeviceResult(dump.Outcome, dump.Note);

        var node = UiSelector.Resolve(dump.Value!, selector);
        if (node is null)
            return DeviceResult.Error($"no node for {selector.By}={selector.Value}");

        return await TapPointAsync(target, node.Bounds.CenterX, node.Bounds.CenterY, ct);
    }

    public Task<DeviceResult> TapPointAsync(DeviceTarget target, int x, int y, CancellationToken ct) =>
        RunAsync(target, $"input tap {x} {y}", ct);

    public Task<DeviceResult> SwipeAsync(DeviceTarget target, int x1, int y1, int x2, int y2, int durationMs,
        CancellationToken ct) =>
        RunAsync(target, $"input swipe {x1} {y1} {x2} {y2} {Math.Clamp(durationMs, 50, 2000)}", ct);

    public Task<DeviceResult> InputTextAsync(DeviceTarget target, string text, CancellationToken ct) =>
        RunAsync(target, $"input text {EscapeInputText(text)}", ct);

    public Task<DeviceResult> KeyAsync(DeviceTarget target, DeviceKey key, CancellationToken ct) {
        string cmd = key switch {
            DeviceKey.Home => "input keyevent KEYCODE_HOME",
            DeviceKey.Back => "input keyevent KEYCODE_BACK",
            DeviceKey.Wake => "input keyevent KEYCODE_WAKEUP",
            DeviceKey.Sleep => "input keyevent KEYCODE_SLEEP",
            DeviceKey.Enter => "input keyevent KEYCODE_ENTER",
            DeviceKey.Recents => "input keyevent KEYCODE_APP_SWITCH",
            DeviceKey.DismissKeyguard => "wm dismiss-keyguard",
            DeviceKey.CloseApp => $"am force-stop {target.Package}; " + DeviceForeground.CloseForegroundCommand,
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, "unhandled DeviceKey")
        };
        return RunAsync(target, cmd, ct);
    }

    public Task<DeviceResult> LaunchAppAsync(DeviceTarget target, string appRef, CancellationToken ct) =>
        RunAsync(target, $"monkey -p {appRef} -c android.intent.category.LAUNCHER 1", ct);

    private async Task<DeviceResult> RunAsync(DeviceTarget target, string cmd, CancellationToken ct) {
        var conn = connections.For(target)!;
        var r = await conn.ShellAsync(cmd, ct);
        return r.ExitCode == 0
            ? DeviceResult.Success()
            : DeviceResult.Error(DeviceParsing.TrimNote(r.Stderr + r.Stdout));
    }

    private static UiNode BuildNode(XElement el) {
        var children = el.Elements("node").Select(BuildNode).ToList();
        UiBounds bounds = UiBoundsParser.TryParse(el.Attribute("bounds")?.Value ?? "", out var parsed)
            ? parsed
            : default;
        return new UiNode(
            el.Attribute("resource-id")?.Value,
            el.Attribute("text")?.Value,
            el.Attribute("content-desc")?.Value,
            el.Attribute("class")?.Value,
            el.Attribute("package")?.Value,
            bounds,
            el.Attribute("clickable")?.Value == "true",
            el.Attribute("enabled")?.Value == "true",
            children);
    }

    private static string EscapeInputText(string text) {
        var sb = new StringBuilder(text.Length);
        foreach (char c in text) {
            if (c == ' ') {
                sb.Append("%s");
                continue;
            }

            if (ShellSpecials.Contains(c))
                sb.Append('\\');
            sb.Append(c);
        }

        return sb.ToString();
    }
}
