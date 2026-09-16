using System.Text;
using System.Text.Json;
using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices;

public sealed class IosUiDriver(IDeviceConnectionFactory connections, IosUiDriver.Options opts) : IDeviceUiDriver {
    private const string NoSshNote = "ios ssh not configured";
    private const string CmdPath = "/tmp/egi-uinav.cmd";
    private const string JsonPath = "/tmp/egi-uinav.json";
    private const string PngPath = "/tmp/egi-uinav.png";
    private const string DonePath = "/tmp/egi-uinav.done";

    public string Platform => Platforms.Ios;

    public async Task<DeviceResult<UiTree>> DumpAsync(DeviceTarget target, CancellationToken ct) {
        var reply = await RunAsync(target, "dump", ct);
        if (reply.Outcome != DeviceOutcome.Ok) return new DeviceResult<UiTree>(reply.Outcome, default, reply.Note);

        if (connections.For(target) is not { } conn)
            return DeviceResult<UiTree>.Unreachable(NoSshNote);

        byte[]? bytes = await conn.PullBytesAsync(JsonPath, ct);
        if (bytes is null) return DeviceResult<UiTree>.Error("egi-uinav dump ok but json pull failed");

        string json = Encoding.UTF8.GetString(bytes);
        try {
            return DeviceResult<UiTree>.Success(new UiTree(ParseTree(json), json));
        } catch (JsonException ex) {
            return DeviceResult<UiTree>.Error($"egi-uinav json parse failed: {ex.Message}");
        }
    }

    public async Task<DeviceResult<byte[]>> ScreenshotAsync(DeviceTarget target, CancellationToken ct) {
        var reply = await RunAsync(target, "screenshot", ct);
        if (reply.Outcome != DeviceOutcome.Ok) return new DeviceResult<byte[]>(reply.Outcome, default, reply.Note);

        if (connections.For(target) is not { } conn)
            return DeviceResult<byte[]>.Unreachable(NoSshNote);

        byte[]? png = await conn.PullBytesAsync(PngPath, ct);
        return png is null
            ? DeviceResult<byte[]>.Error("egi-uinav screenshot ok but png pull failed")
            : DeviceResult<byte[]>.Success(png);
    }

    public async Task<DeviceResult<DeviceScreenState>> ScreenStateAsync(DeviceTarget target, CancellationToken ct) {
        var reply = await RunAsync(target, "state", ct);
        return reply.Outcome != DeviceOutcome.Ok
            ? new DeviceResult<DeviceScreenState>(reply.Outcome, default, reply.Note)
            : DeviceResult<DeviceScreenState>.Success(ParseScreenState(reply.Value!));
    }

    public async Task<DeviceResult<string>> FrontmostBundleAsync(DeviceTarget target, CancellationToken ct) {
        var reply = await RunAsync(target, "frontmost", ct);
        return reply.Outcome != DeviceOutcome.Ok
            ? new DeviceResult<string>(reply.Outcome, default, reply.Note)
            : DeviceResult<string>.Success(ParseFrontmost(reply.Value!));
    }

    public static DeviceScreenState ParseScreenState(string doneLine) {
        bool awake = Flag(doneLine, "awake=") is not false;
        bool locked = Flag(doneLine, "locked=") is true;
        return new DeviceScreenState(awake, locked);
    }

    public static string ParseFrontmost(string doneLine) => Token(doneLine, "bundle=") ?? "";

    private static bool? Flag(string line, string key) => Token(line, key) switch {
        "1" or "true" => true,
        "0" or "false" => false,
        _ => null
    };

    private static string? Token(string line, string key) {
        foreach (string part in line.Split(' ', StringSplitOptions.RemoveEmptyEntries)) {
            if (part.StartsWith(key, StringComparison.Ordinal)) return part[key.Length..];
        }

        return null;
    }

    public async Task<DeviceResult> TapAsync(DeviceTarget target, UiSelector selector, CancellationToken ct) {
        var dump = await DumpAsync(target, ct);
        if (!dump.Ok) return new DeviceResult(dump.Outcome, dump.Note);

        var node = UiSelector.Resolve(dump.Value!, selector);
        if (node is null) return DeviceResult.Error($"no node for {selector.By}={selector.Value}");

        return await TapPointAsync(target, node.Bounds.CenterX, node.Bounds.CenterY, ct);
    }

    public async Task<DeviceResult> TapPointAsync(DeviceTarget target, int x, int y, CancellationToken ct) =>
        ToResult(await RunAsync(target, $"tap {x} {y}", ct));

    public async Task<DeviceResult> InputTextAsync(DeviceTarget target, string text, CancellationToken ct) =>
        ToResult(await RunAsync(target, $"text {text}", ct));

    public Task<DeviceResult> KeyAsync(DeviceTarget target, DeviceKey key, CancellationToken ct) => key switch {
        DeviceKey.Home => KeyHomeAsync(target, ct),
        _ => Task.FromResult(DeviceResult.Unsupported($"ios ui driver: {key} not supported"))
    };

