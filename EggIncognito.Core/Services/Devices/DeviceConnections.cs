namespace EggIncognito.Core.Services.Devices;

public sealed class AdbDeviceConnection(IProcessRunner runner, string serial) : IDeviceConnection {
    public string Serial => serial;
    public string Platform => "android";

    public bool SupportsExecOut => true;

    private bool IsNetworkSerial => AdbTcp.IsNetworkSerial(serial);

    public static async Task<ProcessResult> RunAsync(IProcessRunner runner, string serial, IReadOnlyList<string> args, TimeSpan? timeout, CancellationToken ct) {
        string[] full = ["-s", serial, .. args];
        using var cts = timeout is null ? null : CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts?.CancelAfter(timeout!.Value);
        var token = cts?.Token ?? ct;
        var r = await runner.RunAsync("adb", full, token);
        if (r.ExitCode == 0 || !AdbTcp.IsNetworkSerial(serial) || !AdbTcp.LooksDisconnected(r.Stderr)) return r;
        await AdbTcp.ReviveAsync(runner, serial, token);
        return await runner.RunAsync("adb", full, token);
    }

    public Task<ProcessResult> AdbAsync(IReadOnlyList<string> args, TimeSpan? timeout, CancellationToken ct) =>
        RunAsync(runner, serial, args, timeout, ct);

    private Task<ProcessResult> RunAsync(string[] args, CancellationToken ct) =>
        RunAsync(runner, serial, args, null, ct);

    private async Task<ProcessBytesResult> RunBytesAsync(string[] args, CancellationToken ct) {
        var r = await runner.RunBytesAsync("adb", args, ct);
        if (r.ExitCode == 0 || !IsNetworkSerial || !AdbTcp.LooksDisconnected(r.Stderr)) return r;
        await AdbTcp.ReviveAsync(runner, serial, ct);
        return await runner.RunBytesAsync("adb", args, ct);
    }

    public Task<ProcessResult> ShellAsync(string command, CancellationToken ct) =>
        RunAsync(["shell", command], ct);

    public Task<ProcessBytesResult> ExecOutAsync(string command, CancellationToken ct) =>
        RunBytesAsync(["-s", serial, "exec-out", command], ct);

    public async Task<ProcessHandle?> ExecOutStreamAsync(string command, CancellationToken ct) {
        if (IsNetworkSerial) await AdbTcp.ReviveAsync(runner, serial, ct);

        try {
            return await runner.StartAsync("adb", ["-s", serial, "exec-out", command], ct);
        } catch (NotSupportedException) {
            return null;
        }
    }

    public async Task<byte[]?> PullBytesAsync(string remotePath, CancellationToken ct) {
        string dest = DeviceShell.NewTempPath(".bin");
        try {
            var pull = await RunAsync(["pull", remotePath, dest], ct);
            return pull.ExitCode != 0 ? null : DeviceShell.ReadTemp(dest);
        } finally {
            DeviceShell.TryDelete(dest);
        }
    }

    public async Task<bool> PushFileAsync(string localPath, string remotePath, CancellationToken ct) =>
        (await RunAsync(["push", localPath, remotePath], ct)).ExitCode == 0;
}

public sealed class SshDeviceConnection(IProcessRunner runner, SshEndpoint endpoint) : IDeviceConnection {
    public SshEndpoint Endpoint => endpoint;
    public string Platform => "ios";

    public Task<ProcessResult> ShellAsync(string command, CancellationToken ct) =>
        runner.RunAsync("ssh", endpoint.SshArgs(command), ct);

    public async Task<byte[]?> PullBytesAsync(string remotePath, CancellationToken ct) {
        string dest = DeviceShell.NewTempPath(".bin");
        try {
            var scp = await runner.RunAsync("scp", endpoint.ScpDownArgs(remotePath, dest), ct);
            return scp.ExitCode != 0 ? null : DeviceShell.ReadTemp(dest);
        } finally {
            DeviceShell.TryDelete(dest);
        }
    }

    public async Task<bool> PushFileAsync(string localPath, string remotePath, CancellationToken ct) =>
        (await runner.RunAsync("scp", endpoint.ScpUpArgs(localPath, remotePath), ct)).ExitCode == 0;
}
