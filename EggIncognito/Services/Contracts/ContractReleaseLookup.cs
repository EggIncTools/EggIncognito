using EggIncognito.Data.Services;
using Ei;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Services.Contracts;

public sealed class ContractReleaseLookup(IServiceScopeFactory scopes) : IContractReleases {
    public async Task<Contract?> LatestAsync(string contractId, CancellationToken ct) {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EggIncognitoDbContext>();
        var row = await db.ContractReleases.AsNoTracking()
            .Where(c => c.ContractId == contractId)
            .OrderByDescending(c => c.StartTime)
            .FirstOrDefaultAsync(ct);
        return row is null || row.Proto.Length == 0 ? null : Contract.Parser.ParseFrom(row.Proto);
    }
}

internal sealed class NoContractReleases : IContractReleases {
    public Task<Contract?> LatestAsync(string contractId, CancellationToken ct) => Task.FromResult<Contract?>(null);
}
