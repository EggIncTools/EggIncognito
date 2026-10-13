using EggIncognito.Core.Services.ProtoExtract;
using EggIncognito.Data.Services;

namespace EggIncognito.Services.EggSharpSender;

public sealed record ProtoSendOutcome(string Status, string? Platform, string? Build, string? Sha, string? Detail);

public sealed class EggSharpProtoSender(
    IServiceProvider services,
    ILogger<EggSharpProtoSender> logger,
    EggSharpGitHub? github = null) : IProtoUpsertObserver {
    public const string ProtoPath = "EggSharp.Protos/proto/ei.proto";

    public bool Enabled => github is not null;

    private ProtoRegistryStore Store => services.GetRequiredService<ProtoRegistryStore>();

    public async Task OnUpsertAsync(ProtoUpsertNotice notice, CancellationToken ct) {
        if (github is null || !notice.ProtoChanged || !notice.HasProtoText) return;
        if (ProtoVersionQuality.Flaws(notice.Platform, notice.Build, notice.ClientVersion, notice.ProtoSha,
                notice.HasProtoText).Count > 0) return;

        try {
            var outcome = await SendAsync(notice.Platform, notice.Build, notice.AppVersion, ct);
            if (outcome.Status == "opened") logger.LogInformation("EggSharp proto PR opened: {Url}", outcome.Detail);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            logger.LogWarning(ex, "EggSharp proto PR failed for {Platform} {Build}", notice.Platform, notice.Build);
        }
    }

    public async Task<ProtoSendOutcome> SendLatestAsync(CancellationToken ct) {
        if (github is null) return new ProtoSendOutcome("disabled", null, null, null, "EGGSHARP_APP_* not configured");

        var rows = await Store.ListAsync(null, ct);
        var latest = rows
            .Where(r => !string.IsNullOrEmpty(r.ProtoSha) && r.CanonicalId is null)
            .Where(r => ProtoVersionQuality.Flaws(r.Platform, r.Build, r.ClientVersion, r.ProtoSha, true).Count == 0)
            .Select(r => (Row: r, Key: ProtoVersionQuality.LatestSortKey(r.Platform, r.Build, r.AppVersion)))
            .Where(x => x.Key != long.MinValue)
            .OrderByDescending(x => x.Key)
            .Select(x => x.Row)
            .FirstOrDefault();
        if (latest is null) return new ProtoSendOutcome("none", null, null, null, "no clean registry version with a proto");

        return await SendAsync(latest.Platform, latest.Build, latest.AppVersion, ct);
    }

    private async Task<ProtoSendOutcome> SendAsync(string platform, string build, string appVersion, CancellationToken ct) {
        var (_, canonical) = await Store.GetCanonicalForVersionAsync(platform, build, ct);
        if (!canonical.Ok || canonical.Text is null || canonical.Sha is null)
            return new ProtoSendOutcome("no-proto", platform, build, null, canonical.Error);

        string mainSha = await github!.MainShaAsync(ct);
        string? onMain = await github.FileTextAsync(ProtoPath, mainSha, ct);
        if (onMain is not null && ProtoCanonicalForm.Normalize(onMain) is { Ok: true, Sha: { } mainCanon }
            && string.Equals(mainCanon, canonical.Sha, StringComparison.Ordinal))
            return new ProtoSendOutcome("current", platform, build, canonical.Sha, "EggSharp main already has this proto");

        string branch = $"egi/proto-{canonical.Sha[..12]}";
        if (await github.OpenPullExistsAsync(branch, ct))
            return new ProtoSendOutcome("pending", platform, build, canonical.Sha, $"open PR already on {branch}");

        string title = $"proto: {platform} {appVersion} ({build})";
        string commit = await github.CommitFilesAsync(mainSha,
            new Dictionary<string, string>(StringComparer.Ordinal) { [ProtoPath] = canonical.Text }, title, ct);
        await github.SetBranchAsync(branch, commit, ct);

        string body = string.Join('\n',
            "| platform | appVersion | build | protoSha |",
            "|---|---|---|---|",
            $"| {platform} | {appVersion} | {build} | `{canonical.Sha}` |");
        string url = await github.OpenPullAsync(branch, title, body, ct);
        return new ProtoSendOutcome("opened", platform, build, canonical.Sha, url);
    }
}
