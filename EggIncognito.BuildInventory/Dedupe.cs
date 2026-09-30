using System.Collections.Concurrent;
using System.Globalization;

namespace EggIncognito.BuildInventory;

internal static class Dedupe {
    private const int PathsInSummary = 8;

    private sealed record Scanned(string File, int? Code, string? Name, ArchiveContent? Content, string? Error);

    public static int Run(string root, string output) {
        var scanned = new ConcurrentBag<Scanned>();
        Parallel.ForEach(Program.Archives(root), new ParallelOptions { MaxDegreeOfParallelism = 4 }, file => {
            string rel = Path.GetRelativePath(root, file);
            var (name, build, error) = Program.ReadMeta(file);
            int? code = int.TryParse(build, NumberStyles.None, CultureInfo.InvariantCulture, out int c) ? c : null;
            try {
                var content = ArchiveContent.Read(root, file);
                scanned.Add(new Scanned(rel, code, name, content, error));
                Console.WriteLine($"{code}\t{content.Count} entries\t{content.Modules} modules\t{rel}");
            } catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or OverflowException) {
                scanned.Add(new Scanned(rel, code, name, null, ex.Message));
                Console.Error.WriteLine($"BuildInventory: {rel}: {ex.Message}");
            }
        });

        var rows = new List<string> { "versionCode\tversionName\tverdict\tfile\tmodules\tentries\tversus\tonlyThis\tonlyVersus\tdifferingPaths" };
        var proof = new List<string> { "versionCode\tfile\tversus\tside\tpath\tsha256" };
        int kept = 0, contained = 0, conflicts = 0;

        foreach (var s in scanned.Where(s => s.Content is null || s.Code is null).OrderBy(s => s.File, StringComparer.OrdinalIgnoreCase))
            rows.Add($"{s.Code}\t{s.Name}\tunreadable\t{s.File}\t\t\t\t\t\t{s.Error ?? "no versionCode"}");

        foreach (var group in scanned.Where(s => s is { Content: not null, Code: not null }).GroupBy(s => s.Code!.Value).OrderBy(g => g.Key)) {
            var names = group.ToDictionary(s => s.Content!, s => s.Name);
            var ordered = group.Select(s => s.Content!)
                .OrderByDescending(a => a.Count)
                .ThenBy(a => a.File, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var maximal = ordered
                .Where((a, i) => !ordered.Where((b, j) => j != i && a.IsSubsetOf(b) && (j < i || !b.IsSubsetOf(a))).Any())
                .ToList();
            bool conflict = maximal.Count > 1;

            foreach (var a in ordered) {
                bool isMaximal = maximal.Contains(a);
                if (isMaximal && !conflict) {
                    rows.Add($"{group.Key}\t{names[a]}\tkeep\t{a.File}\t{a.Modules}\t{a.Count}\t\t\t\t");
                    kept++;
                    continue;
                }

                var versus = isMaximal ? maximal.First(m => m != a) : maximal.First(a.IsSubsetOf);
                var onlyThis = a.MissingFrom(versus).Order(StringComparer.Ordinal).ToList();
                var onlyVersus = versus.MissingFrom(a).Order(StringComparer.Ordinal).ToList();
                string verdict = isMaximal ? "conflict" : onlyVersus.Count == 0 ? "identical" : "contained";
                if (isMaximal) conflicts++;
                else contained++;

                string paths = string.Join(" ", onlyThis.Concat(onlyVersus).Select(k => k[..k.IndexOf('\t')]).Distinct().Take(PathsInSummary));
                rows.Add($"{group.Key}\t{names[a]}\t{verdict}\t{a.File}\t{a.Modules}\t{a.Count}\t{versus.File}\t{onlyThis.Count}\t{onlyVersus.Count}\t{paths}");
                proof.AddRange(onlyThis.Select(k => $"{group.Key}\t{a.File}\t{versus.File}\tonly-this\t{k}"));
                proof.AddRange(onlyVersus.Select(k => $"{group.Key}\t{a.File}\t{versus.File}\tonly-versus\t{k}"));
            }
        }

        string outPath = Path.GetFullPath(output);
        string proofPath = Path.ChangeExtension(outPath, ".proof.tsv");
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        File.WriteAllLines(outPath, rows);
        File.WriteAllLines(proofPath, proof);
        Console.WriteLine($"BuildInventory: {kept} keep, {contained} proven contained, {conflicts} in conflict -> {outPath}, {proofPath}");
        return 0;
    }
}
