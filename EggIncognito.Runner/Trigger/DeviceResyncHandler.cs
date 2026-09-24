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

    public async Task<DeviceResyncResult> HandleOneAsync(string? authorizationHeader, string id, bool force,
        CancellationToken ct = default) {
        if (!BearerAuth.Matches(authorizationHeader, _secret)) return new DeviceResyncResult(401, id, null, "unauthorized");
        if (!_runners.TryGetValue(id, out var runner) || !_locks.TryGetValue(id, out var gate))
            return new DeviceResyncResult(404, id, null, "unknown device");
        if (!await gate.WaitAsync(0, ct)) return new DeviceResyncResult(409, id, null, "a resync is already running");
        try {
            return new DeviceResyncResult(200, id, await runner.RunOnceAsync(force, ct), null);
        } catch (Exception ex) {
            return new DeviceResyncResult(500, id, null, ex.Message);
        } finally {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<DeviceResyncResult>> HandleAllAsync(string? authorizationHeader, bool force,
        CancellationToken ct = default) {
        if (!BearerAuth.Matches(authorizationHeader, _secret)) return [new DeviceResyncResult(401, null, null, "unauthorized")];
        var results = new List<DeviceResyncResult>(_runners.Count);
        foreach (var id in _runners.Keys) results.Add(await HandleOneAsync(authorizationHeader, id, force, ct));
        return results;
    }
}
