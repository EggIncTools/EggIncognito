using System.Text.Json;

namespace EggIncognito.Core.Services.Devices;

public sealed record IpaStoreVersion(string AppVersion, DateTimeOffset? ReleaseDate);

public sealed record IpaStoreLookup(bool Ok, IReadOnlyList<IpaStoreVersion> Versions, string Diagnostics);

public sealed class IpaToolStoreVersions(IProcessRunner runner, IpaToolsConfig config) {
    public async Task<IpaStoreLookup> LookupAsync(CancellationToken ct) {
        if (!config.Enabled)
            return new IpaStoreLookup(false, [], $"ipatool not present at {config.IpaToolPath}");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(config.TimeoutSeconds));

        var result = await runner.RunAsync(config.IpaToolPath,
            ["search", config.BundleId, "--limit", "5", "--format", "json", "--non-interactive"], cts.Token);

        if (result.ExitCode != 0)
            return new IpaStoreLookup(false, [], Trim($"ipatool search exit {result.ExitCode}: {result.Stderr}"));

        return Parse(result.Stdout);
    }

    internal static IpaStoreLookup Parse(string stdout) {
        if (string.IsNullOrWhiteSpace(stdout)) return new IpaStoreLookup(false, [], "ipatool returned no output");

        foreach (string line in Lines(stdout)) {
            JsonElement root;
            try {
                root = JsonElement.Parse(line);
            } catch (JsonException) {
                continue;
            }

            var apps = FindApps(root);
            if (apps is null) continue;

            var versions = new List<IpaStoreVersion>();
            foreach (var app in apps.Value.EnumerateArray()) {
                string? version = Str(app, "version") ?? Str(app, "bundleVersion");
                if (string.IsNullOrWhiteSpace(version)) continue;
                versions.Add(new IpaStoreVersion(version, Date(app)));
            }

            if (versions.Count > 0)
                return new IpaStoreLookup(true, versions, $"ipatool search: {versions.Count} result(s)");
        }

        return new IpaStoreLookup(false, [], Trim($"ipatool output had no usable version field: {stdout}"));
    }

    private static JsonElement? FindApps(JsonElement root) {
        if (root.ValueKind == JsonValueKind.Array) return root;
        if (root.ValueKind != JsonValueKind.Object) return null;
        foreach (string key in (string[])["apps", "results", "data"]) {
            if (root.TryGetProperty(key, out var found) && found.ValueKind == JsonValueKind.Array) return found;
        }
        return null;
    }

    private static string? Str(JsonElement el, string name) =>
        el.ValueKind == JsonValueKind.Object
        && el.TryGetProperty(name, out var v)
        && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static DateTimeOffset? Date(JsonElement app) {
        foreach (string key in (string[])["currentVersionReleaseDate", "releaseDate"]) {
            string? raw = Str(app, key);
            if (DateTimeOffset.TryParse(raw, out var parsed)) return parsed;
        }
        return null;
    }

    private static IEnumerable<string> Lines(string stdout) {
        yield return stdout;
        foreach (string line in stdout.Split('\n')) {
            string trimmed = line.Trim();
            if (trimmed.Length > 1) yield return trimmed;
        }
    }

    private static string Trim(string s) => s.Length <= 400 ? s.Trim() : s[..400].Trim();
}
