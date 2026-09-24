using System.Globalization;
using EggIdentity.Contract;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Runner.Adb;
using EggIncognito.Runner.Extract;
using EggIncognito.Runner.State;

namespace EggIncognito.Runner.Runners;

public sealed class AndroidRunner(
    IAdbClient adb, IProtoExtractor proto, VersionState state, IClientVersionReader clientVersion,
    ClientVersionState cvState, string package, string apkStashDir,
    Func<NewVersionEvent, Task> onNewVersion) : IDeviceRunner {
    public string Platform => "android";

    public async Task<RunOutcome> RunOnceAsync(bool force, CancellationToken ct = default) {
        var dumpsys = await adb.DumpsysPackageAsync(package, ct);
        var (appVersion, build) = DeviceParsing.AndroidVersion(dumpsys);
        if (string.IsNullOrEmpty(build))
            return new RunOutcome(false, null, null, "no versionCode in dumpsys");
        if (!force && build == state.LastSeen())
            return new RunOutcome(false, build, null, "build already seen");

        var apkPath = Path.Combine(apkStashDir, $"egginc-{build}.apk");
        await adb.PullArmApkAsync(package, apkPath, ct);

        var extraction = proto.Extract(apkPath);
        var protoBytes = extraction.ProtoText;
        var protoSha = extraction.ProtoSha;

        var cv = clientVersion.Read(apkPath, cvState.Last());
        if (cv is not null && int.TryParse(cv, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cvNum))
            cvState.Save(cvNum);

        await onNewVersion(new NewVersionEvent {
            Package = package,
            Version = appVersion ?? "",
            AppVersion = appVersion ?? "",
            Build = build,
            ClientVersion = cv,
            ApkRef = apkPath,
            ProtoSha = protoSha,
            Platform = Platform,
            ProtoTextB64 = Convert.ToBase64String(protoBytes),
            DetectedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
        });
        state.Save(build);
        return new RunOutcome(true, build, protoSha, "emitted");
    }
}
