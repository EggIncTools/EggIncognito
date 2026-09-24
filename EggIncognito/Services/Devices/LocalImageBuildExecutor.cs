using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices;

public sealed class LocalImageBuildExecutor(DockerEngineClient docker) : IImageBuildExecutor {
    public async Task<ImageBuildOutcome> BuildAsync(
        Stream tarContext, string tag, IReadOnlyDictionary<string, string>? buildArgs, Func<string, Task> onLog,
        CancellationToken ct) {
        var built = await docker.BuildImageAsync(tarContext, tag, buildArgs, onLog, ct);
        if (!built.Ok) return new ImageBuildOutcome(false, tag, built.Note ?? "docker build failed");

        var listed = await docker.ListImagesAsync(tag, ct);
        bool present = listed.Ok
                       && (listed.Value?.Any(i => i.RepoTags.Contains(tag, StringComparer.Ordinal)) ?? false);
        return present
            ? new ImageBuildOutcome(true, tag, "built")
            : new ImageBuildOutcome(false, tag, "docker build reported success but the tag is not present");
    }

    public Task<DeviceResult<IReadOnlyList<DockerImage>>> ListAsync(string? reference, CancellationToken ct) =>
        docker.ListImagesAsync(reference, ct);

    public Task<DeviceResult> RemoveAsync(string tagOrId, CancellationToken ct) =>
        docker.RemoveImageAsync(tagOrId, ct);
}
