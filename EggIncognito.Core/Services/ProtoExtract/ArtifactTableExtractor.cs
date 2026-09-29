using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using static EggIncognito.Core.Services.ProtoExtract.Arm64Operands;

namespace EggIncognito.Core.Services.ProtoExtract;

public static partial class ArtifactTableExtractor {
    public const string WrapperSymbol = "__GLOBAL__sub_I_artifactdata";
    public const string FamilyCommitSymbol = "_ZN12ArtifactDataC2ERKS_";
    public const string TierCommitSymbol = "_ZN12ArtifactData13ArtifactLevelC2ERKS0_";
    public const string SignatureString = "lunar_totem";
    public const int ExpectedFamilies = 44;
    private const int MaxInstructions = 200_000;
    private const int WrapperScan = 64;
    private const ulong BuilderProbeBytes = 0x400;
    private const ulong MaxBuilderBytes = 0x80000;
    private static readonly string[] RarityCtorNeedles = ["mapIN2ei19ArtifactSpec_Rarity", "initializer_list"];

    private static readonly Layout AppleLayout = new(8, 0x50, 0x58, 0x60, 0x78, false);
    private static readonly Layout NdkLayout = new(0x10, 0x60, 0x68, 0x70, 0x88, true);

    private static readonly string[] LunarTiers =
        ["BASIC LUNAR TOTEM", "LUNAR TOTEM", "POWERFUL LUNAR TOTEM", "EGGCEPTIONAL LUNAR TOTEM"];

    [GeneratedRegex("^[a-z][a-z0-9_]{3,}$")]
    private static partial Regex IdPattern();

    public static Result Extract(byte[] bin) => ExtractWith(bin, BinaryImage.Load(bin)?.Symbols ?? []);

    public static Result ExtractWith(byte[] bin, IReadOnlyList<MachoSymbols.Symbol> syms) {
        var img = BinaryImage.Load(bin);
        if (img is null) return Fail("no binary image");
        var symbols = syms.Count > 0 ? syms : img.Symbols;
        bool elf = img is ElfImage;

        string? located = elf
            ? LocateElfBuilder(bin, img, out ulong start, out ulong end)
            : LocateMachoBuilder(bin, symbols, out start, out end);
        if (located is not null) return Fail(located);
        if (!MachoSymbols.TryFindFunc(symbols, [FamilyCommitSymbol], out var family))
            return Fail($"symbol not found: {FamilyCommitSymbol}");
        if (!MachoSymbols.TryFindFunc(symbols, [TierCommitSymbol], out var tier))
            return Fail($"symbol not found: {TierCommitSymbol}");

        var list = Arm64DataTableReader.ListRange(bin, start, end, MaxInstructions);
        if (!list.Ok) return Fail(list.Diagnostics);
        var insns = SpliceVeneers(bin, [.. list.Instructions.TakeWhile(i => i.Mnemonic != "ret")], start, end);

        var calls = new CallResolver(bin, img);
        var rarity = symbols
            .Where(s => s.Value != 0 && RarityCtorNeedles.All(n => s.Name.Contains(n, StringComparison.Ordinal)))
            .Select(s => s.Value)
            .ToHashSet();
        if (rarity.Count == 0 && InferRarityCtor(insns, calls, tier.Start) is { } inferred) rarity.Add(inferred);
        if (rarity.Count == 0) return Fail("rarity map constructor not found");

        var walker = new Walker(bin, img, calls, family.Start, tier.Start, rarity, elf ? NdkLayout : AppleLayout);
        foreach (var insn in insns) walker.Step(insn);
        return Calibrate(walker.Families, walker.Issues, walker.UnresolvedIngredients);
    }

    private static string? LocateMachoBuilder(byte[] bin, IReadOnlyList<MachoSymbols.Symbol> syms, out ulong start,
        out ulong end) {
        start = 0;
        end = 0;
        var wrapper = Arm64DataTableReader.ListWith(bin, syms, [WrapperSymbol], 256);
        if (!wrapper.Ok) return wrapper.Diagnostics;
        var tails = wrapper.Instructions.Where(i => i.Mnemonic == "b").ToList();
        if (tails.Count == 0 || !TryImm(tails[^1].Operands, out ulong builderVa))
            return "artifactdata wrapper has no tail branch";
        if (!MachoSymbols.TryResolveVa(syms, builderVa, out var builder, out ulong offset) || offset != 0)
            return $"tail target 0x{builderVa:x} is not a symbol start";
        start = builder.Start;
        end = builder.End;
        return null;
    }

