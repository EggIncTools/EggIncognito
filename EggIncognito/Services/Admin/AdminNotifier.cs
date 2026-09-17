namespace EggIncognito.Services.Admin;

public static class AdminTopics {
    public const string Users = "users";
    public const string Notifications = "notifications";
    public const string ThemePolicy = "theme-policy";
    public const string DataStatus = "data-status";
    public const string Binaries = "binaries";
    public const string GameData = "game-data";
    public const string Events = "events";
    public const string Contracts = "contracts";
    public const string Staged = "staged";
    public const string ProtoRegistry = "proto-registry";
    public const string Contributions = "contributions";
    public const string Sessions = "sessions";
    public const string Console = "console";
    public const string Maintenance = "maintenance";
    public const string VirtualDevices = "virtual-devices";
    public const string ImageBuilds = "image-builds";
    public const string DeviceStatus = "device-status";
    public const string Apks = "apks";
    public const string Tags = "tags";
}

public sealed class AdminNotifier {
    private readonly Lock _gate = new();
    private event Action<string>? Changed;

    public IDisposable Subscribe(Action<string> handler) {
        lock (_gate) Changed += handler;
        return new Subscription(this, handler);
    }

    public void Publish(string topic) {
        Action<string>? snapshot;
        lock (_gate) snapshot = Changed;
        snapshot?.Invoke(topic);
    }

    private void Remove(Action<string> handler) {
        lock (_gate) Changed -= handler;
    }

    private sealed class Subscription(AdminNotifier owner, Action<string> handler) : IDisposable {
        public void Dispose() => owner.Remove(handler);
    }
}
