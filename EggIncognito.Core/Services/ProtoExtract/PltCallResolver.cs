using static EggIncognito.Core.Services.ProtoExtract.Arm64Operands;

namespace EggIncognito.Core.Services.ProtoExtract;

public sealed class PltCallResolver(byte[] bin, IBinaryImage img) {
    private readonly Dictionary<ulong, ulong> _slots = img is ElfImage ? ElfSections.ReadJumpSlots(bin) : [];
    private readonly Dictionary<ulong, ulong> _cache = [];
    private Dictionary<ulong, string>? _names;

    public string? ImportName(ulong target) {
        _names ??= img is ElfImage ? ElfSections.ReadJumpSlotNames(bin) : MachoImports.ReadPointerSlotNames(bin);
        return Stub(target) is { } slot && _names.TryGetValue(slot, out string? name) ? name : null;
    }

    public ulong Resolve(ulong target) {
        if (_slots.Count == 0) return target;
        if (_cache.TryGetValue(target, out ulong hit)) return hit;
        ulong resolved = Stub(target) is { } slot && _slots.TryGetValue(slot, out ulong fn) ? fn : target;
        _cache[target] = resolved;
        return resolved;
    }

    private ulong? Stub(ulong va) {
        var head = Arm64DataTableReader.ListRange(bin, va, va + 16, 4);
        if (!head.Ok || head.Instructions.Count < 3) return null;
        var i0 = SplitOps(head.Instructions[0].Operands);
        var i1 = SplitOps(head.Instructions[1].Operands);
        if (head.Instructions[0].Mnemonic != "adrp" || i0.Count != 2 || !TryImm(i0[1], out ulong page)) return null;
        if (head.Instructions[1].Mnemonic != "ldr" || i1.Count != 2
                                                  || !TryMem(i1[1], out string reg, out ulong off)
                                                  || reg != i0[0]) return null;
        bool elfStub = head.Instructions.Count >= 4 && head.Instructions[3].Mnemonic == "br";
        bool machoStub = head.Instructions.Count >= 3 && head.Instructions[2].Mnemonic == "br";
        return elfStub || machoStub ? page + off : null;
    }
}
