using EggIncognito.Runner.Runners;

namespace EggIncognito.Runner.Trigger;

public sealed record DeviceResyncResult(int Status, string? DeviceId, RunOutcome? Outcome, string? Error);

public sealed class DeviceResyncHandler {
    private readonly string _secret;
    private readonly IReadOnlyDictionary<string, IDeviceRunner> _runners;
    private readonly Dictionary<string, SemaphoreSlim> _locks;

    public DeviceResyncHandler(string secret, IReadOnlyDictionary<string, IDeviceRunner> runners) {
        _secret = secret;
        _runners = runners;
        _locks = [with(StringComparer.OrdinalIgnoreCase)];
        foreach (var id in runners.Keys) _locks[id] = new SemaphoreSlim(1, 1);
    }

    public DeviceResyncResult HandleOne(string? authorizationHeader, string id, bool force) {
        if (!BearerAuth.Matches(authorizationHeader, _secret)) return new DeviceResyncResult(401, id, null, "unauthorized");
        if (!_runners.TryGetValue(id, out var runner) || !_locks.TryGetValue(id, out var gate))
            return new DeviceResyncResult(404, id, null, "unknown device");
        if (!gate.Wait(0)) return new DeviceResyncResult(409, id, null, "a resync is already running");
        try {
            return new DeviceResyncResult(200, id, runner.RunOnce(force), null);
        } catch (Exception ex) {
            return new DeviceResyncResult(500, id, null, ex.Message);
        } finally {
            gate.Release();
        }
    }

    public IReadOnlyList<DeviceResyncResult> HandleAll(string? authorizationHeader, bool force) {
        if (!BearerAuth.Matches(authorizationHeader, _secret)) return [new DeviceResyncResult(401, null, null, "unauthorized")];
        return [.. _runners.Keys.Select(id => HandleOne(authorizationHeader, id, force))];
    }
}
