namespace EggIncognito.Models.Inspector;

public sealed record DiagnoseError(int Offset, string Path, string? ResolvedPath, string Message);
