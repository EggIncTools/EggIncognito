using Ei;

namespace EggIncognito.Services.Contracts;

public interface IContractReleases {
    Task<Contract?> LatestAsync(string contractId, CancellationToken ct);
}
