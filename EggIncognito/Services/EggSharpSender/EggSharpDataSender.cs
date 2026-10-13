using EggIncognito.Data.Services;
using EggSharp.GameData;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Services.EggSharpSender;

public sealed record DataPushOutcome(string Status, string? Sha, IReadOnlyList<string> Changed, string? Detail);

public sealed class EggSharpDataSender(
    EggIncognitoDbContext db,
    ILogger<EggSharpDataSender> logger,
    TimeProvider time,
    EggSharpGitHub? github = null) {
    public const string DataDir = "EggSharp.GameData/Data";
    public const string IncomingBranch = "data/incoming";
    public const string PromoteContext = "eggsharp/data-promote";

    private static readonly TimeSpan PromoteTimeout = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(20);

    public bool Enabled => github is not null;

    public async Task<DataPushOutcome> PushAsync(CancellationToken ct) {
        if (github is null) return new DataPushOutcome("disabled", null, [], "EGGSHARP_APP_* not configured");

        var ours = await db.GameDataDocuments.AsNoTracking()
            .ToDictionaryAsync(d => d.Id, d => d.Json, StringComparer.Ordinal, ct);

        string mainSha = await github.MainShaAsync(ct);
        var candidate = new Dictionary<string, string>(StringComparer.Ordinal);
        var changed = new List<string>();
        foreach (string id in ContributedIds) {
            string? theirs = await github.FileTextAsync($"{DataDir}/{id}.json", mainSha, ct);
            if (ours.TryGetValue(id, out string? mine)) {
                candidate[id] = mine;
                if (!string.Equals(Normalize(mine), Normalize(theirs), StringComparison.Ordinal)) changed.Add(id);
            } else if (theirs is not null) {
                candidate[id] = theirs;
            }
        }

        if (changed.Count == 0) return new DataPushOutcome("current", null, [], null);

        try {
            GameDataProvider.Verify(candidate);
        } catch (GameDataSchemaException ex) {
            logger.LogWarning("EggSharp data push rejected by Verify: {Error}", ex.Message);
            return new DataPushOutcome("rejected", null, changed, ex.Message);
        }

        string version = BinaryVersions(changed, candidate);
        string sha = await github.CommitFilesAsync(mainSha,
            changed.ToDictionary(id => $"{DataDir}/{id}.json", id => candidate[id], StringComparer.Ordinal),
            $"data: {string.Join(", ", changed)} from {version}", ct);
        await github.SetBranchAsync(IncomingBranch, sha, ct);
        logger.LogInformation("EggSharp data pushed to {Branch} at {Sha}: {Ids}", IncomingBranch, sha, string.Join(", ", changed));

        return await AwaitPromoteAsync(sha, changed, ct);
    }

    private async Task<DataPushOutcome> AwaitPromoteAsync(string sha, IReadOnlyList<string> changed, CancellationToken ct) {
        var deadline = time.GetUtcNow() + PromoteTimeout;
        while (time.GetUtcNow() < deadline) {
            var status = await github!.CommitStatusAsync(sha, PromoteContext, ct);
            if (status is { State: "success" or "failure" or "error" } s)
                return new DataPushOutcome(s.State == "success" ? "promoted" : "failed", sha, changed, s.TargetUrl);
            await Task.Delay(PollInterval, time, ct);
        }

        return new DataPushOutcome("timeout", sha, changed, $"no {PromoteContext} result within {PromoteTimeout}");
    }

    private static IEnumerable<string> ContributedIds =>
        GameDataProvider.DocumentIds.Concat(GameDataProvider.AuxiliaryDocumentIds)
            .Concat(GameDataProvider.OptionalAuxiliaryDocumentIds)
            .Distinct(StringComparer.Ordinal);

    private static string BinaryVersions(IEnumerable<string> ids, Dictionary<string, string> docs) {
        var versions = ids.Select(id => System.Text.Json.Nodes.JsonNode.Parse(docs[id])?["binaryVersion"]?.GetValue<string>())
            .OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        return versions.Count == 0 ? "unknown" : string.Join(", ", versions);
    }

    private static string? Normalize(string? json) =>
        json is null ? null : System.Text.Json.Nodes.JsonNode.Parse(json)?.ToJsonString();
}
