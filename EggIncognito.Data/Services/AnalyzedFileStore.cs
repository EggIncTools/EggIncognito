using EggIncognito.Core;
using EggIncognito.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Data.Services;

public sealed class AnalyzedFileStore(EggIncognitoDbContext db, TimeProvider time) {
    public static string Sha256Hex(byte[] bytes) => Hashes.Sha256Hex(bytes);

    public Task<AnalyzedFile?> FindAsync(string fileSha, CancellationToken ct) => db.AnalyzedFiles.AsNoTracking().FirstOrDefaultAsync(f => f.FileSha == fileSha, ct);

    public async Task RecordAsync(Entry entry, CancellationToken ct) {
        bool exists = await db.AnalyzedFiles.AnyAsync(f => f.FileSha == entry.FileSha, ct);
        if (exists) return;
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
        try {
            await db.SaveChangesAsync(ct);
        } catch (DbUpdateException) {
            db.ChangeTracker.Clear();
        }
    }

    public sealed record Entry(
        string FileSha, string Source, string? Platform, string? ProtoSha,
        string? AppVersion, string? Build, string? ClientVersion, string? FileName);
}
