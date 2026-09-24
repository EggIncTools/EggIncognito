namespace EggIncognito.Core.Services.ProtoExtract;

public static class MachoClientVersionReader {
    public static ClientVersionResult Read(byte[] macho, int? previousClientVersion) {
        if (!MachoText.TryFindText(macho, out int off, out int size, out ulong vm))
            return new ClientVersionResult(null, []);

        var insns = Arm64Decoder.Decode(macho.AsSpan(off, size), vm);
        var cands = Candidates(insns);
        int? chosen = Pick(cands, previousClientVersion);
        var sorted = cands.Keys.Order().ToList();
        return new ClientVersionResult(chosen, sorted);
    }

    internal static Dictionary<int, int> Candidates(IReadOnlyList<Arm64Insn> insns) {
        var pair = new Dictionary<(int baseReg, long off, int val), HashSet<ulong>>();
        for (int i = 0; i < insns.Count; i++) {
            if (insns[i].Op != Arm64Op.Str) continue;
            var str = insns[i];
            int? val = ResolveReg(insns, i, str.Rd);
            if (val is null or < 2 or > 255) continue;
            var key = (str.Rn, str.Imm, val.Value);
            if (!pair.TryGetValue(key, out var set)) pair[key] = set = [];
            set.Add(str.Address);
        }

        var outp = new Dictionary<int, int>();
        foreach (var (key, sites) in pair) {
            if (sites.Count < 3) continue;
            int v = key.val;
            outp[v] = Math.Max(outp.GetValueOrDefault(v, 0), sites.Count);
        }

        return outp;
    }

    private static int? ResolveReg(IReadOnlyList<Arm64Insn> insns, int strIndex, int reg) {
        int value = 0;
        bool seeded = false;
        int start = Math.Max(0, strIndex - 4);
        for (int j = start; j < strIndex; j++) {
            var ins = insns[j];
            if (ins.Rd != reg) continue;
            int shift = ins.Rn * 16;
            if (ins.Op == Arm64Op.Movz) {
                value = (int)(ins.Imm << shift);
                seeded = true;
            } else if (ins.Op == Arm64Op.Movk && seeded) {
                int mask = ~(0xFFFF << shift);
                value = (value & mask) | (int)(ins.Imm << shift);
            }
        }

        return seeded ? value : null;
    }

    public static int? Pick(IReadOnlyDictionary<int, int> cands, int? prev) {
        if (prev is null) return null;
        int p = prev.Value;
        var inRange = cands.Keys.Where(v => v >= p && v <= p + 2).ToList();
        return inRange.Count == 0
            ? null
            : inRange
                .OrderBy(v => Math.Abs(v - p))
                .ThenByDescending(v => cands[v])
                .First();
    }

    public sealed record ClientVersionResult(int? ClientVersion, IReadOnlyList<int> Candidates);
}