    private static string? LocateElfBuilder(byte[] bin, IBinaryImage img, out ulong start, out ulong end) {
        start = 0;
        end = 0;
        var inits = img.GetInitArrayTargets().Distinct().Order().ToList();
        foreach (ulong init in inits) {
            var head = Arm64DataTableReader.ListRange(bin, init, init + 4 * WrapperScan, WrapperScan);
            if (!head.Ok) continue;
            var exit = head.Instructions.FirstOrDefault(i => i.Mnemonic is "b" or "ret");
            if (exit.Mnemonic != "b" || !TryImm(exit.Operands, out ulong target)) continue;
            if (!References(bin, img, target, target + BuilderProbeBytes, SignatureString)) continue;
            start = target;
            end = inits.Where(v => v > target).DefaultIfEmpty(target + MaxBuilderBytes).Min();
            end = Math.Min(end, target + MaxBuilderBytes);
            return null;
        }

        return $"artifactdata builder not located: no init tail-calls a function referencing '{SignatureString}'";
    }

    private static List<Arm64DataTableReader.Insn> SpliceVeneers(byte[] bin,
        List<Arm64DataTableReader.Insn> insns, ulong start, ulong end) {
        var outp = new List<Arm64DataTableReader.Insn>(insns.Count);
        foreach (var i in insns) {
            if (i.Mnemonic != "b" || !TryImm(i.Operands, out ulong target) || (target >= start && target < end)) {
                outp.Add(i);
                continue;
            }

            var patch = Arm64DataTableReader.ListRange(bin, target, target + 4 * WrapperScan, WrapperScan);
            int back = patch.Ok
                ? patch.Instructions.ToList().FindIndex(p => p.Mnemonic == "b" && TryImm(p.Operands, out ulong r)
                                                             && r == i.Va + 4)
                : -1;
            if (back < 0) {
                outp.Add(i);
                continue;
            }

            outp.AddRange(patch.Instructions.Take(back));
        }

        return outp;
    }

    private static bool References(byte[] bin, IBinaryImage img, ulong start, ulong end, string needle) {
        var scan = Arm64DataTableReader.ScanRange(bin, start, end);
        return scan.Ok && scan.Addresses.Any(r => r.Section is "__cstring" or ".rodata"
                                                  && BinaryStrings.ReadCstr(bin, img, r.Va, 64) == needle);
    }

    private static ulong? InferRarityCtor(IReadOnlyList<Arm64DataTableReader.Insn> insns, CallResolver calls,
        ulong tierVa) {
        var counts = new Dictionary<ulong, int>();
        ulong last = 0;
        foreach (var i in insns) {
            if (i.Mnemonic != "bl" || !TryImm(i.Operands, out ulong raw)) continue;
            ulong target = calls.Resolve(raw);
            if (target == tierVa && last != 0) counts[last] = counts.GetValueOrDefault(last) + 1;
            last = target;
        }

        return counts.Count == 0 ? null : counts.MaxBy(kv => kv.Value).Key;
    }

