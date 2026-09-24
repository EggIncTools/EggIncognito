using System.Globalization;
using EggIdentity.Contract;
using EggIncognito.Core;
using EggIncognito.Core.Services.ProtoExtract;
using EggIncognito.Runner.State;

namespace EggIncognito.Runner.Runners;

public sealed class IosRunner(
    string binaryPath, VersionState state, string package,
    Func<NewVersionEvent, Task> onNewVersion) : IDeviceRunner {
    public string Platform => "ios";

    public async Task<RunOutcome> RunOnceAsync(bool force, CancellationToken ct = default) {
        if (!File.Exists(binaryPath))
            return new RunOutcome(false, null, null, $"no staged ios binary at {binaryPath}");

        var macho = await File.ReadAllBytesAsync(binaryPath, ct);
        var build = Hashes.Sha256HexShort(macho, 16);
        if (!force && build == state.LastSeen())
            return new RunOutcome(false, build, null, "binary already seen");

        var result = MachoProtoExtractor.Extract(macho);
        if (!result.Ok || result.Proto is null)
            return new RunOutcome(false, build, null, result.Diagnostics);

        var protoBytes = System.Text.Encoding.UTF8.GetBytes(result.Proto);
        var protoSha = result.ProtoSha ?? Hashes.Sha256Hex(protoBytes);

        await onNewVersion(new NewVersionEvent {
            Package = package,
            Version = "",
            AppVersion = "",
            Build = build,
            ClientVersion = null,
            ApkRef = binaryPath,
            ProtoSha = protoSha,
            Platform = Platform,
            ProtoTextB64 = Convert.ToBase64String(protoBytes),
            DetectedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
        });
        state.Save(build);
        return new RunOutcome(true, build, protoSha, "emitted");
    }
}
