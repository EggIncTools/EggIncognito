namespace EggIncognito.Core.Services.ProtoExtract;

public static class ProtoDiff {
    private const double RenameThreshold = 0.6;

    public static ProtoDiffResult Compute(string oldProto, string newProto) {
        var oldRoots = ProtoModelParser.Parse(oldProto);
        var newRoots = ProtoModelParser.Parse(newProto);
        var entries = new List<MessageDiff>();
        DiffScope(oldRoots, newRoots, entries);
        return new ProtoDiffResult(entries);
    }

    public static string Diff(string oldProto, string newProto) => RenderText(Compute(oldProto, newProto));

    public static string RenderText(ProtoDiffResult result) {
        var sections = new List<string>();
        foreach (var e in result.Entries) {
            string header = e.Kind == MessageDiffKind.Renamed
                ? $"@@ message {e.OldPath} -> {e.NewPath} @@"
                : $"@@ message {e.DisplayPath} @@";

            var lines = new List<string>();
            if (e.Kind == MessageDiffKind.Added) {
                foreach (string line in e.Body) lines.Add("+" + line.TrimEnd('\n', '\r'));
            } else if (e.Kind == MessageDiffKind.Removed) {
                foreach (string line in e.Body) lines.Add("-" + line.TrimEnd('\n', '\r'));
            } else {
                foreach (var c in e.FieldChanges.OrderBy(c => c.Number)) {
                    if (c.Old is not null) lines.Add("-" + c.Old.Raw);
                    if (c.New is not null) lines.Add("+" + c.New.Raw);
                }

                foreach (var c in e.EnumChanges.OrderBy(c => c.Number)) {
                    if (c.Old is not null) lines.Add("-" + c.Old.Raw);
                    if (c.New is not null) lines.Add("+" + c.New.Raw);
                }
            }

            sections.Add(header);
            sections.AddRange(lines);
            sections.Add("");
        }

        return string.Join("\n", sections);
    }

    private static void DiffScope(List<ProtoMessage> oldList, List<ProtoMessage> newList, List<MessageDiff> entries) {
        var newByName = new Dictionary<string, ProtoMessage>();
        foreach (var m in newList) newByName.TryAdd(m.Name, m);

        var matchedPairs = new List<(ProtoMessage Old, ProtoMessage New, bool Renamed)>();
        var oldLeftover = new List<ProtoMessage>();
        var claimedNewNames = new HashSet<string>();

        foreach (var om in oldList) {
            if (newByName.TryGetValue(om.Name, out var nm) && claimedNewNames.Add(nm.Name)) {
                matchedPairs.Add((om, nm, false));
            } else {
                oldLeftover.Add(om);
            }
        }

        var newLeftover = newList.Where(nm => !claimedNewNames.Contains(nm.Name)).ToList();

        var candidates = new List<(ProtoMessage Old, ProtoMessage New, double Score)>();
        foreach (var om in oldLeftover) {
            foreach (var nm in newLeftover) {
                double score = Similarity(om, nm);
                if (score >= RenameThreshold) candidates.Add((om, nm, score));
            }
        }

        candidates.Sort((a, b) => b.Score.CompareTo(a.Score));

        var claimedOld = new HashSet<ProtoMessage>();
        var claimedNew = new HashSet<ProtoMessage>();
        foreach (var c in candidates) {
            if (claimedOld.Contains(c.Old) || claimedNew.Contains(c.New)) continue;
            claimedOld.Add(c.Old);
            claimedNew.Add(c.New);
            matchedPairs.Add((c.Old, c.New, true));
        }

        var remainingOld = oldLeftover.Where(m => !claimedOld.Contains(m)).ToList();
        var remainingNew = newLeftover.Where(m => !claimedNew.Contains(m)).ToList();

        foreach (var (om, nm, renamed) in matchedPairs) {
            var fieldChanges = DiffFields(om, nm);
            var enumChanges = DiffEnums(om, nm);
            if (renamed || fieldChanges.Count > 0 || enumChanges.Count > 0) {
                entries.Add(new MessageDiff(
                    renamed ? MessageDiffKind.Renamed : MessageDiffKind.Modified,
                    om.Path,
                    nm.Path,
                    fieldChanges,
                    enumChanges,
                    []));
            }

            DiffScope(om.Children, nm.Children, entries);
        }

        foreach (var om in remainingOld) entries.Add(new MessageDiff(MessageDiffKind.Removed, om.Path, null, [], [], om.BodyLines));

        foreach (var nm in remainingNew) entries.Add(new MessageDiff(MessageDiffKind.Added, null, nm.Path, [], [], nm.BodyLines));
    }