    private static Result Calibrate(List<Family> families, IReadOnlyList<string> issues, int unresolved) {
        var errors = new List<string>(issues);
        if (families.Count != ExpectedFamilies)
            errors.Add($"expected {ExpectedFamilies} families, read {families.Count}");
        if (families.Select(f => f.AfxId).Distinct().Count() != families.Count) errors.Add("duplicate afx ids");

        foreach (var f in families) {
            var levels = f.Tiers.Select(t => t.Level).ToList();
            if (f.Tiers.Count == 0 || !levels.SequenceEqual(Enumerable.Range(0, f.Tiers.Count)))
                errors.Add($"{f.BinaryId}: tier levels [{string.Join(",", levels)}]");
            if (f.Tiers.Any(t => t.Name.Length == 0 || t.RarityCount <= 0))
                errors.Add($"{f.BinaryId}: tier missing name or rarity count");
        }

        Anchor(families, errors, 0, f => f.BinaryId == "lunar_totem" && f.PluralName == "LUNAR TOTEMS"
                                         && f.Kind == 0 && f.Dimension == 3
                                         && f.Tiers.Select(t => t.Name).SequenceEqual(LunarTiers)
                                         && f.Tiers.Select(t => t.RarityCount).SequenceEqual([1, 2, 2, 4]));
        Anchor(families, errors, 34, f => f.Kind == 1 && f.Dimension == 12 && f.Tiers.Count > 0
                                          && f.Tiers[0].Recipe.Contains(new Ingredient(47, 0, 20)));
        Anchor(families, errors, 18, f => f.BinaryId == "tau_centi_geode" && f.Kind == 2);

        string tail = unresolved == 0 ? "" : $", {unresolved} unresolved ingredients";
        string diag = errors.Count == 0
            ? $"{families.Count} families, {families.Sum(f => f.Tiers.Count)} tiers{tail}"
            : string.Join("; ", errors) + tail;
        return new Result(errors.Count == 0, families, diag);
    }

    private static void Anchor(IReadOnlyList<Family> families, List<string> errors, int afxId, Func<Family, bool> ok) {
        var f = families.FirstOrDefault(x => x.AfxId == afxId);
        if (f.Tiers is null || !ok(f)) errors.Add($"anchor afx {afxId} mismatch");
    }

    private static Result Fail(string diagnostics) => new(false, [], diagnostics);

    public readonly record struct Ingredient(int AfxId, int Level, int Count);

    public readonly record struct Tier(int Level, string Name, int RarityCount, IReadOnlyList<Ingredient> Recipe);

    public readonly record struct Family(int AfxId, int Order, string BinaryId, string PluralName, int Kind,
        int Dimension, IReadOnlyList<Tier> Tiers);

    public readonly record struct Result(bool Ok, IReadOnlyList<Family> Families, string Diagnostics);

    private readonly record struct Ptr(int Region, long Offset) {
        public Ptr Plus(long delta) => new(Region, Offset + delta);
    }

    private readonly record struct PendingTier(int Level, string? Name, int RarityCount,
        IReadOnlyList<Ingredient> Recipe);

    private readonly record struct Layout(int KeyGap, int KindOffset, int RecipeBegin, int RecipeEnd,
        int RarityMapOffset, bool ShortStringFirst);

    private sealed class CallResolver(byte[] bin, IBinaryImage img) {
        private readonly Dictionary<ulong, ulong> _slots =
            img is ElfImage ? ElfSections.ReadJumpSlots(bin) : [];

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

