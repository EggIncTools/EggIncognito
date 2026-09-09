namespace EggIncognito.Models.Tools;

public sealed record BlobDecodeResult(string? Type, string? Json, bool Wrapped, int Confidence);
