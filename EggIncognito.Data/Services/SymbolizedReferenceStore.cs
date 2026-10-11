using EggIncognito.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Data.Services;

public sealed class SymbolizedReferenceStore(EggIncognitoDbContext db) {
    public Task<SymbolizedBinary?> GetAsync(string platform, string version, CancellationToken ct = default) =>
        db.SymbolizedBinaries.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Platform == platform && b.AppVersion == version, ct);

    public Task<SymbolizedBinary?> GetLatestAsync(string platform, CancellationToken ct = default) =>
        db.SymbolizedBinaries.AsNoTracking()
            .Where(b => b.Platform == platform)
            .OrderByDescending(b => b.UploadedAt)
            .FirstOrDefaultAsync(ct);
}
