using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using static EggIncognito.Core.Services.ProtoExtract.Arm64Operands;

namespace EggIncognito.Core.Services.ProtoExtract;

public static class StructInitReader {
    private static readonly FrozenSet<string> TrackedProducers =
        new[] { "adrp", "add", "mov", "ldr", "ldur", "ldp", "movz", "movk", "movn", "orr" }
            .ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> NonWritingMnemonics =
        new[] { "cmp", "cmn", "tst", "fcmp", "str", "stur", "strb", "sturb", "strh", "sturh", "stp" }
            .ToFrozenSet(StringComparer.Ordinal);

    public static Result Read(byte[] bin, string initSymbol, int maxInstructions = 100_000)
        => ReadWith(bin, BinaryImage.Load(bin)?.Symbols ?? [], initSymbol, maxInstructions);

    public static Result ReadWith(byte[] bin, IReadOnlyList<MachoSymbols.Symbol> syms, string initSymbol,
        int maxInstructions = 100_000) {
        var lst = Arm64DataTableReader.ListWith(bin, syms, [initSymbol], maxInstructions);
        return !lst.Ok ? new Result(false, [], lst.Diagnostics) : Walk(bin, lst.Instructions, false);
    }

    public static Result ReadRange(byte[] bin, ulong startVa, ulong endVa, int maxInstructions = 100_000,
        bool writeback = false) {
        var lst = Arm64DataTableReader.ListRange(bin, startVa, endVa, maxInstructions);
        return !lst.Ok ? new Result(false, [], lst.Diagnostics) : Walk(bin, lst.Instructions, writeback);
    }

