namespace EggIncognito.Core.Services.Devices;

public readonly record struct UiBounds(int Left, int Top, int Right, int Bottom) {
    public int CenterX => (Left + Right) / 2;
    public int CenterY => (Top + Bottom) / 2;
}

public sealed record UiNode(
    string? ResourceId, string? Text, string? ContentDesc, string? ClassName, string? Package,
    UiBounds Bounds, bool Clickable, bool Enabled, IReadOnlyList<UiNode> Children) {
    public IEnumerable<UiNode> Flatten() {
        yield return this;
        foreach (var c in Children) {
            foreach (var n in c.Flatten()) {
                yield return n;
            }
        }
    }
}

public sealed record UiTree(UiNode Root, string Raw) {
    public IEnumerable<UiNode> Nodes() => Root.Flatten();
}

public enum UiSelectorBy { ResourceId, Text, ContentDesc, ClassName }

public sealed record UiSelector(UiSelectorBy By, string Value, bool Contains = false, int Index = 0) {
    public static UiSelector Id(string v) => new(UiSelectorBy.ResourceId, v);
    public static UiSelector Text(string v) => new(UiSelectorBy.Text, v);
    public static UiSelector TextContains(string v) => new(UiSelectorBy.Text, v, true);
    public static UiSelector Desc(string v) => new(UiSelectorBy.ContentDesc, v);
    public static UiSelector Class(string v) => new(UiSelectorBy.ClassName, v);

    public bool Matches(UiNode n) {
        string? attr = By switch {
            UiSelectorBy.ResourceId => n.ResourceId,
            UiSelectorBy.Text => n.Text,
            UiSelectorBy.ContentDesc => n.ContentDesc,
            UiSelectorBy.ClassName => n.ClassName,
            _ => null
        };
        if (attr is null) return false;
        return Contains
            ? attr.Contains(Value, StringComparison.Ordinal)
            : string.Equals(attr, Value, StringComparison.Ordinal);
    }

    public static UiNode? Resolve(UiTree tree, UiSelector sel) =>
        tree.Nodes().Where(sel.Matches).Skip(sel.Index).FirstOrDefault();
}

public enum DeviceKey { Home, Back, Wake, Sleep, Enter, DismissKeyguard, Recents, CloseApp }

public enum TouchPhase { Down, Move, Up, Cancel }

public readonly record struct UiScreenSize(int Width, int Height);

public interface IDeviceUiDriver {
    string Platform { get; }
    Task<DeviceResult<UiTree>> DumpAsync(DeviceTarget target, CancellationToken ct);
    Task<DeviceResult<byte[]>> ScreenshotAsync(DeviceTarget target, CancellationToken ct);

    Task<DeviceResult<UiScreenSize>> ScreenSizeAsync(DeviceTarget target, CancellationToken ct) =>
        Task.FromResult(DeviceResult<UiScreenSize>.Unsupported($"{Platform} ui driver: screen size not supported"));
    Task<DeviceResult> TapAsync(DeviceTarget target, UiSelector selector, CancellationToken ct);
    Task<DeviceResult> TapPointAsync(DeviceTarget target, int x, int y, CancellationToken ct);

    Task<DeviceResult> TouchAsync(DeviceTarget target, TouchPhase phase, int x, int y, CancellationToken ct) =>
        Task.FromResult(DeviceResult.Unsupported($"{Platform} ui driver: held touches not supported"));

    Task<DeviceResult> SwipeAsync(DeviceTarget target, int x1, int y1, int x2, int y2, int durationMs,
        CancellationToken ct) =>
        Task.FromResult(DeviceResult.Unsupported($"{Platform} ui driver: swipe not supported"));
    Task<DeviceResult> InputTextAsync(DeviceTarget target, string text, CancellationToken ct);
    Task<DeviceResult> KeyAsync(DeviceTarget target, DeviceKey key, CancellationToken ct);
    Task<DeviceResult> LaunchAppAsync(DeviceTarget target, string appRef, CancellationToken ct);
}
