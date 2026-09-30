using System.Globalization;
using System.IO.Compression;
using EggIncognito.Core.Services.ProtoExtract;

namespace EggIncognito.BuildInventory;

public static class Program {
    private static readonly string[] Extensions = [".apk", ".apkm", ".xapk"];

    public static int Main(string[] args) {
        string mode = args.Length > 0 && args[0] is "dedupe" or "retire" or "missing" ? args[0] : "inventory";
        if (mode != "inventory") args = args[1..];
        int need = mode == "retire" ? 3 : 2;
        if (args.Length < need) {
            Console.Error.WriteLine("Usage: EggIncognito.BuildInventory <archive root> <output tsv>");
            Console.Error.WriteLine("       EggIncognito.BuildInventory dedupe <archive root> <output tsv>");
            Console.Error.WriteLine("       EggIncognito.BuildInventory retire <archive root> <dedupe tsv> <destination dir>");
            Console.Error.WriteLine("       EggIncognito.BuildInventory missing <archive root> <output txt> [from versionCode] [to versionCode]");
            return 1;
        }

        string root = Path.GetFullPath(args[0]);
        if (!Directory.Exists(root)) {
            Console.Error.WriteLine($"BuildInventory: directory not found: {root}");
            return 1;
        }

        if (mode == "dedupe") return Dedupe.Run(root, args[1]);
        if (mode == "retire") return Retire.Run(root, args[1], args[2]);

        var (byCode, unreadable) = Scan(root);
        if (mode == "missing") return Missing(byCode, args[1], args.Length > 2 ? args[2] : null, args.Length > 3 ? args[3] : null);

        var lines = new List<string> { "versionCode\tstatus\tversionName\tfiles" };
        int absent = 0;
        int? prev = null;
        foreach (var (code, files) in byCode) {
            if (prev is int p && code > p + 1) {
                lines.Add($"{Range(p + 1, code - 1)}\tabsent\t\t");
                absent += code - p - 1;
            }

            lines.Add($"{code}\tpresent\t{string.Join(" / ", files.Select(f => f.Name).Distinct())}\t{string.Join(" | ", files.Select(f => f.File))}");
            prev = code;
        }

        lines.AddRange(unreadable.Select(u => $"\tunreadable\t{u.Reason}\t{u.File}"));

        string output = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllLines(output, lines);
        Console.WriteLine($"BuildInventory: {byCode.Count} versionCodes present, {absent} absent between them, {unreadable.Count} unreadable -> {output}");
        return 0;
    }

    private static (SortedDictionary<int, List<(string File, string? Name)>> ByCode, List<(string File, string Reason)> Unreadable) Scan(string root) {
        var byCode = new SortedDictionary<int, List<(string File, string? Name)>>();
        var unreadable = new List<(string File, string Reason)>();
        foreach (string file in Archives(root)) {
            string rel = Path.GetRelativePath(root, file);
            var (name, build, error) = ReadMeta(file);
            if (!int.TryParse(build, NumberStyles.None, CultureInfo.InvariantCulture, out int code)) {
                string reason = error ?? "no versionCode";
                unreadable.Add((rel, reason));
                Console.Error.WriteLine($"BuildInventory: {rel}: {reason}");
                continue;
            }

            if (!byCode.TryGetValue(code, out var files)) byCode[code] = files = [];
            files.Add((rel, name));
            Console.WriteLine($"{code}\t{name}\t{rel}");
        }

        return (byCode, unreadable);
    }

    private static int Missing(SortedDictionary<int, List<(string File, string? Name)>> byCode, string output, string? fromArg, string? toArg) {
        if (byCode.Count == 0) {
            Console.Error.WriteLine("BuildInventory: no readable archives, nothing to diff against");
            return 1;
        }

        int from = fromArg is null ? byCode.Keys.First() : int.Parse(fromArg, NumberStyles.None, CultureInfo.InvariantCulture);
        int to = toArg is null ? byCode.Keys.Last() : int.Parse(toArg, NumberStyles.None, CultureInfo.InvariantCulture);
        var codes = Enumerable.Range(from, to - from + 1).Where(c => !byCode.ContainsKey(c)).ToList();

        string NameOf(int code) => string.Join(" / ", byCode[code].Select(f => f.Name).Distinct());
        var detail = new List<string> { "versionCode\tafter\tbefore" };
        foreach (int code in codes) {
            int? prev = byCode.Keys.Where(k => k < code).Cast<int?>().LastOrDefault();
            int? next = byCode.Keys.Where(k => k > code).Cast<int?>().FirstOrDefault();
            detail.Add($"{code}\t{(prev is int p ? $"{p} {NameOf(p)}" : "")}\t{(next is int n ? $"{n} {NameOf(n)}" : "")}");
        }

        string txt = Path.GetFullPath(output);
        string tsv = Path.ChangeExtension(txt, ".tsv");
        Directory.CreateDirectory(Path.GetDirectoryName(txt)!);
        File.WriteAllLines(txt, codes.Select(c => c.ToString(CultureInfo.InvariantCulture)));
        File.WriteAllLines(tsv, detail);
        Console.WriteLine($"BuildInventory: {codes.Count} versionCodes missing in {from}-{to} ({byCode.Count} present) -> {txt}, {tsv}");
        return 0;
    }

    private static string Range(int from, int to) =>
        from == to ? from.ToString(CultureInfo.InvariantCulture) : $"{from}-{to}";

    internal static IEnumerable<string> Archives(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(f => Extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase);

    internal static (string? Name, string? Build, string? Error) ReadMeta(string path) {
        try {
            using var zip = ZipFile.OpenRead(path);
            var (name, build) = ManifestMeta(zip);
            if (build is not null and not "1") return (name, build, null);

            var inner = ArchiveProtoExtractor.FindBaseModule(zip);
            if (inner is null) return (name, build, build is null ? "no manifest and no base module" : null);

            using var buf = new MemoryStream();
            using (var es = inner.Open()) es.CopyTo(buf);
            buf.Position = 0;
            using var innerZip = new ZipArchive(buf, ZipArchiveMode.Read);
            var (innerName, innerBuild) = ManifestMeta(innerZip);
            return innerBuild is null ? (name, build, $"{inner.FullName} has no versionCode") : (innerName, innerBuild, null);
        } catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) {
            return (null, null, ex.Message);
        }
    }

    private static (string? Name, string? Build) ManifestMeta(ZipArchive zip) {
        var entry = zip.GetEntry("AndroidManifest.xml");
        if (entry is null) return (null, null);
        using var es = entry.Open();
        using var buf = new MemoryStream();
        es.CopyTo(buf);
        return AppMetaReader.Read(buf.ToArray());
    }
}