    private static Result Walk(byte[] bin, IReadOnlyList<Arm64DataTableReader.Insn> insns, bool writeback) {
        var img = BinaryImage.Load(bin);
        var page = new Dictionary<string, ulong>(StringComparer.Ordinal);
        var imm = new Dictionary<string, (ulong Val, bool Wide)>(StringComparer.Ordinal);
        var vec = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var vecSrc = new Dictionary<string, ulong>(StringComparer.Ordinal);
        var byteWrites = new Dictionary<ulong, Dictionary<long, byte>>();
        var ptrWrites = new Dictionary<ulong, Dictionary<long, ulong>>();
        var tplWrites = new Dictionary<ulong, Dictionary<long, ulong>>();

        bool TryResolveMem(string token, out string reg, out ulong off) {
            if (!TryMem(token, out reg, out off, out string? idx, out int sh)) return false;
            if (idx is null) return true;
            if (!imm.TryGetValue(RegNum(idx), out var iv)) return false;
            off += iv.Val << sh;
            return true;
        }

        byte[]? ReadVecFrom(ulong va, int width) {
            if (img is null || !img.TryVaToFileOffset(va, out int fo, out _)) return null;
            if (fo < 0 || fo + width > bin.Length) return null;
            byte[] buf = new byte[width];
            Array.Copy(bin, fo, buf, 0, width);
            return buf;
        }

        void StoreBytes(ulong baseVa, long off, byte[] raw, ulong? srcVa) {
            if (!byteWrites.TryGetValue(baseVa, out var bytes)) {
                bytes = [];
                byteWrites[baseVa] = bytes;
            }

            for (int k = 0; k < raw.Length; k++)
                bytes[off + k] = raw[k];

            if (srcVa is { } sv) {
                if (!tplWrites.TryGetValue(baseVa, out var tpls)) {
                    tpls = [];
                    tplWrites[baseVa] = tpls;
                }

                tpls[off] = sv;
            }
        }

        void Store(ulong baseVa, long off, int width, ulong raw, ulong? ptr) {
            if (!byteWrites.TryGetValue(baseVa, out var bytes)) {
                bytes = [];
                byteWrites[baseVa] = bytes;
            }

            for (int k = 0; k < width; k++)
                bytes[off + k] = (byte)(raw >> (k * 8));

            if (ptr is { } p) {
                if (!ptrWrites.TryGetValue(baseVa, out var ptrs)) {
                    ptrs = [];
                    ptrWrites[baseVa] = ptrs;
                }

                ptrs[off] = p;
            }
        }

        foreach (var i in insns) {
            var ops = SplitOps(i.Operands);

            if (!TrackedProducers.Contains(i.Mnemonic) && !NonWritingMnemonics.Contains(i.Mnemonic)
                                                       && ops.Count >= 1 && LooksLikeGpr(ops[0])) {
                page.Remove(ops[0]);
                imm.Remove(RegNum(ops[0]));
            }

            if (ops.Count >= 1 && LooksLikeVecReg(ops[0]) && i.Mnemonic is not ("ldr" or "ldur" or "ldp")
                && !NonWritingMnemonics.Contains(i.Mnemonic)) {
                vec.Remove(ops[0]);
                vecSrc.Remove(ops[0]);
            }

            switch (i.Mnemonic) {
                case "bl":
                case "blr":
                    for (int r = 0; r <= 17; r++) {
                        page.Remove("x" + r);
                        page.Remove("w" + r);
                        imm.Remove(r.ToString(CultureInfo.InvariantCulture));
                    }

                    break;

                case "adrp":
                    if (ops.Count == 2 && TryImm(ops[1], out ulong pg)) {
                        page[ops[0]] = pg;
                        imm.Remove(RegNum(ops[0]));
                    }

                    break;

                case "add":
                    if (ops.Count == 3 && page.TryGetValue(ops[1], out ulong addBase) &&
                        TryImm(ops[2], out ulong addOff)) {
                        page[ops[0]] = addBase + addOff;
                        imm.Remove(RegNum(ops[0]));
                    } else if (ops.Count >= 1) {
                        page.Remove(ops[0]);
                        imm.Remove(RegNum(ops[0]));
                    }

                    break;

                case "mov":
                    if (ops.Count == 2) {
                        if (page.TryGetValue(ops[1], out ulong mv)) {
                            page[ops[0]] = mv;
                            imm.Remove(RegNum(ops[0]));
                        } else if (imm.TryGetValue(RegNum(ops[1]), out var mi)) {
                            imm[RegNum(ops[0])] = mi;
                            page.Remove(ops[0]);
                        } else if (TryImm(ops[1], out ulong mc)) {
                            imm[RegNum(ops[0])] = (mc, ops[0].StartsWith('x'));
                            page.Remove(ops[0]);
                        } else {
                            page.Remove(ops[0]);
                            imm.Remove(RegNum(ops[0]));
                        }
                    }

                    break;

                case "ldr":
                case "ldur":
                    if (ops.Count >= 2 && LooksLikeVecReg(ops[0]) &&
                        TryResolveMem(ops[^1], out string ldQreg, out ulong ldQoff)
                        && page.TryGetValue(ldQreg, out ulong ldQbase)) {
                        ulong src = ldQbase + ldQoff;
                        byte[]? buf = ReadVecFrom(src, VecWidth(ops[0]));
                        if (buf is not null) {
                            vec[ops[0]] = buf;
                            vecSrc[ops[0]] = src;
                        } else {
                            vec.Remove(ops[0]);
                            vecSrc.Remove(ops[0]);
                        }
                    } else if (ops.Count >= 2 && !LooksLikeVecReg(ops[0]) &&
                               TryResolveMem(ops[^1], out string ldMreg, out ulong ldMoff) &&
                               page.TryGetValue(ldMreg, out ulong ldMbase)) {
                        page[ops[0]] = ldMbase + ldMoff;
                        imm.Remove(RegNum(ops[0]));
                    } else if (ops.Count >= 1) {
                        page.Remove(ops[0]);
                        imm.Remove(RegNum(ops[0]));
                        vec.Remove(ops[0]);
                        vecSrc.Remove(ops[0]);
                    }

                    break;

                case "ldp":
                    if (ops.Count >= 3 && LooksLikeVecReg(ops[0]) &&
                        TryResolveMem(ops[^1], out string ldpReg, out ulong ldpOff)
                        && page.TryGetValue(ldpReg, out ulong ldpBase)) {
                        int w = VecWidth(ops[0]);
                        ulong loSrc = ldpBase + ldpOff;
                        ulong hiSrc = ldpBase + ldpOff + (ulong)w;
                        byte[]? lo = ReadVecFrom(loSrc, w);
                        byte[]? hi = ReadVecFrom(hiSrc, w);
                        if (lo is not null) {
                            vec[ops[0]] = lo;
                            vecSrc[ops[0]] = loSrc;
                        } else {
                            vec.Remove(ops[0]);
                            vecSrc.Remove(ops[0]);
                        }

                        if (hi is not null) {
                            vec[ops[1]] = hi;
                            vecSrc[ops[1]] = hiSrc;
                        } else {
                            vec.Remove(ops[1]);
                            vecSrc.Remove(ops[1]);
                        }
                    }

                    break;

                case "movz":
                    if (ops.Count >= 2 && TryImm(ops[1], out ulong mz)) {
                        int shift = ShiftOf(ops);
                        bool wide = ops[0].StartsWith('x');
                        imm[RegNum(ops[0])] = (mz << shift, wide);
                        page.Remove(ops[0]);
                    }

                    break;

                case "orr":
                    if (ops.Count == 3 && IsZeroReg(ops[1]) && TryImm(ops[2], out ulong orrImm)) {
                        imm[RegNum(ops[0])] = (orrImm, ops[0].StartsWith('x'));
                        page.Remove(ops[0]);
                    } else if (ops.Count >= 1) {
                        page.Remove(ops[0]);
                        imm.Remove(RegNum(ops[0]));
                    }

                    break;

                case "movn":
                    if (ops.Count >= 2 && TryImm(ops[1], out ulong mn)) {
                        int shift = ShiftOf(ops);
                        bool wide = ops[0].StartsWith('x');
                        ulong raw = ~(mn << shift);
                        if (!wide) raw &= 0xFFFFFFFF;
                        imm[RegNum(ops[0])] = (raw, wide);
                        page.Remove(ops[0]);
                    }

                    break;

                case "movk":
                    if (ops.Count >= 2 && TryImm(ops[1], out ulong mk)) {
                        int shift = ShiftOf(ops);
                        string key = RegNum(ops[0]);
                        var prev = imm.TryGetValue(key, out var p) ? p : (0UL, ops[0].StartsWith('x'));
                        ulong val = (prev.Item1 & ~(0xFFFFUL << shift)) | (mk << shift);
                        imm[key] = (val, prev.Item2 || ops[0].StartsWith('x'));
                    }

                    break;

                case "str":
                case "stur":
                case "strb":
                case "sturb":
                case "strh":
                case "sturh":
                    if (ops.Count >= 2 && TryResolveMem(ops[^1], out string sReg, out ulong sOffU) &&
                        page.TryGetValue(sReg, out ulong sBase)) {
                        long off = unchecked((long)sOffU);
                        string rt = ops[0];
                        if (LooksLikeVecReg(rt)) {
                            if (vec.TryGetValue(rt, out byte[]? vv))
                                StoreBytes(sBase, off, vv, vecSrc.TryGetValue(rt, out ulong sv) ? sv : null);
                        } else {
                            int width = i.Mnemonic switch {
                                "strb" or "sturb" => 1,
                                "strh" or "sturh" => 2,
                                _ => rt.StartsWith('x') ? 8 : 4
                            };
                            if (imm.TryGetValue(RegNum(rt), out var iv))
                                Store(sBase, off, width, iv.Val, null);
                            else if (page.TryGetValue(rt, out ulong pv))
                                Store(sBase, off, width, pv, pv);
                        }

                        if (writeback && IsWriteback(ops[^1])) page[sReg] = sBase + (ulong)off;
                    }

                    break;

                case "stp":
                    if (ops.Count >= 3 && TryResolveMem(ops[^1], out string pReg, out ulong pOffU) &&
                        page.TryGetValue(pReg, out ulong pBase)) {
                        long off0 = unchecked((long)pOffU);
                        if (LooksLikeVecReg(ops[0])) {
                            int w = VecWidth(ops[0]);
                            if (vec.TryGetValue(ops[0], out byte[]? vv0))
                                StoreBytes(pBase, off0, vv0, vecSrc.TryGetValue(ops[0], out ulong sv0) ? sv0 : null);
                            if (vec.TryGetValue(ops[1], out byte[]? vv1)) {
                                StoreBytes(pBase, off0 + w, vv1,
                                    vecSrc.TryGetValue(ops[1], out ulong sv1) ? sv1 : null);
                            }
                        } else {
                            int w0 = ops[0].StartsWith('x') ? 8 : 4;
                            if (imm.TryGetValue(RegNum(ops[0]), out var iv0)) Store(pBase, off0, w0, iv0.Val, null);
                            if (imm.TryGetValue(RegNum(ops[1]), out var iv1))
                                Store(pBase, off0 + w0, w0, iv1.Val, null);
                        }

                        if (writeback && IsWriteback(ops[^1])) page[pReg] = pBase + (ulong)off0;
                    }

                    break;
            }
        }

        var emptyPtrs = new Dictionary<long, ulong>();
        var structs = byteWrites
            .OrderBy(kv => kv.Key)
            .Select(kv => new StructInit(kv.Key, kv.Value,
                ptrWrites.GetValueOrDefault(kv.Key, emptyPtrs),
                tplWrites.GetValueOrDefault(kv.Key, emptyPtrs)))
            .ToList();
        return new Result(true, structs, $"{structs.Count} struct bases, {structs.Sum(s => s.Bytes.Count)} bytes");
    }

