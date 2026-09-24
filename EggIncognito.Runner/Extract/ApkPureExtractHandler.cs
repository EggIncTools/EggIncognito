using System.Globalization;
using EggIdentity.Contract;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Core.Services.ProtoExtract;

namespace EggIncognito.Runner.Extract;

public sealed record ExtractResult(int Status, string? Build, string? ProtoSha, string? Error, string? Detail);

public sealed class ApkPureExtractHandler(
    string secret, ApkPureDownloader downloader, IProtoExtractor extractor,
    IClientVersionReader clientVersion, State.ClientVersionState cvState,
    Func<NewVersionEvent, Task> postEvent) : IDisposable {
    private readonly SemaphoreSlim _lock = new(1, 1);

    public void Dispose() => _lock.Dispose();

    public async Task<ExtractResult> HandleAsync(string? authHeader, string? appVersion) {
        if (!BearerAuth.Matches(authHeader, secret))
            return new ExtractResult(401, null, null, "unauthorized", null);
        if (!await _lock.WaitAsync(0))
            return new ExtractResult(409, null, null, "an extract is already running", null);
        try {
            if (string.IsNullOrWhiteSpace(appVersion))
                return new ExtractResult(400, null, null, "appVersion required", null);

            var downloaded = await downloader.DownloadApkAsync(appVersion);
            if (downloaded is null)
                return new ExtractResult(502, null, null, "download failed", null);
            var armSplit = ApkPureDownloader.ExtractArmSplit(downloaded);
            if (armSplit is null)
                return new ExtractResult(502, null, null, "no arm split in download", null);

            var tmp = DeviceShell.NewTempPath(".apk");
            ProtoExtraction extraction;
            string? cv;
            try {
                await File.WriteAllBytesAsync(tmp, armSplit);
                extraction = extractor.Extract(tmp);
                cv = clientVersion.Read(tmp, cvState.Last());
            } finally {
                DeviceShell.TryDelete(tmp);
            }
            if (cv is not null && int.TryParse(cv, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cvNum))
                cvState.Save(cvNum);

            var build = ApkVersionCode.Read(armSplit);
            var protoBytes = extraction.ProtoText;
            var protoSha = extraction.ProtoSha;
            await postEvent(new NewVersionEvent {
                Package = "com.auxbrain.egginc",
                Version = appVersion,
                AppVersion = appVersion,
                Build = build ?? "",
                ClientVersion = cv,
                ApkRef = $"apkpure:{appVersion}",
                ProtoSha = protoSha,
                Platform = "android",
                ProtoTextB64 = Convert.ToBase64String(protoBytes),
                DetectedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            });
            return new ExtractResult(200, build, protoSha, null, "extracted and posted");
        } catch (Exception ex) {
            return new ExtractResult(500, null, null, ex.Message, null);
        } finally {
            _lock.Release();
        }
    }
}
