namespace EggIncognito.Core.Services.Devices;

public static class BridgeRoutes {
    public const string SecretHeader = "X-Api-Key";
    public const string Root = "api/bridge";
    public const string Exec = "exec";
    public const string ExecStream = "exec/stream";
    public const string Docker = "docker";
    public const string Host = "host";
    public const string AdbRestart = "host/adb-restart";
    public const string Reach = "host/reach";
    public const string Claim = "claim";
    public const string Release = "release";
    public const string Fleet = "fleet";
    public const string Instances = "instances";

    public static string InstanceDestroy(string instanceId) =>
        $"{Instances}/{Uri.EscapeDataString(instanceId)}/destroy";

    public static string Absolute(string? baseUrl, string verb) =>
        $"{baseUrl?.TrimEnd('/')}/{Root}/{verb}";
}
