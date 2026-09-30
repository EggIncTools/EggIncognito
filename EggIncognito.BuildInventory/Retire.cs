namespace EggIncognito.BuildInventory;

internal static class Retire {
    private static readonly string[] Retirable = ["identical", "contained"];

    public static int Run(string root, string dedupeTsv, string destination) {
        string dest = Path.GetFullPath(destination);
        var header = File.ReadLines(dedupeTsv).First().Split('\t');
        int Col(string name) => Array.IndexOf(header, name);
        int codeCol = Col("versionCode"), nameCol = Col("versionName"), verdictCol = Col("verdict");
        int fileCol = Col("file"), versusCol = Col("versus"), onlyThisCol = Col("onlyThis");

        var rows = File.ReadLines(dedupeTsv).Skip(1).Select(l => l.Split('\t'))
            .Where(r => Retirable.Contains(r[verdictCol]) && r[onlyThisCol] == "0")
            .ToList();
        var leaves = rows.Select(r => Path.GetFileName(r[fileCol])).ToList();
        var clash = leaves.GroupBy(l => l, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (clash is not null) {
            Console.Error.WriteLine($"BuildInventory: two retirees share the name {clash.Key}, nothing moved");
            return 1;
        }

        Directory.CreateDirectory(dest);
        string logPath = Path.Combine(dest, "moved.tsv");
        bool fresh = !File.Exists(logPath);
        using var log = new StreamWriter(logPath, append: true);
        if (fresh) log.WriteLine("versionCode\tversionName\tverdict\tfrom\tto\tkeeper");

        int moved = 0, skipped = 0;
        foreach (var r in rows) {
            string src = Path.Combine(root, r[fileCol]);
            string keeper = Path.Combine(root, r[versusCol]);
            string dst = Path.Combine(dest, Path.GetFileName(src));
            if (!File.Exists(src) || !File.Exists(keeper) || File.Exists(dst)) {
                Console.Error.WriteLine($"BuildInventory: skipped {r[fileCol]} (source {File.Exists(src)}, keeper {File.Exists(keeper)}, target exists {File.Exists(dst)})");
                skipped++;
                continue;
            }

            File.Move(src, dst);
            log.WriteLine($"{r[codeCol]}\t{r[nameCol]}\t{r[verdictCol]}\t{src}\t{dst}\t{keeper}");
            Console.WriteLine($"{r[codeCol]}\t{r[verdictCol]}\t{r[fileCol]} -> {dst}");
            moved++;
        }

        Console.WriteLine($"BuildInventory: {moved} moved, {skipped} skipped -> {dest}, log {logPath}");
        return skipped == 0 ? 0 : 2;
    }
}
