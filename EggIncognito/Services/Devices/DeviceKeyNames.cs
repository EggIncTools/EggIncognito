using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices;

public static class DeviceKeyNames {
    private static readonly Dictionary<string, DeviceKey> ByName = new(StringComparer.OrdinalIgnoreCase) {
        ["back"] = DeviceKey.Back,
        ["home"] = DeviceKey.Home,
        ["recents"] = DeviceKey.Recents,
        ["enter"] = DeviceKey.Enter,
        ["wake"] = DeviceKey.Wake,
        ["sleep"] = DeviceKey.Sleep,
        ["dismiss-keyguard"] = DeviceKey.DismissKeyguard,
        ["close-app"] = DeviceKey.CloseApp,
        ["del"] = DeviceKey.Delete,
        ["forward-del"] = DeviceKey.ForwardDelete,
        ["tab"] = DeviceKey.Tab,
        ["up"] = DeviceKey.Up,
        ["down"] = DeviceKey.Down,
        ["left"] = DeviceKey.Left,
        ["right"] = DeviceKey.Right,
        ["page-up"] = DeviceKey.PageUp,
        ["page-down"] = DeviceKey.PageDown
    };

    public static IReadOnlyCollection<string> All => ByName.Keys;

    public static bool TryParse(string? name, out DeviceKey key) {
        key = DeviceKey.Back;
        return !string.IsNullOrWhiteSpace(name) && ByName.TryGetValue(name.Trim(), out key);
    }
}
