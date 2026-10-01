using EggIdentity.UI;

namespace EggIncognito.Services.Contracts;

public sealed class ContractsWorkbenchState : WorkbenchStateBase {
    public override IReadOnlyList<(string Key, string Label, int? Count)> Modes { get; } = [];

    public override string HashPrefix => "contracts";

    public override string? Hash() => HashPrefix;

    public override bool ApplyHash(string? hash) => OwnsHash(hash);
}
