using EggIncognito.Core.Services.Devices;
using EggIncognito.Services.Devices;

namespace EggIncognito.Tests.Devices;

public class AndroidUiDriverTests {
    private const string UiDump =
        "<hierarchy rotation=\"0\">" +
        "<node index=\"0\" text=\"\" resource-id=\"\" class=\"android.widget.FrameLayout\" " +
        "package=\"com.android.vending\" clickable=\"false\" enabled=\"true\" bounds=\"[0,0][1080,2280]\">" +
        "<node index=\"0\" text=\"\" resource-id=\"com.android.vending:id/toolbar\" " +
        "class=\"android.widget.LinearLayout\" package=\"com.android.vending\" clickable=\"false\" " +
        "enabled=\"true\" bounds=\"[0,63][1080,235]\">" +
        "<node index=\"1\" text=\"Update\" resource-id=\"com.android.vending:id/update_button\" " +
        "class=\"android.widget.Button\" package=\"com.android.vending\" clickable=\"true\" enabled=\"true\" " +
        "bounds=\"[441,1590][639,1698]\"/>" +
        "</node>" +
        "<node index=\"1\" text=\"Uninstall\" resource-id=\"com.android.vending:id/uninstall_button\" " +
        "class=\"android.widget.Button\" package=\"com.android.vending\" clickable=\"true\" enabled=\"true\" " +
        "bounds=\"[200,1590][400,1698]\"/>" +
        "<node index=\"2\" text=\"Item\" resource-id=\"com.android.vending:id/item\" " +
        "class=\"android.widget.TextView\" package=\"com.android.vending\" clickable=\"false\" enabled=\"true\" " +
        "bounds=\"[0,300][200,400]\"/>" +
        "<node index=\"3\" text=\"Item\" resource-id=\"com.android.vending:id/item\" " +
        "class=\"android.widget.TextView\" package=\"com.android.vending\" clickable=\"false\" enabled=\"true\" " +
        "bounds=\"[0,400][200,500]\"/>" +
        "</node>" +
        "</hierarchy>";

    private static DeviceTarget AndroidTarget => new("a", "android", "SER", "com.auxbrain.egginc");

    private static FakeRunner DumpRunner() => new(args => {
        string cmd = ShellCommand(args);
        if (cmd.StartsWith("uiautomator dump", StringComparison.Ordinal)) return new ProcessResult(0, "", "");
        if (cmd.StartsWith("cat ", StringComparison.Ordinal)) return new ProcessResult(0, UiDump, "");
        return new ProcessResult(0, "", "");
    });

    private static string ShellCommand(string[] args) => args is ["-s", _, "shell", var cmd] ? cmd : "";

    [Fact]
    public async Task DumpAsync_ParsesDump_UpdateButtonBoundsAndCenter() {
        var driver = new AndroidUiDriver(new FakeConnections(DumpRunner()));

        var result = await driver.DumpAsync(AndroidTarget, default);
        var tree = result.Value;

        Assert.True(result.Ok);
        Assert.NotNull(tree);
        var update = tree.Nodes().Single(n => n.Text == "Update");
        Assert.Equal(540, update.Bounds.CenterX);
        Assert.Equal(1644, update.Bounds.CenterY);
        Assert.True(update.Clickable);
    }

    [Fact]
    public async Task Selectors_ResolveExpectedNodes() {
        var driver = new AndroidUiDriver(new FakeConnections(DumpRunner()));
        var dump = await driver.DumpAsync(AndroidTarget, default);
        var tree = dump.Value;
        Assert.NotNull(tree);

        var byText = UiSelector.Resolve(tree, UiSelector.Text("Update"));
        var byContains = UiSelector.Resolve(tree, UiSelector.TextContains("Up"));
        var byId = UiSelector.Resolve(tree, UiSelector.Id("com.android.vending:id/update_button"));

        Assert.NotNull(byText);
        Assert.Same(byText, byContains);
        Assert.Same(byText, byId);

        var first = UiSelector.Resolve(tree, UiSelector.Text("Item"));
        var second = UiSelector.Resolve(tree, new UiSelector(UiSelectorBy.Text, "Item", Index: 1));
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotSame(first, second);
        Assert.Equal(350, first.Bounds.CenterY);
        Assert.Equal(450, second.Bounds.CenterY);
    }

