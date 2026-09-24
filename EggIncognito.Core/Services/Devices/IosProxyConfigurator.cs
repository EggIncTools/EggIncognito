using System.Globalization;

namespace EggIncognito.Core.Services.Devices;

public sealed class IosProxyConfigurator(IProcessRunner runner, IosProxyConfigurator.SshConfig ssh)
    : IDeviceProxyConfigurator {
    private const string DefaultPlutil = "/cores/binpack/usr/bin/plutil";
    private const string DefaultPrefs = "/var/preferences/SystemConfiguration/preferences.plist";
    internal const string DefaultReloadCommand = "launchctl kickstart -k system/com.apple.configd";

    public string Platform => "ios";

    public async Task<(bool Ok, string? Note)> SetProxyAsync(DeviceTarget device, string hostIp, int port,
        CancellationToken ct) {
        return !string.IsNullOrEmpty(ssh.SetTemplate)
            ? await Ssh(
                ssh.SetTemplate.Replace("{host}", hostIp)
                    .Replace("{port}", port.ToString(CultureInfo.InvariantCulture)), ct)
            : string.IsNullOrEmpty(ssh.NetworkServiceGuid)
                ? (false, "ios proxy needs the network-service guid (DeviceCapture:Ios:NetworkServiceGuid)")
                : await Ssh(BuildSet(hostIp, port), ct);
    }

    public async Task<(bool Ok, string? Note)> ClearProxyAsync(DeviceTarget device, CancellationToken ct) {
        return !string.IsNullOrEmpty(ssh.ClearTemplate)
            ? await Ssh(ssh.ClearTemplate, ct)
            : string.IsNullOrEmpty(ssh.NetworkServiceGuid)
                ? (false, "ios proxy needs the network-service guid (DeviceCapture:Ios:NetworkServiceGuid)")
                : await Ssh(BuildClear(), ct);
    }

    internal string BuildSet(string hostIp, int port) {
        string portStr = port.ToString(CultureInfo.InvariantCulture);
        string[] expected = [
            "HTTPEnable = 1;", "HTTPSEnable = 1;",
            $"""HTTPProxy = \"{hostIp}\";""", $"""HTTPSProxy = \"{hostIp}\";""",
            $"HTTPPort = {portStr};", $"HTTPSPort = {portStr};"
        ];
        string[] writes = [
            Write("HTTPEnable", "1", "int"), Write("HTTPProxy", hostIp, "string"), Write("HTTPPort", portStr, "int"),
            Write("HTTPSEnable", "1", "int"), Write("HTTPSProxy", hostIp, "string"), Write("HTTPSPort", portStr, "int")
        ];
        return Script(expected, $"proxy unchanged {hostIp}:{portStr}", writes,
            $"proxy set {hostIp}:{portStr} (configd restarted)");
    }

    internal string BuildClear() =>
        Script(["HTTPEnable = 0;", "HTTPSEnable = 0;"], "proxy already clear",
            [Write("HTTPEnable", "0", "int"), Write("HTTPSEnable", "0", "int")], "proxy cleared (configd restarted)");

    private string Plutil => ssh.PlutilPath ?? DefaultPlutil;
    private string Prefs => ssh.PrefsPlist ?? DefaultPrefs;
    private string ProxiesKey => $"-key NetworkServices -key {ssh.NetworkServiceGuid} -key Proxies";

    private string Write(string key, string value, string type) =>
        $"{Plutil} {ProxiesKey} -key {key} -value {value} -type {type} {Prefs}";

    private string Script(string[] expected, string unchangedNote, string[] writes, string doneNote) {
        string reload = (ssh.ReloadCommand ?? DefaultReloadCommand).Replace("'", "'\\''");
        string guard = string.Join(" && ", expected.Select(e => $"echo \"$CUR\" | grep -qF \"{e}\""));
        return "/bin/sh -c '" +
               $"CUR=$({Plutil} {ProxiesKey} {Prefs} 2>/dev/null); " +
               $"""if {guard}; then echo "{unchangedNote}"; exit 0; fi; """ +
               string.Join("; ", writes) + "; " +
               $$"""OUT=$({{reload}} 2>&1) || { echo "configd reload failed: $OUT"; exit 1; }; """ +
               $"""echo "{doneNote}"'""";
    }

    private async Task<(bool Ok, string? Note)> Ssh(string remoteCmd, CancellationToken ct) {
        if (string.IsNullOrEmpty(ssh.Host) || string.IsNullOrEmpty(ssh.KeyPath))
            return (false, "ios ssh host/key not configured");
        var r = await runner.RunAsync("ssh", new SshEndpoint(ssh.Host, ssh.Port, ssh.KeyPath).SshArgs(remoteCmd), ct);
        if (r.ExitCode != 0) return (false, DeviceParsing.TrimNote(r.Stderr + r.Stdout));
        string note = DeviceParsing.TrimNote(r.Stdout);
        return (true, note.Length > 0 ? note : null);
    }

    public sealed record SshConfig(
        string? Host,
        string Port,
        string? KeyPath,
        string? SetTemplate,
        string? ClearTemplate,
        string? NetworkServiceGuid = null,
        string? PlutilPath = null,
        string? PrefsPlist = null,
        string? ReloadCommand = null);
}
