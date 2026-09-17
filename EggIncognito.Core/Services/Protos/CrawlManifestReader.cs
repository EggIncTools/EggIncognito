using System.Globalization;
using System.IO.Compression;
using System.Text.Json;

namespace EggIncognito.Core.Services.Protos;

public static class CrawlManifestReader {
    private static readonly JsonSerializerOptions ManifestJsonOptions = JsonPresets.Web;

    private static int ConfidenceRank(string? c) => c switch {
        "version-file" => 3,
        "subject" => 2,
        "tree-scan" => 1,
        _ => 0
    };

    private static bool IsTrusted(string? c) => c is "version-file" or "subject";

    private static string NormalizePlatform(string? p) => p?.ToUpperInvariant() switch {
        "IOS" => "ios",
        "ANDROID" => "android",
        _ => "android"
    };

    public static IReadOnlyList<CrawlRecord> Read(byte[] zipBytes) {
        using var ms = new MemoryStream(zipBytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);

        var manifestEntry = zip.GetEntry("manifest.json")
                            ?? zip.Entries.FirstOrDefault(e =>
                                e.Name.Equals("manifest.json", StringComparison.OrdinalIgnoreCase));
        if (manifestEntry is null) return [];

        List<ManifestRow>? rows;
        using (var s = manifestEntry.Open())
            rows = JsonSerializer.Deserialize<List<ManifestRow>>(s, ManifestJsonOptions);

        if (rows is null) return [];

        var bestPerSha = rows
            .Where(r => !string.IsNullOrEmpty(r.ProtoSha256) && !string.IsNullOrEmpty(r.SnapshotFile))
            .GroupBy(r => r.ProtoSha256!)
            .Select(g => g
                .OrderByDescending(r => ConfidenceRank(r.VersionConfidence))
                .ThenBy(r => r.Date ?? DateTimeOffset.MaxValue)
                .First());

        var result = new List<CrawlRecord>();
        foreach (var r in bestPerSha) {
            var snap = zip.GetEntry(r.SnapshotFile!);
            if (snap is null) continue;
            string text;
            using (var sr = new StreamReader(snap.Open())) text = sr.ReadToEnd();

            bool trusted = IsTrusted(r.VersionConfidence);
            result.Add(new CrawlRecord(
                NormalizePlatform(r.Platform),
                trusted ? r.AppVersion : null,
                trusted ? r.Build : null,
                trusted ? r.ClientVersion?.ToString(CultureInfo.InvariantCulture) : null,
                r.ProtoSha256!, text, r.Repo, r.Commit, r.Date?.ToUniversalTime(),
                string.IsNullOrEmpty(r.VersionConfidence) ? null : r.VersionConfidence));
        }

        return result;
    }

    public sealed record CrawlRecord(
        string Platform,
        string? AppVersion,
        string? Build,
        string? ClientVersion,
        string ProtoSha,
        string ProtoText,
        string? OriginRepo,
        string? OriginCommit,
        DateTimeOffset? OriginDate,
        string? Confidence);

    private sealed record ManifestRow(
        string? Repo,
        string? Commit,
        DateTimeOffset? Date,
        string? ProtoSha256,
        int? ClientVersion,
        string? AppVersion,
        string? Build,
        string? SnapshotFile,
        string? VersionConfidence,
        string? Platform);
}