    [Fact]
    public async Task TapAsync_ResolvesSelector_AndTapsCenter() {
        var runner = DumpRunner();
        var driver = new AndroidUiDriver(new FakeConnections(runner));

        var result = await driver.TapAsync(AndroidTarget, UiSelector.Text("Update"), default);

        Assert.True(result.Ok);
        Assert.Contains(runner.Commands, c => c == "input tap 540 1644");
    }

    [Fact]
    public async Task TapAsync_NoMatch_ReturnsError() {
        var driver = new AndroidUiDriver(new FakeConnections(DumpRunner()));

        var result = await driver.TapAsync(AndroidTarget, UiSelector.Text("Nope"), default);

        Assert.Equal(DeviceOutcome.Error, result.Outcome);
    }

    [Fact]
    public void UiBoundsParser_ParsesCorners_AndCenter() {
        Assert.True(UiBoundsParser.TryParse("[0,0][100,200]", out var bounds));
        Assert.Equal(new UiBounds(0, 0, 100, 200), bounds);
        Assert.Equal(50, bounds.CenterX);
        Assert.Equal(100, bounds.CenterY);
    }

    [Fact]
    public void UiBoundsParser_Malformed_ReturnsFalse() {
        Assert.False(UiBoundsParser.TryParse("not bounds", out _));
        Assert.False(UiBoundsParser.TryParse("[0,0][100]", out _));
    }

    [Fact]
    public async Task DumpAsync_UiautomatorFails_ReturnsUnreachable() {
        var runner = new FakeRunner(_ => new ProcessResult(1, "", "device offline"));
        var driver = new AndroidUiDriver(new FakeConnections(runner));

        var result = await driver.DumpAsync(AndroidTarget, default);

        Assert.Equal(DeviceOutcome.Unreachable, result.Outcome);
    }

    [Fact]
    public async Task DumpAsync_EmptyCat_ReturnsError() {
        var runner = new FakeRunner(_ => new ProcessResult(0, "", ""));
        var driver = new AndroidUiDriver(new FakeConnections(runner));

        var result = await driver.DumpAsync(AndroidTarget, default);

        Assert.Equal(DeviceOutcome.Error, result.Outcome);
    }