    public readonly record struct StructInit(
        ulong BaseVa,
        IReadOnlyDictionary<long, byte> Bytes,
        IReadOnlyDictionary<long, ulong> Pointers,
        IReadOnlyDictionary<long, ulong> Templates) {
        public bool TryTemplate(long offset, out ulong srcVa) => Templates.TryGetValue(offset, out srcVa);

        public bool TryFloat64(long offset, out double value) {
            if (TryReadBytes(offset, 8, out ulong raw)) {
                value = BitConverter.Int64BitsToDouble((long)raw);
                return true;
            }

            value = 0;
            return false;
        }

        public bool TryInt(long offset, int width, out long value) {
            if (TryReadBytes(offset, width, out ulong raw)) {
                value = width switch {
                    1 => (sbyte)raw,
                    2 => (short)raw,
                    4 => (int)raw,
                    _ => (long)raw
                };
                return true;
            }

            value = 0;
            return false;
        }

        public bool TryPointer(long offset, out ulong va) => Pointers.TryGetValue(offset, out va);

        public string TryInlineString(long start, int maxLen = 32) {
            var bytes = new List<byte>(maxLen);
            for (long off = start; off < start + maxLen; off++) {
                if (!Bytes.TryGetValue(off, out byte b) || b == 0) break;
                if (b is < 0x20 or > 0x7e) break;
                bytes.Add(b);
            }

            return Encoding.ASCII.GetString([.. bytes]);
        }

        public bool TryInlineStringComplete(long start, out string value, int maxLen = 32) {
            var bytes = new List<byte>(maxLen);
            for (long off = start; off < start + maxLen; off++) {
                if (!Bytes.TryGetValue(off, out byte b)) {
                    value = "";
                    return false;
                }

                if (b == 0) {
                    value = Encoding.ASCII.GetString([.. bytes]);
                    return bytes.Count > 0;
                }

                if (b is < 0x20 or > 0x7e) {
                    value = "";
                    return false;
                }

                bytes.Add(b);
            }

            value = "";
            return false;
        }

        private bool TryReadBytes(long start, int width, out ulong value) {
            value = 0;
            for (int k = 0; k < width; k++) {
                if (!Bytes.TryGetValue(start + k, out byte b)) return false;
                value |= (ulong)b << (k * 8);
            }

            return true;
        }
    }

    public readonly record struct Result(bool Ok, IReadOnlyList<StructInit> Structs, string Diagnostics);
}
