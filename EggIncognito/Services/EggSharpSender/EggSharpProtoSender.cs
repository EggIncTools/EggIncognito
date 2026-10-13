using EggIncognito.Core.Services.ProtoExtract;
using EggIncognito.Data.Services;

namespace EggIncognito.Services.EggSharpSender;

public sealed class EggSharpProtoSender(
    ProtoRegistryStore store,
    ILogger<EggSharpProtoSender> logger,
    EggSharpGitHub? github = null) : IProtoUpsertObserver {
    public const string ProtoPath = "EggSharp.Protos/proto/ei.proto";

    public async Task OnUpsertAsync(ProtoUpsertNotice notice, CancellationToken ct) {
        if (github is null || !notice.ProtoChanged || !notice.HasProtoText) return;
        if (ProtoVersionQuality.Flaws(notice.Platform, notice.Build, notice.ClientVersion, notice.ProtoSha,
                notice.HasProtoText).Count > 0) return;

        try {
            string? url = await SendAsync(notice, ct);
            if (url is not null) logger.LogInformation("EggSharp proto PR opened: {Url}", url);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            logger.LogWarning(ex, "EggSharp proto PR failed for {Platform} {Build}", notice.Platform, notice.Build);
        }
    }

    private async Task<string?> SendAsync(ProtoUpsertNotice notice, CancellationToken ct) {
        var (_, canonical) = await store.GetCanonicalForVersionAsync(notice.Platform, notice.Build, ct);
        if (!canonical.Ok || canonical.Text is null || canonical.Sha is null) return null;

        string mainSha = await github!.MainShaAsync(ct);
        string? onMain = await github.FileTextAsync(ProtoPath, mainSha, ct);
        if (onMain is not null && ProtoCanonicalForm.Normalize(onMain) is { Ok: true, Sha: { } mainCanon }
            && string.Equals(mainCanon, canonical.Sha, StringComparison.Ordinal)) return null;

        string branch = $"egi/proto-{canonical.Sha[..12]}";
        if (await github.OpenPullExistsAsync(branch, ct)) return null;

        string commit = await github.CommitFilesAsync(mainSha,
            new Dictionary<string, string>(StringComparer.Ordinal) { [ProtoPath] = canonical.Text },
            $"proto: {notice.Platform} {notice.AppVersion} ({notice.Build})", ct);
        await github.SetBranchAsync(branch, commit, ct);

        string body = string.Join('\n',
            "| platform | appVersion | build | protoSha |",
            "|---|---|---|---|",
            $"| {notice.Platform} | {notice.AppVersion} | {notice.Build} | `{canonical.Sha}` |");
        return await github.OpenPullAsync(branch, $"proto: {notice.Platform} {notice.AppVersion} ({notice.Build})", body, ct);
    }
}