    [Fact]
    public async Task ScreenshotAsync_UsesExecOutWithoutTempFile() {
        byte[] png = [1, 2, 3];
        var runner = new FakeRunner(_ => new ProcessResult(0, "", "")) {
            Bytes = _ => new ProcessBytesResult(0, png, "")
        };
        var driver = new AndroidUiDriver(new FakeConnections(runner));

        var result = await driver.ScreenshotAsync(AndroidTarget, default);

        Assert.True(result.Ok);
        Assert.Equal(png, result.Value);
        Assert.Contains(runner.RawArgs, a => a is ["-s", _, "exec-out", "screencap -p"]);
        Assert.DoesNotContain(runner.Commands, c => c.Contains("/sdcard/egi-screen.png", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ScreenshotAsync_ExecOutFails_ReturnsUnreachable() {
        var runner = new FakeRunner(_ => new ProcessResult(0, "", "")) {
            Bytes = _ => new ProcessBytesResult(1, [], "device offline")
        };
        var driver = new AndroidUiDriver(new FakeConnections(runner));

        var result = await driver.ScreenshotAsync(AndroidTarget, default);

        Assert.Equal(DeviceOutcome.Unreachable, result.Outcome);
    }

    [Fact]
    public async Task InputTextAsync_EscapesSpacesAndShellSpecials() {
        var runner = new FakeRunner(_ => new ProcessResult(0, "", ""));
        var driver = new AndroidUiDriver(new FakeConnections(runner));

        await driver.InputTextAsync(AndroidTarget, "a b$c", default);

        Assert.Contains(runner.Commands, c => c == "input text a%sb\\$c");
    }

    [Theory]
    [InlineData(DeviceKey.Home, "input keyevent KEYCODE_HOME")]
    [InlineData(DeviceKey.Back, "input keyevent KEYCODE_BACK")]
    [InlineData(DeviceKey.Wake, "input keyevent KEYCODE_WAKEUP")]
    [InlineData(DeviceKey.Sleep, "input keyevent KEYCODE_SLEEP")]
    [InlineData(DeviceKey.Enter, "input keyevent KEYCODE_ENTER")]
    [InlineData(DeviceKey.Recents, "input keyevent KEYCODE_APP_SWITCH")]
    [InlineData(DeviceKey.DismissKeyguard, "wm dismiss-keyguard")]
    public async Task KeyAsync_MapsToExpectedCommand(DeviceKey key, string expected) {
        var runner = new FakeRunner(_ => new ProcessResult(0, "", ""));
        var driver = new AndroidUiDriver(new FakeConnections(runner));

        await driver.KeyAsync(AndroidTarget, key, default);

        Assert.Contains(runner.Commands, c => c == expected);
    }

    [Fact]
    public async Task KeyAsync_CloseApp_ForceStopsTheTargetPackageFirst() {
        var runner = new FakeRunner(_ => new ProcessResult(0, "", ""));
        var driver = new AndroidUiDriver(new FakeConnections(runner));

        await driver.KeyAsync(AndroidTarget, DeviceKey.CloseApp, default);

        Assert.Contains(runner.Commands, c => c.StartsWith($"am force-stop {AndroidTarget.Package}", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SwipeAsync_IssuesInputSwipe_WithClampedDuration() {
        var runner = new FakeRunner(_ => new ProcessResult(0, "", ""));
        var driver = new AndroidUiDriver(new FakeConnections(runner));

        var result = await driver.SwipeAsync(AndroidTarget, 10, 500, 10, 100, 5, default);

        Assert.True(result.Ok);
        Assert.Contains(runner.Commands, c => c == "input swipe 10 500 10 100 50");
    }

    [Fact]
    public async Task LaunchAppAsync_IssuesMonkeyCommand() {
        var runner = new FakeRunner(_ => new ProcessResult(0, "", ""));
        var driver = new AndroidUiDriver(new FakeConnections(runner));

        await driver.LaunchAppAsync(AndroidTarget, "com.auxbrain.egginc", default);

        Assert.Contains(runner.Commands,
            c => c == "monkey -p com.auxbrain.egginc -c android.intent.category.LAUNCHER 1");
    }

    [Fact]
    public async Task RunAsync_NonZeroExit_ReturnsTrimmedError() {
        var runner = new FakeRunner(_ => new ProcessResult(1, "out", "some failure"));
        var driver = new AndroidUiDriver(new FakeConnections(runner));

        var result = await driver.TapPointAsync(AndroidTarget, 1, 2, default);

        Assert.Equal(DeviceOutcome.Error, result.Outcome);
        Assert.Equal("some failureout", result.Note);
    }

    private sealed class FakeRunner(Func<string[], ProcessResult> fn) : IProcessRunner {
        public List<string> Commands { get; } = [];
        public List<string[]> RawArgs { get; } = [];
        public Func<string[], ProcessBytesResult>? Bytes { get; init; }

        public Task<ProcessResult> RunAsync(string exe, string[] args, CancellationToken ct) {
            RawArgs.Add(args);
            string cmd = ShellCommand(args);
            if (cmd.Length > 0) Commands.Add(cmd);
            return Task.FromResult(fn(args));
        }

        public Task<ProcessBytesResult> RunBytesAsync(string exe, string[] args, CancellationToken ct) {
            RawArgs.Add(args);
            return Task.FromResult(Bytes is null
                ? new ProcessBytesResult(-1, [], "no raw handler")
                : Bytes(args));
        }
    }

    private sealed class FakeConnections(IProcessRunner runner) : IDeviceConnectionFactory {
        public IDeviceConnection? For(DeviceTarget target) => new AdbDeviceConnection(runner, target.Target);
        public SshDeviceConnection? Ios(string? hostFallback = null) => null;
    }
}
