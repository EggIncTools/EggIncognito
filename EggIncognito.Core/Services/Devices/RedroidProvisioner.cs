using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace EggIncognito.Core.Services.Devices;

public sealed class RedroidProvisioner(
    DockerEngineClient docker,
    VirtualDeviceConfig config,
    IHostFacts host,
    TimeProvider time,
    ILogger<RedroidProvisioner> logger) : IDeviceProvisioner {
    public const string OwnerLabel = "egi.virtual";
    public const string KindLabel = "egi.kind";
    public const string NamePrefix = "egi-vd-";
    public const int AdbPort = 5555;

    public const string OwnerFilter = OwnerLabel + "=1";

    private string? _network;

    public static readonly string[] BootArgs = [
        "androidboot.redroid_width=720",
        "androidboot.redroid_height=1280",
        "androidboot.redroid_dpi=320",
        "androidboot.redroid_gpu_mode=guest"
    ];

    public string Kind => "redroid";

    public ProvisionerCapabilities Capabilities =>
        ProvisionerCapabilities.Create | ProvisionerCapabilities.StartStop |
        ProvisionerCapabilities.Destroy | ProvisionerCapabilities.List;

    public static string TargetFor(string instanceId) => $"{instanceId}:{AdbPort}";

    public static string VolumeFor(string instanceId) => $"{instanceId}-data";

    private static bool IsHostMode(string network) =>
        string.Equals(network, DockerEngineClient.HostNetwork, StringComparison.OrdinalIgnoreCase);

    private static string? SerialFor(string name, string? ip, bool hostMode) =>
        hostMode ? ip is { Length: > 0 } ? $"{ip}:{AdbPort}" : null : TargetFor(name);

    private async Task<DeviceResult<string>> NetworkAsync(CancellationToken ct) {
        if (config.Network is { Length: > 0 } configured) return DeviceResult<string>.Success(configured);
        if (_network is { } cached) return DeviceResult<string>.Success(cached);

        var facts = await host.GetAsync(ct);
        if (!facts.Ok || facts.Value is not { } value)
            return new DeviceResult<string>(facts.Outcome, null, facts.Note);
        if (value.Network is not { Length: > 0 } net) {
            return DeviceResult<string>.Error(
                "the host reports no docker network for the app container; set Devices:Virtual:Network");
        }

        _network = net;
        return DeviceResult<string>.Success(net);
    }

    private async Task<bool> HostModeAsync(CancellationToken ct) {
        var net = await NetworkAsync(ct);
        return net.Ok && net.Value is { } n && IsHostMode(n);
    }

    public async Task<DeviceResult<ProvisionedInstance>> CreateAsync(ProvisionSpec spec, CancellationToken ct) {
        var ping = await docker.PingAsync(ct);
        if (!ping.Ok) return DeviceResult<ProvisionedInstance>.Unsupported(ping.Note);

        var existing = await docker.ListAsync(OwnerFilter, ct);
        if (!existing.Ok) return new DeviceResult<ProvisionedInstance>(existing.Outcome, null, existing.Note);
        int mine = existing.Value?.Count ?? 0;
        if (mine >= config.MaxInstances) {
            return DeviceResult<ProvisionedInstance>.Error(
                $"virtual device cap reached ({mine}/{config.MaxInstances}); destroy one before creating another");
        }

        var network = await NetworkAsync(ct);
        if (!network.Ok || network.Value is not { } net)
            return DeviceResult<ProvisionedInstance>.Error(network.Note ?? "could not discover the app docker network");
        bool hostMode = IsHostMode(net);

        string name = NewName();
        string image = string.IsNullOrWhiteSpace(spec.Image) ? config.Image : spec.Image;
        var labels = new Dictionary<string, string>(StringComparer.Ordinal) {
            [OwnerLabel] = "1",
            [KindLabel] = Kind,
            ["egi.instance"] = name
        };
        if (!string.IsNullOrWhiteSpace(spec.Label)) labels["egi.label"] = spec.Label;

        var created = await docker.CreateAsync(
            new DockerCreateSpec(name, image, BootArgs, [$"{VolumeFor(name)}:/data"], hostMode ? null : net, labels),
            ct);
        if (!created.Ok || created.Value is not { } id)
            return DeviceResult<ProvisionedInstance>.Error(created.Note ?? "container create failed");

        var started = await docker.StartAsync(id, ct);
        if (!started.Ok) {
            await docker.RemoveAsync(id, ct);
            await docker.RemoveVolumeAsync(VolumeFor(name), ct);
            return DeviceResult<ProvisionedInstance>.Error(started.Note ?? "container start failed");
        }

        string? ip = null;
        if (hostMode) {
            var inspect = await docker.InspectAsync(id, ct);
            ip = inspect.Value?.IpAddress;
        }

        string where = hostMode
            ? ip is { Length: > 0 } ? $"reachable at {ip} from the host network" : "waiting for a bridge address"
            : $"attached to docker network {net}";
        logger.LogInformation("virtual device: created {Name} from {Image}, {Where}", name, image, where);

        return DeviceResult<ProvisionedInstance>.Success(new ProvisionedInstance(
            name, Kind, image, ProvisionStates.Creating, SerialFor(name, ip, hostMode), id, time.GetUtcNow(), where));
    }

    public async Task<DeviceResult> StartAsync(string instanceId, CancellationToken ct) {
        var guard = Guard(instanceId);
        return guard ?? await docker.StartAsync(instanceId, ct);
    }

    public async Task<DeviceResult> StopAsync(string instanceId, CancellationToken ct) {
        var guard = Guard(instanceId);
        return guard ?? await docker.StopAsync(instanceId, 20, ct);
    }

    public async Task<DeviceResult> DestroyAsync(string instanceId, CancellationToken ct) {
        if (Guard(instanceId) is { } guard) return guard;

        await docker.StopAsync(instanceId, 15, ct);
        var removed = await docker.RemoveAsync(instanceId, ct);
        if (!removed.Ok) return removed;

        var volume = await docker.RemoveVolumeAsync(VolumeFor(instanceId), ct);
        if (!volume.Ok)
            logger.LogWarning("virtual device: {Id} removed but its data volume lingers: {Note}", instanceId,
                volume.Note);

        logger.LogInformation("virtual device: destroyed {Id}", instanceId);
        return DeviceResult.Success(volume.Ok ? null : "container removed, volume left behind");
    }

    public async Task<DeviceResult<IReadOnlyList<ProvisionedInstance>>> ListAsync(CancellationToken ct) {
        var listed = await docker.ListAsync(OwnerFilter, ct);
        if (!listed.Ok || listed.Value is not { } rows)
            return new DeviceResult<IReadOnlyList<ProvisionedInstance>>(listed.Outcome, null, listed.Note);

        bool hostMode = await HostModeAsync(ct);
        var mapped = rows
            .Where(c => c.Name.StartsWith(NamePrefix, StringComparison.Ordinal))
            .Select(c => new ProvisionedInstance(
                c.Name, Kind, c.Image, StateOf(c.State), SerialFor(c.Name, c.IpAddress, hostMode), c.Id, c.CreatedAt,
                c.Status))
            .ToList();
        return DeviceResult<IReadOnlyList<ProvisionedInstance>>.Success(mapped);
    }

    private static string StateOf(string dockerState) => dockerState switch {
        "running" => ProvisionStates.Booting,
        "created" or "restarting" => ProvisionStates.Creating,
        "paused" or "exited" => ProvisionStates.Stopped,
        _ => ProvisionStates.Failed
    };

    private DeviceResult? Guard(string instanceId) {
        if (!docker.Available) return DeviceResult.Unsupported($"{docker.Endpoint.Describe()} is not available");
        return instanceId.StartsWith(NamePrefix, StringComparison.Ordinal)
            ? null
            : DeviceResult.Error($"'{instanceId}' is not a provisioned virtual device");
    }

    private static string NewName() =>
        NamePrefix + Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
}
