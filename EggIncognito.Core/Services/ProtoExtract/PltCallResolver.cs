using static EggIncognito.Core.Services.ProtoExtract.Arm64Operands;

namespace EggIncognito.Core.Services.ProtoExtract;

public sealed class PltCallResolver(byte[] bin, IBinaryImage img) {
    private readonly Dictionary<ulong, ulong> _slots = img is ElfImage ? ElfSections.ReadJumpSlots(bin) : [];
    private readonly Dictionary<ulong, ulong> _cache = [];

    public ulong Resolve(ulong target) {
        if (_slots.Count == 0) return target;
        if (_cache.TryGetValue(target, out ulong hit)) return hit;
        ulong resolved = Stub(target) is { } slot && _slots.TryGetValue(slot, out ulong fn) ? fn : target;
        _cache[target] = resolved;
        return resolved;
    }

    private ulong? Stub(ulong va) {
        var head = Arm64DataTableReader.ListRange(bin, va, va + 16, 4);
        if (!head.Ok || head.Instructions.Count < 4 || head.Instructions[3].Mnemonic != "br") return null;
        var i0 = SplitOps(head.Instructions[0].Operands);
        var i1 = SplitOps(head.Instructions[1].Operands);
        if (head.Instructions[0].Mnemonic != "adrp" || i0.Count != 2 || !TryImm(i0[1], out ulong page)) return null;
        if (head.Instructions[1].Mnemonic != "ldr" || i1.Count != 2
                                                  || !TryMem(i1[1], out string reg, out ulong off)
                                                  || reg != i0[0]) return null;
        return page + off;
    }
}
