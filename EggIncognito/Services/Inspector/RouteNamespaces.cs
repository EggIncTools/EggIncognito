namespace EggIncognito.Services.Inspector;

public static class RouteNamespaces {
    public static string Of(string path) {
        int i = path.IndexOf('/');
        return (i < 0 ? path : path[..i]).ToLowerInvariant();
    }

    public static string LocalPath(string path) {
        int i = path.IndexOf('/');
        return i < 0 ? path : path[(i + 1)..];
    }

    public static List<IGrouping<string, T>> Group<T>(IEnumerable<T> items, Func<T, string> path) {
        return [.. items.GroupBy(x => Of(path(x)), StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal)];
    }
}
