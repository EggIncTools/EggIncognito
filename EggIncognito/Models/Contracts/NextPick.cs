namespace EggIncognito.Models.Contracts;

public sealed record NextPick(string ContractId, string Name, double SlotTime, double Probability, string? EggId, string? IconUrl);
