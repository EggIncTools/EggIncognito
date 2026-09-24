using System.Collections.Frozen;

namespace EggIncognito.Capture;

public static class HeaderRedactor {
    private static readonly FrozenSet<string> Sensitive = new[] {
        "authorization",
        "proxy-authorization",
        "cookie",
        "set-cookie",
        "x-api-key",
        "x-auth-token",
        "x-egg-inc-token",
        "x-cloud-trace-context"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static bool IsSensitive(string name) => Sensitive.Contains(name);

    public static (IReadOnlyList<DashboardHeader> redacted, IReadOnlyList<DashboardHeader> raw) Build(
        IReadOnlyList<HttpHeader>? headers) {
        if (headers is null || headers.Count == 0)
            return ([], []);

        var redacted = new List<DashboardHeader>(headers.Count);
        var raw = new List<DashboardHeader>(headers.Count);
        foreach (var h in headers) {
            bool sensitive = IsSensitive(h.Name);
            redacted.Add(new DashboardHeader(h.Name, sensitive ? "redacted" : h.Value, sensitive));
            raw.Add(new DashboardHeader(h.Name, h.Value, sensitive));
        }

        return (redacted, raw);
    }
}