    private static double Similarity(ProtoMessage a, ProtoMessage b) {
        if (a.Fields.Count == 0 || b.Fields.Count == 0) return 0;
        var keys = a.Fields.Select(f => (f.Number, LeafType(f.Type))).ToHashSet();
        int shared = b.Fields.Count(f => keys.Contains((f.Number, LeafType(f.Type))));
        return 2.0 * shared / (a.Fields.Count + b.Fields.Count);
    }

    private static bool TypesMatch(string a, string b) {
        string na = ProtoModelParser.NormalizeType(a);
        string nb = ProtoModelParser.NormalizeType(b);
        if (na == nb) return true;
        return na.EndsWith("." + nb, StringComparison.Ordinal) || nb.EndsWith("." + na, StringComparison.Ordinal);
    }

    private static string LeafType(string type) {
        string n = ProtoModelParser.NormalizeType(type);
        int i = n.LastIndexOf('.');
        return i < 0 ? n : n[(i + 1)..];
    }

    private static List<FieldChange> DiffFields(ProtoMessage oldM, ProtoMessage newM) {
        var oldByNum = new Dictionary<int, ProtoField>();
        foreach (var f in oldM.Fields) oldByNum.TryAdd(f.Number, f);
        var newByNum = new Dictionary<int, ProtoField>();
        foreach (var f in newM.Fields) newByNum.TryAdd(f.Number, f);

        var numbers = oldByNum.Keys.Union(newByNum.Keys).OrderBy(n => n);
        var changes = new List<FieldChange>();
        foreach (int n in numbers) {
            bool hasOld = oldByNum.TryGetValue(n, out var of);
            bool hasNew = newByNum.TryGetValue(n, out var nf);
            if (hasOld && !hasNew) {
                changes.Add(new FieldChange(FieldChangeKind.Removed, n, of, null));
            } else if (!hasOld && hasNew) {
                changes.Add(new FieldChange(FieldChangeKind.Added, n, null, nf));
            } else if (hasOld && hasNew) {
                bool changed = of!.Name != nf!.Name
                    || !TypesMatch(of.Type, nf.Type)
                    || of.Label != nf.Label;
                if (changed) changes.Add(new FieldChange(FieldChangeKind.Changed, n, of, nf));
            }
        }

        return changes;
    }

    private static List<EnumValueChange> DiffEnums(ProtoMessage oldM, ProtoMessage newM) {
        var oldByName = new Dictionary<string, ProtoEnumDef>();
        foreach (var e in oldM.Enums) oldByName.TryAdd(e.Name, e);
        var newByName = new Dictionary<string, ProtoEnumDef>();
        foreach (var e in newM.Enums) newByName.TryAdd(e.Name, e);

        var names = oldByName.Keys.Union(newByName.Keys);
        var changes = new List<EnumValueChange>();
        foreach (string name in names) {
            bool hasOld = oldByName.TryGetValue(name, out var oe);
            bool hasNew = newByName.TryGetValue(name, out var ne);
            if (hasOld && hasNew) {
                var oldByNum = new Dictionary<int, ProtoEnumValue>();
                foreach (var v in oe!.Values) oldByNum.TryAdd(v.Number, v);
                var newByNum = new Dictionary<int, ProtoEnumValue>();
                foreach (var v in ne!.Values) newByNum.TryAdd(v.Number, v);

                var numbers = oldByNum.Keys.Union(newByNum.Keys).OrderBy(n => n);
                foreach (int n in numbers) {
                    bool ho = oldByNum.TryGetValue(n, out var ov);
                    bool hn = newByNum.TryGetValue(n, out var nv);
                    if (ho && !hn) {
                        changes.Add(new EnumValueChange(FieldChangeKind.Removed, name, n, ov, null));
                    } else if (!ho && hn) {
                        changes.Add(new EnumValueChange(FieldChangeKind.Added, name, n, null, nv));
                    } else if (ho && hn && ov!.Name != nv!.Name) {
                        changes.Add(new EnumValueChange(FieldChangeKind.Changed, name, n, ov, nv));
                    }
                }
            } else if (hasOld) {
                foreach (var v in oe!.Values) changes.Add(new EnumValueChange(FieldChangeKind.Removed, name, v.Number, v, null));
            } else if (hasNew) {
                foreach (var v in ne!.Values) changes.Add(new EnumValueChange(FieldChangeKind.Added, name, v.Number, null, v));
            }
        }

        return changes;
    }
}
