using System.Globalization;

namespace EggIncognito.Core.Services.Devices;

public static class IslandScope {
    public static string User(int userId) => userId.ToString(CultureInfo.InvariantCulture);

    public static string UserFlag(int? userId) =>
        userId is { } id ? $" --user {User(id)}" : "";
}
