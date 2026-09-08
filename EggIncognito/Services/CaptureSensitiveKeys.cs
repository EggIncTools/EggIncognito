using EggIncognito.Core.Services;

namespace EggIncognito.Services;

public static class CaptureSensitiveKeys {
    public static readonly IReadOnlyList<string> All = [.. Redactor.SensitiveFieldNames, "eiUserId", "userId"];
}
