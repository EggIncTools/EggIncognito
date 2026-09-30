using System.Globalization;
using EggIncognito.Core;
using EggIncognito.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Data.Services;

public sealed class AnalyzedFileStore(EggIncognitoDbContext db, TimeProvider time, ProtoRegistryStore registry) {
    public static string Sha256Hex(byte[] bytes) => Hashes.Sha256Hex(bytes);

    public Task<AnalyzedFile?> FindAsync(string fileSha, CancellationToken ct) => db.AnalyzedFiles.AsNoTracking().FirstOrDefaultAsync(f => f.FileSha == fileSha, ct);

    public async Task<Known?> FindKnownAsync(string fileSha, CancellationToken ct) {
        var file = await FindAsync(fileSha, ct);
        if (file is not { ProtoSha: { Length: > 0 } sha, AppVersion.Length: > 0, Build.Length: > 0, ClientVersion.Length: > 0 })
            return null;
        string? text = await CanonicalTextAsync(sha, ct);
        if (text is null) return null;
        int? cv = int.TryParse(file.ClientVersion, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : null;
        return new Known(sha, text, file.AppVersion, file.Build, cv);
    }

    private Task<string?> CanonicalTextAsync(string protoSha, CancellationToken ct) =>
        db.ProtoCanonicals.AsNoTracking()
            .Where(c => c.ProtoSha == protoSha && c.Ok && c.CanonicalText != null)
            .Select(c => c.CanonicalText)
            .FirstOrDefaultAsync(ct);

    public async Task RecordAsync(Entry entry, CancellationToken ct) {
        var row = await db.AnalyzedFiles.FirstOrDefaultAsync(f => f.FileSha == entry.FileSha, ct);
        if (row is null) {
            db.AnalyzedFiles.Add(new AnalyzedFile {
                FileSha = entry.FileSha,
                FirstSeen = time.GetUtcNow(),
                Source = entry.Source,
                Platform = entry.Platform,
                ProtoSha = entry.ProtoSha,
                AppVersion = entry.AppVersion,
                Build = entry.Build,
                ClientVersion = entry.ClientVersion,
                FileName = entry.FileName
            });
        } else {
            row.Platform = Fresh(entry.Platform, row.Platform);
            row.ProtoSha = Fresh(entry.ProtoSha, row.ProtoSha);
            row.AppVersion = Fresh(entry.AppVersion, row.AppVersion);
            row.Build = Fresh(entry.Build, row.Build);
            row.ClientVersion = Fresh(entry.ClientVersion, row.ClientVersion);
            row.FileName = Fresh(entry.FileName, row.FileName);
        }

        try {
            await db.SaveChangesAsync(ct);
        } catch (DbUpdateException) {
            db.ChangeTracker.Clear();
        }

        if (entry.ProtoSha is { Length: > 0 } sha && entry.ProtoText is { Length: > 0 } text)
            await registry.EnsureCanonicalAsync(sha, text, ct);
    }

    private static string? Fresh(string? incoming, string? stored) =>
        string.IsNullOrWhiteSpace(incoming) ? stored : incoming;

    public sealed record Entry(
        string FileSha, string Source, string? Platform, string? ProtoSha,
        string? AppVersion, string? Build, string? ClientVersion, string? FileName, string? ProtoText = null);

    public sealed record Known(string ProtoSha, string ProtoText, string? AppVersion, string? Build, int? ClientVersion);
}