    private sealed class Walker(
        byte[] bin,
        IBinaryImage img,
        CallResolver calls,
        ulong familyVa,
        ulong tierVa,
        HashSet<ulong> rarityVas,
        Layout layout) {
        private const int Binary = -1;
        private const int Stack = 0;
        private const int ElementSize = 20;
        private const int PluralOffset = 0x18;

        private static readonly string[] Keys =
            [.. Enumerable.Range(0, 31).Select(i => i.ToString(CultureInfo.InvariantCulture))];

        private static readonly HashSet<string> NonWriting =
            ["cmp", "cmn", "tst", "fcmp", "fcmpe", "ccmp", "ccmn", "cbz", "cbnz", "tbz", "tbnz", "ret", "nop", "prfm"];

        private readonly Dictionary<string, ulong> _imm = [];
        private readonly Dictionary<string, Ptr> _ptr = [];
        private readonly Dictionary<int, byte?[]> _vec = [];
        private readonly Dictionary<(int, long), byte> _mem = [];
        private readonly Dictionary<(int, long), Ptr> _slots = [];
        private readonly Dictionary<Ptr, int> _rarity = [];
        private readonly List<PendingTier> _tiers = [];
        private readonly List<string> _strings = [];
        private string? _prevStr;
        private ulong _prevVa;
        private int _heap;

        public List<Family> Families { get; } = [];
        public List<string> Issues { get; } = [];
        public int UnresolvedIngredients { get; private set; }

        public void Step(Arm64DataTableReader.Insn insn) {
            var ops = SplitOps(insn.Operands);
            switch (insn.Mnemonic) {
                case "adrp":
                case "adr":
                    if (ops.Count == 2 && TryImm(ops[1], out ulong page)) SetPtr(ops[0], new Ptr(Binary, (long)page));
                    else ClobberFirst(ops);
                    break;
                case "add":
                    Add(ops, false);
                    break;
                case "sub":
                    Add(ops, true);
                    break;
                case "mov":
                    Mov(ops);
                    break;
                case "movz":
                case "movn":
                case "movk":
                    MovWide(insn.Mnemonic, ops);
                    break;
                case "orr":
                    Orr(ops);
                    break;
                case "movi":
                    Movi(ops);
                    break;
                case "ldr":
                case "ldur":
                case "ldrb":
                case "ldurb":
                case "ldrh":
                case "ldurh":
                    Load(insn.Mnemonic, ops);
                    break;
                case "ldp":
                case "ldnp":
                    LoadPair(ops);
                    break;
                case "str":
                case "stur":
                case "strb":
                case "sturb":
                case "strh":
                case "sturh":
                    Store(insn.Mnemonic, ops);
                    break;
                case "stp":
                case "stnp":
                    StorePair(ops);
                    break;
                case "bl":
                case "blr":
                    Call(insn.Mnemonic, ops);
                    break;
                default:
                    if (insn.Mnemonic is "b" or "br" || insn.Mnemonic.StartsWith("b.", StringComparison.Ordinal)
                                                     || NonWriting.Contains(insn.Mnemonic)) break;
                    ClobberFirst(ops);
                    break;
            }
        }

        private void Add(List<string> ops, bool negate) {
            if (ops.Count < 3 || !TryGpr(ops[1], out string n, out _) || !TryOperand(ops, 2, out ulong m)) {
                ClobberFirst(ops);
                return;
            }

            long delta = negate ? -(long)m : (long)m;
            if (TryPtrOf(n, out var p)) {
                var q = p.Plus(delta);
                SetPtr(ops[0], q);
                if (q.Region == Binary) Materialize((ulong)q.Offset);
            } else if (TryImmOf(n, out ulong v)) {
                SetImm(ops[0], unchecked(v + (ulong)delta));
            } else {
                Clobber(ops[0]);
            }
        }

        private void Mov(List<string> ops) {
            if (ops.Count != 2) {
                ClobberFirst(ops);
                return;
            }

            if (TryVec(ops[0], out int vd, out _)) {
                if (!ops[1].Contains('[') && TryVec(ops[1], out int vs, out _) && _vec.TryGetValue(vs, out var b))
                    _vec[vd] = [.. b];
                else _vec.Remove(vd);
                return;
            }

            if (TryGpr(ops[1], out string s, out _)) {
                if (TryPtrOf(s, out var p)) SetPtr(ops[0], p);
                else if (TryImmOf(s, out ulong v)) SetImm(ops[0], v);
                else Clobber(ops[0]);
            } else if (TryImm(ops[1], out ulong c)) {
                SetImm(ops[0], c);
            } else {
                Clobber(ops[0]);
            }
        }

        private void MovWide(string mnemonic, List<string> ops) {
            if (ops.Count < 2 || !TryImm(ops[1], out ulong imm)) {
                ClobberFirst(ops);
                return;
            }

            int shift = ShiftOf(ops);
            if (mnemonic == "movz") {
                SetImm(ops[0], imm << shift);
            } else if (mnemonic == "movn") {
                SetImm(ops[0], ~(imm << shift));
            } else if (TryGpr(ops[0], out string key, out _) && TryImmOf(key, out ulong prev)) {
                SetImm(ops[0], (prev & ~(0xFFFFUL << shift)) | (imm << shift));
            } else {
                Clobber(ops[0]);
            }
        }

        private void Orr(List<string> ops) {
            if (ops.Count == 3 && IsZeroReg(ops[1]) && TryImm(ops[2], out ulong imm)) SetImm(ops[0], imm);
            else ClobberFirst(ops);
        }

        private void Movi(List<string> ops) {
            if (ops.Count < 2 || !TryVec(ops[0], out int num, out _) || !TryImm(ops[1], out ulong imm)) {
                ClobberFirst(ops);
                return;
            }

            int lane = LaneWidth(ops[0]);
            if (lane == 0 || ops.Skip(2).Any(o => o.StartsWith("msl", StringComparison.Ordinal))) {
                _vec.Remove(num);
                return;
            }

            ulong value = imm << ShiftOf(ops.GetRange(2, ops.Count - 2));
            var bytes = Zeros(16);
            for (int k = 0; k < 16; k++) bytes[k] = (byte)(value >> (8 * (k % lane)));
            _vec[num] = bytes;
        }

        private static int LaneWidth(string tok) {
            int dot = tok.IndexOf('.', StringComparison.Ordinal);
            if (dot < 0) return 0;
            return tok[^1] switch {
                'b' => 1,
                'h' => 2,
                's' => 4,
                'd' => 8,
                _ => 0
            };
        }

        private void Load(string mnemonic, List<string> ops) {
            int memIdx = ops.FindIndex(o => o.Contains('['));
            if (memIdx < 1) {
                ClobberFirst(ops);
                return;
            }

            bool ok = TryAddress(ops, memIdx, out var addr, out string baseKey, out var writeback);
            LoadInto(mnemonic, ops[0], ok ? addr : null);
            if (writeback is { } w) SetPtrKey(baseKey, w);
        }

        private void LoadPair(List<string> ops) {
            int memIdx = ops.FindIndex(o => o.Contains('['));
            if (memIdx < 2) {
                ClobberFirst(ops);
                return;
            }

            bool ok = TryAddress(ops, memIdx, out var addr, out string baseKey, out var writeback);
            int width = RegWidth(ops[0]);
            LoadInto("ldr", ops[0], ok ? addr : null);
            LoadInto("ldr", ops[1], ok ? addr.Plus(width) : null);
            if (writeback is { } w) SetPtrKey(baseKey, w);
        }

        private void LoadInto(string mnemonic, string dest, Ptr? addr) {
            if (TryVec(dest, out int num, out int vw)) {
                if (addr is not { } a) {
                    _vec.Remove(num);
                    return;
                }

                var bytes = Zeros(16);
                Array.Copy(Read(a, vw), bytes, vw);
                _vec[num] = bytes;
                return;
            }

            if (!TryGpr(dest, out _, out int gw) || addr is not { } p) {
                Clobber(dest);
                return;
            }

            int width = mnemonic switch {
                "ldrb" or "ldurb" => 1,
                "ldrh" or "ldurh" => 2,
                _ => gw
            };
            if (width == 8 && p.Region != Binary && _slots.TryGetValue((p.Region, p.Offset), out var slot)) {
                SetPtr(dest, slot);
                return;
            }

            if (TryPack(Read(p, width), out ulong v)) SetImm(dest, v);
            else Clobber(dest);
        }

        private void Store(string mnemonic, List<string> ops) {
            int memIdx = ops.FindIndex(o => o.Contains('['));
            if (memIdx < 1 || !TryAddress(ops, memIdx, out var addr, out string baseKey, out var writeback)) return;
            int forced = mnemonic switch {
                "strb" or "sturb" => 1,
                "strh" or "sturh" => 2,
                _ => 0
            };
            WriteReg(ops[0], addr, forced);
            if (writeback is { } w) SetPtrKey(baseKey, w);
        }

        private void StorePair(List<string> ops) {
            int memIdx = ops.FindIndex(o => o.Contains('['));
            if (memIdx < 2 || !TryAddress(ops, memIdx, out var addr, out string baseKey, out var writeback)) return;
            int width = WriteReg(ops[0], addr, 0);
            WriteReg(ops[1], addr.Plus(width), 0);
            if (writeback is { } w) SetPtrKey(baseKey, w);
        }

        private void Call(string mnemonic, List<string> ops) {
            ulong target = mnemonic == "bl" && ops.Count == 1 && TryImm(ops[0], out ulong t) ? calls.Resolve(t) : 0;
            bool known = target == familyVa || target == tierVa || rarityVas.Contains(target);
            if (target == familyVa) CommitFamily();
            else if (target == tierVa) CommitTier();
            else if (rarityVas.Contains(target)) RecordRarity();

            bool alloc = target != 0 && !known && _imm.TryGetValue(Keys[0], out ulong size) && size is > 0 and <= 0x10000;
            for (int r = 0; r <= 18; r++) {
                _imm.Remove(Keys[r]);
                _ptr.Remove(Keys[r]);
            }

            _imm.Remove(Keys[30]);
            _ptr.Remove(Keys[30]);
            foreach (int v in _vec.Keys.ToList()) {
                if (v is < 8 or > 15) _vec.Remove(v);
                else Array.Fill(_vec[v], null, 8, 8);
            }

            if (alloc) _ptr[Keys[0]] = new Ptr(++_heap, 0);
        }

        private void RecordRarity() {
            if (TryPtrOf(Keys[0], out var map) && TryImmOf(Keys[2], out ulong count))
                _rarity[map.Plus(-layout.RarityMapOffset)] = (int)count;
            else Issues.Add($"rarity map {_tiers.Count} in family {Families.Count} unresolved");
        }

        private void CommitTier() {
            if (!TryPtrOf(Keys[0], out var dest) || !TryPtrOf(Keys[1], out var src)) {
                Issues.Add($"tier commit in family {Families.Count} unresolved");
                return;
            }

            int rarity = _rarity.TryGetValue(src, out int r) ? r : 0;
            _tiers.Add(new PendingTier(ReadInt32(dest.Plus(-layout.KeyGap)) ?? -1, ReadString(src), rarity,
                ReadRecipe(src)));
        }

        private void CommitFamily() {
            int order = Families.Count;
            if (!TryPtrOf(Keys[0], out var dest) || !TryPtrOf(Keys[1], out var src)) {
                Issues.Add($"family commit {order} unresolved");
                ResetFamily();
                return;
            }

            var names = _strings.Where(s => BinaryStrings.IsName(s, " .'", allowDigitStart: true) is not null)
                .ToList();
            bool streamPlural = names.Count == _tiers.Count + 1;
            var streamTiers = streamPlural ? [.. names.Skip(1)] : names;
            int? afxId = ReadInt32(dest.Plus(-layout.KeyGap));
            string? id = ReadString(src) ?? _strings.FirstOrDefault(s => IdPattern().IsMatch(s));
            string? plural = ReadString(src.Plus(PluralOffset)) ?? (streamPlural ? names[0] : null);
            int? kind = ReadInt32(src.Plus(layout.KindOffset));
            int? dimension = ReadInt32(src.Plus(layout.KindOffset + 4));
            if (afxId is null || id is null || plural is null || kind is null || dimension is null)
                Issues.Add($"family {order} ({id ?? "?"}) has unresolved identity fields");

            var tiers = _tiers
                .Select((t, i) => new Tier(t.Level, t.Name ?? (i < streamTiers.Count ? streamTiers[i] : ""),
                    t.RarityCount, t.Recipe))
                .OrderBy(t => t.Level)
                .ToList();
            Families.Add(new Family(afxId ?? -1, order, id ?? "", plural ?? "", kind ?? -1, dimension ?? -1, tiers));
            ResetFamily();
        }

        private void ResetFamily() {
            _tiers.Clear();
            _strings.Clear();
            _prevStr = null;
        }

        private List<Ingredient> ReadRecipe(Ptr tier) {
            if (!_slots.TryGetValue((tier.Region, tier.Offset + layout.RecipeBegin), out var begin)) return [];
            if (!_slots.TryGetValue((tier.Region, tier.Offset + layout.RecipeEnd), out var end)
                || end.Region != begin.Region) {
                UnresolvedIngredients++;
                return [];
            }

            var outp = new List<Ingredient>();
            for (long k = 0; k < (end.Offset - begin.Offset) / ElementSize; k++) {
                var e = begin.Plus(k * ElementSize);
                if (ReadInt32(e) is { } afx && ReadInt32(e.Plus(4)) is { } count && ReadInt32(e.Plus(8)) is { } level)
                    outp.Add(new Ingredient(afx, level, count));
                else UnresolvedIngredients++;
            }

            return outp;
        }

        private void Materialize(ulong va) {
            if (!img.TryVaToFileOffset(va, out _, out var owner) || owner.Name is not ("__cstring" or ".rodata")) return;
            string s = BinaryStrings.ReadCstr(bin, img, va, 96);
            if (s.Length == 0) return;
            if (_prevStr is not null && _prevStr.EndsWith(s, StringComparison.Ordinal) && va > _prevVa
                && va <= _prevVa + (ulong)_prevStr.Length + 1) return;
            _prevStr = s;
            _prevVa = va;
            _strings.Add(s);
        }

        private bool TryAddress(List<string> ops, int memIdx, out Ptr addr, out string baseKey, out Ptr? writeback) {
            addr = default;
            baseKey = "";
            writeback = null;
            if (!TryMem(ops[memIdx], out string baseReg, out ulong disp, out string? idx, out int shift)) return false;
            if (!TryGpr(baseReg, out baseKey, out _) || !TryPtrOf(baseKey, out var b)) return false;
            if (memIdx + 1 < ops.Count && TryImm(ops[memIdx + 1], out ulong post)) {
                addr = b;
                writeback = b.Plus((long)post);
                return true;
            }

            ulong off = disp;
            if (idx is not null) {
                if (!TryGpr(idx, out string ik, out _) || !TryImmOf(ik, out ulong iv)) return false;
                off += iv << shift;
            }

            addr = b.Plus((long)off);
            if (IsWriteback(ops[memIdx])) writeback = addr;
            return true;
        }

        private int WriteReg(string src, Ptr addr, int forced) {
            if (TryVec(src, out int num, out int vw)) {
                var raw = new byte?[vw];
                if (_vec.TryGetValue(num, out var v)) Array.Copy(v, raw, vw);
                WriteBytes(addr, raw);
                return vw;
            }

            if (!TryGpr(src, out string key, out int gw)) return 0;
            int width = forced > 0 ? forced : gw;
            if (width == 8 && TryPtrOf(key, out var p)) {
                WritePtr(addr, p);
                return 8;
            }

            var bytes = new byte?[width];
            if (TryImmOf(key, out ulong imm)) {
                for (int k = 0; k < width; k++) bytes[k] = (byte)(imm >> (8 * k));
            }

            WriteBytes(addr, bytes);
            return width;
        }

        private void WriteBytes(Ptr addr, byte?[] bytes) {
            if (addr.Region == Binary) return;
            for (int k = 0; k < bytes.Length; k++) {
                long at = addr.Offset + k;
                for (long s = at - 7; s <= at; s++) _slots.Remove((addr.Region, s));
                if (bytes[k] is { } b) _mem[(addr.Region, at)] = b;
                else _mem.Remove((addr.Region, at));
            }
        }

        private void WritePtr(Ptr addr, Ptr value) {
            WriteBytes(addr, new byte?[8]);
            if (addr.Region != Binary) _slots[(addr.Region, addr.Offset)] = value;
        }

        private byte? ByteAt(Ptr p) {
            if (p.Region != Binary) return _mem.TryGetValue((p.Region, p.Offset), out byte b) ? b : null;
            if (!img.TryVaToFileOffset((ulong)p.Offset, out int fo, out _) || fo < 0 || fo >= bin.Length) return null;
            return bin[fo];
        }

        private byte?[] Read(Ptr p, int width) {
            var outp = new byte?[width];
            for (int k = 0; k < width; k++) outp[k] = ByteAt(p.Plus(k));
            return outp;
        }

        private int? ReadInt32(Ptr p) => TryPack(Read(p, 4), out ulong v) ? unchecked((int)(uint)v) : null;

        private long? ReadInt64(Ptr p) => TryPack(Read(p, 8), out ulong v) ? unchecked((long)v) : null;

        private string? ReadString(Ptr at) {
            if (layout.ShortStringFirst) {
                if (ByteAt(at) is not { } head) return null;
                if ((head & 1) == 0) return head == 0 ? null : Ascii(Read(at.Plus(1), head >> 1));
                if (!_slots.TryGetValue((at.Region, at.Offset + 16), out var ndkHeap)) return null;
                if (ReadInt64(at.Plus(8)) is not { } ndkSize || ndkSize is <= 0 or > 256) return null;
                return Ascii(Read(ndkHeap, (int)ndkSize));
            }

            if (ByteAt(at.Plus(23)) is not { } flag) return null;
            if ((flag & 0x80) == 0) return flag == 0 ? null : Ascii(Read(at, flag));
            if (!_slots.TryGetValue((at.Region, at.Offset), out var heap)) return null;
            if (ReadInt64(at.Plus(8)) is not { } size || size is <= 0 or > 256) return null;
            return Ascii(Read(heap, (int)size));
        }

        private bool TryImmOf(string key, out ulong v) {
            v = 0;
            return key == "zr" || _imm.TryGetValue(key, out v);
        }

        private bool TryPtrOf(string key, out Ptr p) {
            if (key == "sp" && !_ptr.ContainsKey(key)) _ptr[key] = new Ptr(Stack, 0);
            return _ptr.TryGetValue(key, out p);
        }

        private bool TryOperand(List<string> ops, int i, out ulong value) {
            value = 0;
            int shift = ShiftOf(ops.GetRange(i + 1, ops.Count - i - 1));
            if (ops[i].StartsWith('#')) {
                if (!TryImm(ops[i], out value)) return false;
            } else if (!TryGpr(ops[i], out string key, out _) || !TryImmOf(key, out value)) {
                return false;
            }

            value <<= shift;
            return true;
        }

        private void SetImm(string tok, ulong v) {
            if (!TryGpr(tok, out string key, out int width) || key is "zr" or "sp") return;
            _imm[key] = width == 4 ? v & 0xFFFFFFFF : v;
            _ptr.Remove(key);
        }

        private void SetPtr(string tok, Ptr p) {
            if (TryGpr(tok, out string key, out _)) SetPtrKey(key, p);
        }

        private void SetPtrKey(string key, Ptr p) {
            if (key == "zr") return;
            _ptr[key] = p;
            _imm.Remove(key);
        }

        private void Clobber(string tok) {
            if (TryVec(tok, out int num, out _)) {
                _vec.Remove(num);
                return;
            }

            if (!TryGpr(tok, out string key, out _)) return;
            _imm.Remove(key);
            _ptr.Remove(key);
        }

        private void ClobberFirst(List<string> ops) {
            if (ops.Count > 0) Clobber(ops[0]);
        }

        private static int RegWidth(string tok) {
            if (TryVec(tok, out _, out int vw)) return vw;
            return TryGpr(tok, out _, out int gw) ? gw : 8;
        }

        private static bool TryGpr(string tok, out string key, out int width) {
            key = "";
            width = 8;
            string t = tok.Trim();
            if (t is "sp" or "wsp") {
                key = "sp";
                return true;
            }

            if (t is "xzr" or "wzr") {
                key = "zr";
                width = t[0] == 'w' ? 4 : 8;
                return true;
            }

            if (!LooksLikeGpr(t)) return false;
            key = t[1..];
            width = t[0] == 'w' ? 4 : 8;
            return true;
        }

        private static bool TryVec(string tok, out int num, out int width) {
            num = 0;
            width = 0;
            string t = tok.Trim();
            int dot = t.IndexOf('.', StringComparison.Ordinal);
            if (dot > 0) t = t[..dot];
            if (t.Length < 2 || !int.TryParse(t.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out num))
                return false;
            width = t[0] switch {
                'q' or 'v' => 16,
                'd' => 8,
                's' => 4,
                'h' => 2,
                'b' => 1,
                _ => 0
            };
            return width > 0;
        }

        private static byte?[] Zeros(int width) {
            var outp = new byte?[width];
            Array.Fill(outp, (byte)0);
            return outp;
        }

        private static bool TryPack(byte?[] bytes, out ulong value) {
            value = 0;
            for (int k = 0; k < bytes.Length; k++) {
                if (bytes[k] is not { } b) return false;
                value |= (ulong)b << (8 * k);
            }

            return true;
        }

        private static string? Ascii(byte?[] bytes) {
            var sb = new StringBuilder(bytes.Length);
            foreach (byte? b in bytes) {
                if (b is not { } c || c is < 0x20 or > 0x7e) return null;
                sb.Append((char)c);
            }

            return sb.ToString();
        }
    }
}
