using System.Diagnostics;
using EggIncognito.Bot;
using EggIncognito.Capture;
using EggIncognito.Core.Services;

namespace EggIncognito.Services;

public sealed record RepoUrl(string Value);

public sealed class StatusSnapshotFactory(
    IAppMode mode,
    CaptureSession capture,
    ITransportPipeline pipeline,
    IConfiguration config,
    RepoUrl repoUrl,
    TimeProvider time) : IStatusProvider {
    private static readonly DateTimeOffset ProcessStart =
        new(Process.GetCurrentProcess().StartTime);

    private readonly bool _dbEnabled = !string.IsNullOrWhiteSpace(config.GetConnectionString("Postgres"));

    public StatusSnapshot Build() {
        var stats = capture.Hub.StatsSnapshot();
        (int ok, int empty, int missing) = ClassifyEndpoints();
        return new StatusSnapshot(
            mode.Mode.ToString(),
            mode.CanCapture,
            mode.CanWrite,
            capture.State.ToString(),
            capture.State == CaptureState.Running,
            stats.CapturedAuxbrain,
            stats.DeviceCount,
            stats.BytesCaptured,
            _dbEnabled,
            pipeline.CanSign,
            time.GetUtcNow() - ProcessStart,
            BuildInfo.FromAssembly(repoUrl.Value),
            ok, empty, missing);
    }

    private (int Ok, int Empty, int Missing) ClassifyEndpoints() {
        try {
            string root = ContentRoot.Resolve(config["ContentRoot"]);
            string yaml = Path.Combine(root, "RouteMap", "routes.yaml");
            string defaults = Path.Combine(root, "Endpoints", "default");
            var r = EndpointStatus.Classify(yaml, defaults);
            return (r.Ok.Count, r.Empty.Count, r.Missing.Count);
        } catch {
            return (0, 0, 0);
        }
    }
}