    public async Task<DeviceResult> LaunchAppAsync(DeviceTarget target, string appRef, CancellationToken ct) {
        if (connections.For(target) is not { } conn) return DeviceResult.Unreachable(NoSshNote);
        var r = await conn.ShellAsync($"uiopen --bundleid {appRef}", ct);
        return r.ExitCode == 0
            ? DeviceResult.Success()
            : DeviceResult.Error(DeviceParsing.TrimNote(r.Stderr + r.Stdout));
    }

    private async Task<DeviceResult> KeyHomeAsync(DeviceTarget target, CancellationToken ct) =>
        ToResult(await RunAsync(target, "key home", ct));

    private static DeviceResult ToResult(DeviceResult<string> reply) =>
        reply.Outcome == DeviceOutcome.Ok ? DeviceResult.Success() : new DeviceResult(reply.Outcome, reply.Note);

    private async Task<DeviceResult<string>> RunAsync(DeviceTarget target, string verbLine, CancellationToken ct) {
        if (connections.For(target) is not { } conn)
            return DeviceResult<string>.Unreachable(NoSshNote);

        if (await CheckTweakAsync(conn, ct) is { } missing) return missing;

        string quoted = DeviceShell.Quote(verbLine);
        var send = await conn.ShellAsync(
            $"rm -f {JsonPath} {PngPath} {DonePath}; printf %s {quoted} > {CmdPath}; chmod 666 {CmdPath}", ct);
        if (send.ExitCode != 0)
            return DeviceResult<string>.Unreachable(DeviceParsing.TrimNote(send.Stderr + send.Stdout));

        var deadline = DateTime.UtcNow.AddMilliseconds(opts.TimeoutMs);
        while (true) {
            var poll = await conn.ShellAsync($"[ -f {DonePath} ] && cat {DonePath}", ct);
            if (poll.ExitCode == 0) {
                string line = poll.Stdout.Trim();
                if (line.Length > 0)
                    return line.StartsWith("ok ", StringComparison.Ordinal)
                        ? DeviceResult<string>.Success(line)
                        : DeviceResult<string>.Error(DeviceParsing.TrimNote(line));
            }

            if (DateTime.UtcNow >= deadline)
                return DeviceResult<string>.Unreachable(
                    "egi-uinav tweak did not respond (installed? app foreground?)");

            try {
                await Task.Delay(opts.PollIntervalMs, ct);
            } catch (OperationCanceledException) {
                return DeviceResult<string>.Error("cancelled waiting for egi-uinav tweak");
            }
        }
    }

    private async Task<DeviceResult<string>?> CheckTweakAsync(IDeviceConnection conn, CancellationToken ct) {
        var probe = await conn.ShellAsync(
            $"test -f {opts.TweakPath} && echo tweak-present || echo tweak-absent", ct);
        if (probe.Stdout.Contains("tweak-present", StringComparison.Ordinal)) return null;
        return probe.Stdout.Contains("tweak-absent", StringComparison.Ordinal)
            ? DeviceResult<string>.Unsupported($"egi-uinav tweak not installed at {opts.TweakPath}")
            : DeviceResult<string>.Unreachable(
                $"could not verify the egi-uinav tweak: {DeviceParsing.TrimNote(probe.Stderr + probe.Stdout)}");
    }

    public static UiNode ParseTree(string json) {
        using var doc = JsonDocument.Parse(json);
        return ParseNode(doc.RootElement);
    }

    private static UiNode ParseNode(JsonElement el) {
        string? className = StringOrNull(el, "class");
        string? label = StringOrNull(el, "label");
        string? id = StringOrNull(el, "id");
        string? text = StringOrNull(el, "text");
        bool enabled = el.TryGetProperty("enabled", out var enabledEl) && enabledEl.ValueKind == JsonValueKind.True;
        var bounds = ParseBounds(el);

        var children = new List<UiNode>();
        if (el.TryGetProperty("children", out var childrenEl) && childrenEl.ValueKind == JsonValueKind.Array)
            foreach (var child in childrenEl.EnumerateArray()) {
                children.Add(ParseNode(child));
            }

        return new UiNode(id, text, label, className, null, bounds, enabled, enabled, children);
    }

    private static UiBounds ParseBounds(JsonElement el) {
        if (!el.TryGetProperty("frame", out var frame) || frame.ValueKind != JsonValueKind.Object)
            return default;

        double x = NumberOrZero(frame, "x");
        double y = NumberOrZero(frame, "y");
        double w = NumberOrZero(frame, "w");
        double h = NumberOrZero(frame, "h");
        return new UiBounds(Round(x), Round(y), Round(x + w), Round(y + h));
    }

    private static int Round(double v) => (int)Math.Round(v, MidpointRounding.AwayFromZero);

    private static double NumberOrZero(JsonElement obj, string prop) =>
        obj.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;

    private static string? StringOrNull(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public sealed record Options(string TweakPath, int PollIntervalMs = 250, int TimeoutMs = 5000);
}
