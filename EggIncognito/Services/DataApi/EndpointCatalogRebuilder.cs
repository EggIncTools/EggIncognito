using EggIncognito.Core.Services;
using EggIncognito.Core.Services.ProtoExtract;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Models.Routes;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Services.DataApi;

public sealed class EndpointCatalogRebuilder(
    IServiceProvider services,
    GameBinaryProvider binaries,
    RouteCatalog yaml) {
    public async Task<EndpointRebuildResult> RebuildAsync(CancellationToken ct) {
        var found = await binaries.GetExtractionCandidatesAsync(ct);
        if (found.Candidates.Count == 0) {
            return new EndpointRebuildResult(0, 0, 0, null,
                found.Rejected.Count == 0 ? "no extraction binary available" : found.Diagnostics);
        }

        var attempts = new List<Attempt>();
        foreach (var c in found.Candidates) {
            var syms = IsElf(c.Bytes) ? ElfSymbols.Read(c.Bytes) : c.Symbols ?? MachoSymbols.Read(c.Bytes);
            var extracted = EndpointCatalogExtractor.ExtractWith(c.Bytes, syms);
            var filtered = extracted.Ok
                ? Filter(extracted.Endpoints, yaml.ExcludedPaths)
                : [];
            attempts.Add(new Attempt(c, filtered,
                $"{c.Platform} {c.Version}: {(extracted.Ok ? $"{filtered.Count} endpoints from {syms.Count} symbols" : extracted.Diagnostics)}"));
        }

        var contributors = attempts.Where(a => a.Endpoints.Count > 0).ToList();
        if (contributors.Count == 0) {
            return new EndpointRebuildResult(0, 0, 0, null,
                string.Join("; ", found.Rejected.Concat(attempts.Select(a => a.Note))));
        }

        var notUsed = found.Rejected
            .Concat(attempts.Where(a => a.Endpoints.Count == 0).Select(a => a.Note)).ToList();
        var inputs = contributors
            .Select(a => new MergeContributor(a.Candidate.Platform, a.Candidate.Version, a.Endpoints))
            .ToList();
        var merged = Merge(inputs);

        var db = services.GetService(typeof(EggIncognitoDbContext)) as EggIncognitoDbContext
                 ?? throw new InvalidOperationException("no database configured");

        var now = DateTimeOffset.UtcNow;
        var existing = await db.RouteBinaryCatalogs.ToDictionaryAsync(x => x.Path, StringComparer.Ordinal, ct);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var binaryRows = new List<BinaryRouteInfo>();

        foreach (var m in merged) {
            var e = m.Descriptor;
            string path = e.Path!;
            if (!seen.Add(path)) continue;

            if (existing.TryGetValue(path, out var row)) {
                row.Method = e.Method;
                row.RequestType = e.RequestType;
                row.ResponseType = e.ResponseType;
                row.RequestWrapped = e.RequestWrapped;
                row.ResponseWrapped = e.ResponseWrapped;
                row.BinaryVersion = m.Version;
                row.Platform = m.Platform;
                row.RefreshedAt = now;
            } else {
                db.RouteBinaryCatalogs.Add(new RouteBinaryCatalog {
                    Path = path,
                    Method = e.Method,
                    RequestType = e.RequestType,
                    ResponseType = e.ResponseType,
                    RequestWrapped = e.RequestWrapped,
                    ResponseWrapped = e.ResponseWrapped,
                    BinaryVersion = m.Version,
                    Platform = m.Platform,
                    RefreshedAt = now
                });
            }

            binaryRows.Add(new BinaryRouteInfo(path, e.Method, e.RequestType, e.ResponseType, e.RequestWrapped,
                e.ResponseWrapped, m.Version, m.Platform, now));
        }

        var stale = existing.Values.Where(r => !seen.Contains(r.Path)).ToList();
        if (stale.Count > 0) db.RouteBinaryCatalogs.RemoveRange(stale);

        await db.SaveChangesAsync(ct);
        (services.GetService(typeof(IBinaryRouteProvider)) as IBinaryRouteProvider)?.Invalidate();

        int newCount = seen.Count(p => !existing.ContainsKey(p));
        var nonBinary = services.GetService(typeof(INonBinaryRouteCatalog)) as INonBinaryRouteCatalog
                        ?? new NonBinaryRouteCatalog(yaml, null, null);
        var drift = RouteDrift.Compute(nonBinary.All(), binaryRows);
        string note = BuildNote(inputs, merged.Count, notUsed);
        return new EndpointRebuildResult(seen.Count, newCount, drift.Count, contributors[0].Candidate.Version, note);
    }

    internal static IReadOnlyList<MergedRoute> Merge(IReadOnlyList<MergeContributor> contributors) {
        var order = new List<string>();
        var owned = new Dictionary<string, MergedRoute>(StringComparer.Ordinal);

        foreach (var c in contributors) {
            foreach (var e in c.Endpoints) {
                if (e.Path is null) continue;
                if (!owned.TryGetValue(e.Path, out var current)) {
                    order.Add(e.Path);
                    owned[e.Path] = new MergedRoute(e, c.Platform, c.Version);
                    continue;
                }

                var d = current.Descriptor;
                if (d.RequestType is null && e.RequestType is not null) {
                    d = d with { RequestType = e.RequestType, RequestWrapped = e.RequestWrapped };
                }

                if (d.ResponseType is null && e.ResponseType is not null) {
                    d = d with { ResponseType = e.ResponseType, ResponseWrapped = e.ResponseWrapped };
                }

                owned[e.Path] = current with { Descriptor = d };
            }
        }

        return order.Select(p => owned[p]).ToList();
    }

    internal static string BuildNote(IReadOnlyList<MergeContributor> contributors, int mergedCount,
        IReadOnlyList<string> notUsed) {
        string head = string.Join(" + ",
            contributors.Select(c => $"{c.Platform} {c.Version} ({c.Endpoints.Count})"));
        string body = $"{head}, merged {mergedCount}";
        return notUsed.Count == 0 ? body : $"{body}; not used: {string.Join("; ", notUsed)}";
    }

    public sealed record MergeContributor(
        string Platform,
        string Version,
        IReadOnlyList<EndpointCatalogExtractor.EndpointDescriptor> Endpoints);

    public sealed record MergedRoute(
        EndpointCatalogExtractor.EndpointDescriptor Descriptor,
        string Platform,
        string Version);

    private sealed record Attempt(
        GameBinaryProvider.ExtractionCandidate Candidate,
        IReadOnlyList<EndpointCatalogExtractor.EndpointDescriptor> Endpoints,
        string Note);

    internal static IReadOnlyList<EndpointCatalogExtractor.EndpointDescriptor> Filter(
        IReadOnlyList<EndpointCatalogExtractor.EndpointDescriptor> endpoints, IReadOnlyList<string> excludedPaths) {
        var excluded = new HashSet<string>(excludedPaths, StringComparer.Ordinal);
        return endpoints.Where(e => e.Path is not null && !excluded.Contains(e.Path)).ToList();
    }

    private static bool IsElf(byte[] b) =>
        b.Length >= 4 && b[0] == 0x7f && b[1] == 0x45 && b[2] == 0x4c && b[3] == 0x46;
}
