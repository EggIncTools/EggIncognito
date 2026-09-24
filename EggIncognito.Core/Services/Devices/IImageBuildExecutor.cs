namespace EggIncognito.Core.Services.Devices;

public sealed record ImageBuildOutcome(bool Ok, string Tag, string? Note);

public interface IImageBuildExecutor {
    Task<ImageBuildOutcome> BuildAsync(
        Stream tarContext, string tag, IReadOnlyDictionary<string, string>? buildArgs, Func<string, Task> onLog,
        CancellationToken ct);

    Task<DeviceResult<IReadOnlyList<DockerImage>>> ListAsync(string? reference, CancellationToken ct);

    Task<DeviceResult> RemoveAsync(string tagOrId, CancellationToken ct);
}
