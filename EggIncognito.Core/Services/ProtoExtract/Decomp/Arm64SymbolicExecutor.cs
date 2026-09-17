using Gee.External.Capstone.Arm64;

namespace EggIncognito.Core.Services.ProtoExtract.Decomp;

public static class Arm64SymbolicExecutor {
    private const int InstrBudget = 5000;

    private static string NormKey(string name) {
        if (name.Length == 0) return name;
        char c = name[0];
        int i = 1;
        while (i < name.Length && char.IsDigit(name[i])) i++;
        string num = name[1..i];
        return c is 'v' or 'q' or 'd' or 's' ? "v" + num : c is 'x' or 'w' ? "x" + num : name;
    }

    public static ExecResult Run(
        byte[] code, MachoSymbols.FuncRange fn,
        IReadOnlyList<MachoSymbols.Symbol> syms, IReadOnlyDictionary<string, ExprNode> seedInputs,
        Func<string, ExprNode[], ExprNode?> resolveCall)
        => Run(code, fn, syms, seedInputs, null, resolveCall);

    public static ExecResult Run(
        byte[] code, MachoSymbols.FuncRange fn,
        IReadOnlyList<MachoSymbols.Symbol> syms, IReadOnlyDictionary<string, ExprNode> seedInputs,
        IReadOnlyDictionary<string, string>? seedBases,
        Func<string, ExprNode[], ExprNode?> resolveCall) {
        var st = new State(seedInputs, seedBases);
        int n = 0;

        try {
            using var cs = Arm64Decode.CreateDisassembler();
            foreach (var insn in cs.Disassemble(code, (long)fn.Start)) {
                if (++n > InstrBudget) return st.Result("instruction budget exceeded");
                var ops = insn.Details?.Operands;
                if (ops is null) continue;
                Step(st, insn, ops, syms, resolveCall);
            }
        } catch (Exception ex) {
            return st.Result("executor error: " + ex.Message);
        }

        return st.Result("ok");
    }

    private static void Step(State st, Arm64Instruction insn, Arm64Operand[] ops,
        IReadOnlyList<MachoSymbols.Symbol> syms, Func<string, ExprNode[], ExprNode?> resolveCall) {
        switch (insn.Id) {
            case Arm64InstructionId.ARM64_INS_FMOV when ops.Length == 2 &&
                                                        ops[1].Type == Arm64OperandType.FloatingPoint &&
                                                        ops[0].Register is { } fi:
                st.SetScalar(fi.Name, new ConstExpr(ops[1].FloatingPoint));
                break;
            case Arm64InstructionId.ARM64_INS_FMOV
                when ops.Length == 2 && ops[0].Register is { } fd && ops[1].Register is { } fs:

                st.SetScalar(fd.Name, st.RegExpr(fs.Name));
                break;
            case Arm64InstructionId.ARM64_INS_FMUL: st.Bin(ops, BinOp.Mul); break;
            case Arm64InstructionId.ARM64_INS_FADD: st.Bin(ops, BinOp.Add); break;
            case Arm64InstructionId.ARM64_INS_FSUB: st.Bin(ops, BinOp.Sub); break;
            case Arm64InstructionId.ARM64_INS_FDIV: st.Bin(ops, BinOp.Div); break;
            case Arm64InstructionId.ARM64_INS_FNEG: st.Un(ops, UnOp.Neg); break;
            case Arm64InstructionId.ARM64_INS_FABS: st.Un(ops, UnOp.Abs); break;
            case Arm64InstructionId.ARM64_INS_FSQRT: st.Un(ops, UnOp.Sqrt); break;
            case Arm64InstructionId.ARM64_INS_FMAXNM: st.Bin(ops, BinOp.Max); break;
            case Arm64InstructionId.ARM64_INS_FMINNM: st.Bin(ops, BinOp.Min); break;
            case Arm64InstructionId.ARM64_INS_FCVT
                when ops.Length == 2 && ops[0].Register is { } cd && ops[1].Register is { } cs2:
                st.SetScalar(cd.Name, st.Scalar(cs2.Name));
                break;
            case Arm64InstructionId.ARM64_INS_FMADD when ops.Length == 4 && ops[0].Register is { } md:
                st.SetScalar(md.Name, new Binary(BinOp.Add, st.Scalar(RegName(ops, 3)),
                    new Binary(BinOp.Mul, st.Scalar(RegName(ops, 1)), st.Scalar(RegName(ops, 2)))));
                break;
            case Arm64InstructionId.ARM64_INS_FCSEL when ops.Length >= 3 && ops[0].Register is { } cd2:
                st.SetScalar(cd2.Name,
                    new SelectExpr(new Opaque("cond", []), st.Scalar(RegName(ops, 1)), st.Scalar(RegName(ops, 2))));
                break;

            case Arm64InstructionId.ARM64_INS_MOVZ when ops.Length >= 2 && ops[0].Register is { } mz &&
                                                        ops[1].Type == Arm64OperandType.Immediate:
                st.SetGp(mz.Name, ShiftImm(ops));
                break;
            case Arm64InstructionId.ARM64_INS_MOVK when ops.Length >= 2 && ops[0].Register is { } mk &&
                                                        ops[1].Type == Arm64OperandType.Immediate:
                st.SetGp(mk.Name, st.GpVal(mk.Name) | ShiftImm(ops));
                break;
            case Arm64InstructionId.ARM64_INS_MOV
                when ops.Length == 2 && ops[0].Register is { } gd && ops[1].Register is { } gs:
                st.CopyReg(gd.Name, gs.Name);
                break;
            case Arm64InstructionId.ARM64_INS_ADD when ops.Length == 3 && ops[0].Register is { } ad &&
                                                       ops[1].Register is { } an &&
                                                       ops[2].Type == Arm64OperandType.Immediate:
                st.AddImm(ad.Name, an.Name, ops[2].Immediate);
                break;
            case Arm64InstructionId.ARM64_INS_ORR when ops.Length == 3 && ops[0].Register is { } od &&
                                                       ops[1].Register is { } on &&
                                                       ops[2].Type == Arm64OperandType.Immediate:

                st.OrrImm(od.Name, on.Name, ops[2].Immediate);
                break;

            case Arm64InstructionId.ARM64_INS_INS
                when ops.Length == 2 && ops[0].Register is { } id0 && ops[1].Register is { } is0:
                st.VecInsert(id0.Name, ops[0].VectorIndex, is0.Name, ops[1].VectorIndex);
                break;
            case Arm64InstructionId.ARM64_INS_DUP
                when ops.Length == 2 && ops[0].Register is { } dd && ops[1].Register is { } ds:
                st.VecDup(dd.Name, ds.Name, ops[1].VectorIndex);
                break;
            case Arm64InstructionId.ARM64_INS_ZIP1 when ops.Length == 3 && ops[0].Register is { } zd:
                st.VecZip1(zd.Name, RegName(ops, 1), RegName(ops, 2));
                break;
            case Arm64InstructionId.ARM64_INS_EXT when ops.Length == 4 && ops[0].Register is { } ed &&
                                                       ops[3].Type == Arm64OperandType.Immediate:
                st.VecExt(ed.Name, RegName(ops, 1), RegName(ops, 2), (int)ops[3].Immediate);
                break;

            case Arm64InstructionId.ARM64_INS_STR: st.Store(ops); break;
            case Arm64InstructionId.ARM64_INS_STUR: st.Store(ops); break;
            case Arm64InstructionId.ARM64_INS_STP: st.StorePair(ops); break;
            case Arm64InstructionId.ARM64_INS_ST1: st.St1(ops); break;
            case Arm64InstructionId.ARM64_INS_LDR: st.Load(ops); break;
            case Arm64InstructionId.ARM64_INS_LDUR: st.Load(ops); break;
            case Arm64InstructionId.ARM64_INS_LDP: st.LoadPair(ops); break;

            case Arm64InstructionId.ARM64_INS_BL:
                st.DirectCall(ops, syms, resolveCall);
                break;
            case Arm64InstructionId.ARM64_INS_BLR:
                st.IndirectCall();
                break;
        }
    }

    private static long ShiftImm(Arm64Operand[] ops) {
        long v = ops[1].Immediate;

        if (ops.Length >= 2) {
            try {
                if (ops[1].ShiftOperation != Arm64ShiftOperation.Invalid) v <<= ops[1].ShiftValue;
            } catch (Exception) {
                return v;
            }
        }

        return v;
    }

    private static string RegName(Arm64Operand[] ops, int i) =>
        i < ops.Length && ops[i].Register is { } r ? r.Name : "";

    public readonly record struct CallRecord(string Name, IReadOnlyList<ExprNode> FloatArgs);

    public readonly record struct ExecResult(
        IReadOnlyDictionary<string, ExprNode> Regs,
        IReadOnlyDictionary<long, ExprNode> Stack,
        IReadOnlyDictionary<long, ExprNode> RetVec,
        ExprNode? SinkArg,
        long? SinkStackPtr,
        int Opaque,
        IReadOnlyList<CallRecord> Calls,
        string Diagnostics) {
        public ExprNode? Reg(string name) => Regs.GetValueOrDefault(NormKey(name));
    }

    private sealed class State {
        private readonly List<CallRecord> _calls = [];
        private readonly Dictionary<string, long> _gp = [];
        private readonly Dictionary<string, string> _named = [];
        private readonly Dictionary<string, (string Space, long Off)> _ptr = [];
        private readonly Dictionary<string, ExprNode> _regs = [];
        private readonly Dictionary<long, ExprNode> _retVec = [];
        private readonly Dictionary<long, ExprNode> _stack = [];
        private readonly Dictionary<string, ExprNode[]> _vecs = [];
        public int Opaque;
        public ExprNode? SinkArg;
        public long? SinkStackPtr;

        public State(IReadOnlyDictionary<string, ExprNode> seed) : this(seed, null) {
        }

        public State(IReadOnlyDictionary<string, ExprNode> seed, IReadOnlyDictionary<string, string>? seedBases) {
            foreach ((string k, var v) in seed) _regs[Norm(k)] = v;
            _ptr["sp"] = ("sp", 0);
            _regs["zr"] = new ConstExpr(0);
            _gp["zr"] = 0;
            if (seedBases is not null) {
                foreach ((string reg, string name) in seedBases) {
                    _named[Norm(reg)] = name;
                    if (name == "ret") _ptr[Norm(reg)] = ("ret", 0);
                }
            }
        }

        public ExecResult Result(string diag) =>
            new(_regs, _stack, _retVec, SinkArg, SinkStackPtr, Opaque, _calls, diag);

        public void RecordCall(string name, ExprNode[] floatArgs) =>
            _calls.Add(new CallRecord(name, floatArgs.Select(ExprNode.Fold).ToArray()));

        public ExprNode Scalar(string name) => _regs.TryGetValue(Norm(name), out var e) ? e : new Opaque("unset", []);
        public ExprNode RegExpr(string name) => Scalar(name);

        public void SetScalar(string name, ExprNode e) => _regs[Norm(name)] = e;

        public void Bin(Arm64Operand[] ops, BinOp op) {
            if (ops.Length < 3 || ops[0].Register is not { } d) return;

            if (IsVec(ops[0])) {
                var a = Lanes(RegName(ops, 1));
                var b = Lanes(RegName(ops, 2));
                var outl = new ExprNode[4];
                for (int i = 0; i < 4; i++) outl[i] = new Binary(op, a[i], b[i]);
                SetVec(d.Name, outl);
            } else {
                SetScalar(d.Name, new Binary(op, Scalar(RegName(ops, 1)), Scalar(RegName(ops, 2))));
            }
        }

        public void Un(Arm64Operand[] ops, UnOp op) {
            if (ops.Length < 2 || ops[0].Register is not { } d) return;
            SetScalar(d.Name, new Unary(op, Scalar(RegName(ops, 1))));
        }

        public void SetGp(string name, long v) {
            _gp[Norm(name)] = v;
            _regs[Norm(name)] = new ConstExpr(ReinterpretFloat(v));
        }

        public long GpVal(string name) => _gp.GetValueOrDefault(Norm(name), 0);

        public void CopyReg(string d, string s) {
            d = Norm(d);
            s = Norm(s);
            if (_regs.TryGetValue(s, out var e)) _regs[d] = e;
            if (_gp.TryGetValue(s, out long g)) _gp[d] = g;
            if (_ptr.TryGetValue(s, out var p)) _ptr[d] = p;
            else _ptr.Remove(d);
            if (_named.TryGetValue(s, out string? nb)) _named[d] = nb;
            else _named.Remove(d);
            if (_vecs.TryGetValue(s, out var v)) _vecs[d] = (ExprNode[])v.Clone();
        }

        public void AddImm(string d, string n, long imm) {
            d = Norm(d);
            n = Norm(n);
            if (_ptr.TryGetValue(n, out var p)) {
                _ptr[d] = (p.Space, p.Off + imm);
            } else {
                _ptr.Remove(d);
                if (_gp.TryGetValue(n, out long g)) _gp[d] = g + imm;
            }
        }

        public void OrrImm(string d, string n, long imm) {
            d = Norm(d);
            n = Norm(n);
            if (_ptr.TryGetValue(n, out var p)) _ptr[d] = (p.Space, p.Off | imm);
        }

        public ExprNode[] Lanes(string name) {
            name = Norm(name);
            if (_vecs.TryGetValue(name, out var v)) return v;

            var e = _regs.TryGetValue(name, out var r) ? r : new Opaque("unset", []);
            return [e, new Opaque("unset", []), new Opaque("unset", []), new Opaque("unset", [])];
        }

        public void SetVec(string name, ExprNode[] lanes) {
            name = Norm(name);
            _vecs[name] = lanes;
            _regs[name] = new ConstExpr(0);
        }

        public void VecInsert(string d, int di, string s, int si) {
            var dl = (ExprNode[])Lanes(d).Clone();
            var sl = Lanes(s);
            if (di >= 0 && di < 4 && si >= 0 && si < 4) dl[di] = sl[si];
            SetVec(d, dl);
        }

        public void VecDup(string d, string s, int si) {
            var sl = Lanes(s);
            var e = si is >= 0 and < 4 ? sl[si] : sl[0];
            SetVec(d, [e, e, e, e]);
        }

        public void VecZip1(string d, string a, string b) {
            var al = Lanes(a);
            var bl = Lanes(b);
            SetVec(d, [al[0], bl[0], al[1], bl[1]]);
        }

        public void VecExt(string d, string a, string b, int byteIdx) {
            var al = Lanes(a);
            var bl = Lanes(b);
            int sh = byteIdx / 4;
            var combined = new[] { al[0], al[1], al[2], al[3], bl[0], bl[1], bl[2], bl[3] };
            SetVec(d, [combined[sh], combined[sh + 1], combined[sh + 2], combined[sh + 3]]);
        }

        public void Store(Arm64Operand[] ops) {
            if (ops.Length < 2 || ops[^1].Type != Arm64OperandType.Memory) return;
            if (!MemTarget(ops[^1], out string space, out long off)) return;
            var src = ops[0];
            if (src.Register is not { } r) return;
            if (IsVec(src) || r.Name.StartsWith('q')) {
                var lanes = Lanes(r.Name);
                for (int i = 0; i < 4; i++) WriteSlot(space, off + i * 4, lanes[i]);
            } else if (r.Name.StartsWith('d')) {
                var lanes = Lanes(r.Name);
                WriteSlot(space, off, lanes[0]);
                WriteSlot(space, off + 4, lanes[1]);
            } else {
                WriteSlot(space, off, Scalar(r.Name));
            }
        }

        public void StorePair(Arm64Operand[] ops) {
            if (ops.Length < 3 || ops[^1].Type != Arm64OperandType.Memory) return;
            if (!MemTarget(ops[^1], out string space, out long off)) return;
            if (ops[0].Register is { } a) WriteSlot(space, off, Scalar(a.Name));
            if (ops[1].Register is { } b) WriteSlot(space, off + LaneSize(ops[0]), Scalar(b.Name));
        }

        public void St1(Arm64Operand[] ops) {
            if (ops.Length < 2) return;
            var memOp = ops[^1];
            if (!MemTarget(memOp, out string space, out long off)) return;
            if (ops[0].Register is { } v) {
                var lanes = Lanes(v.Name);
                int li = ops[0].VectorIndex >= 0 ? ops[0].VectorIndex : 0;
                WriteSlot(space, off, li < 4 ? lanes[li] : lanes[0]);
            }
        }

        public void Load(Arm64Operand[] ops) {
            if (ops.Length < 2 || ops[^1].Type != Arm64OperandType.Memory || ops[0].Register is not { } r) return;
            if (MemTarget(ops[^1], out string space, out long off) && space == "sp") {
                if (IsVec(ops[0]) || r.Name.StartsWith('q'))
                    SetVec(r.Name, [Slot(off), Slot(off + 4), Slot(off + 8), Slot(off + 12)]);
                else
                    SetScalar(r.Name, Slot(off));
                return;
            }

            _ptr.Remove(Norm(r.Name));
            if (ops[^1].Memory?.Base is { } b) {
                string? baseName = _named.TryGetValue(Norm(b.Name), out string? nb) ? nb : b.Name;
                int disp = ops[^1].Memory.Displacement;
                if (IsVec(ops[0]) || r.Name.StartsWith('q')) {
                    SetVec(r.Name,
                    [
                        new Field(baseName, disp), new Field(baseName, disp + 4), new Field(baseName, disp + 8),
                        new Field(baseName, disp + 12)
                    ]);
                } else {
                    SetScalar(r.Name, new Field(baseName, disp));
                }
            } else {
                _regs.Remove(Norm(r.Name));
            }
        }

        public void LoadPair(Arm64Operand[] ops) {
            if (ops.Length < 3 || ops[^1].Type != Arm64OperandType.Memory) return;
            int sz = LaneSize(ops[0]);
            if (MemTarget(ops[^1], out string space, out long off) && space == "sp") {
                if (ops[0].Register is { } a) SetScalar(a.Name, Slot(off));
                if (ops[1].Register is { } b) SetScalar(b.Name, Slot(off + sz));
                return;
            }

            if (ops[^1].Memory?.Base is { } mb) {
                string? baseName = _named.TryGetValue(Norm(mb.Name), out string? nb) ? nb : mb.Name;
                int disp = ops[^1].Memory.Displacement;
                if (ops[0].Register is { } a) SetScalar(a.Name, new Field(baseName, disp));
                if (ops[1].Register is { } b) SetScalar(b.Name, new Field(baseName, disp + sz));
            }
        }

        private ExprNode Slot(long off) => _stack.TryGetValue(off, out var e) ? e : new Field("stack", off);

        private bool MemTarget(Arm64Operand mem, out string space, out long off) {
            space = "";
            off = 0;
            if (mem.Memory?.Base is not { } b) return false;
            if (!_ptr.TryGetValue(Norm(b.Name), out var p)) return false;
            space = p.Space;
            off = p.Off + mem.Memory.Displacement;
            return true;
        }

        private void WriteSlot(string space, long off, ExprNode e) {
            if (space == "ret") _retVec[off] = e;
            else _stack[off] = e;
        }

        public void DirectCall(Arm64Operand[] ops, IReadOnlyList<MachoSymbols.Symbol> syms,
            Func<string, ExprNode[], ExprNode?> resolveCall) {
            ulong target = ops.Length == 1 && ops[0].Type == Arm64OperandType.Immediate ? (ulong)ops[0].Immediate : 0;
            string name = Nearest(syms, target);
            var args = new[] { Scalar("s0"), Scalar("s1"), Scalar("s2") };
            RecordCall(name, args);
            var modeled = resolveCall(name, args);
            if (modeled is null) {
                SetScalar("s0", new Opaque(name, []));
                SetScalar("x0", Scalar("s0"));
                Opaque++;
                return;
            }

            if (modeled is Opaque { Call: "@sink" } sink && sink.Args.Count > 0) SinkArg = sink.Args[0];
            SetScalar("s0", modeled);
            SetScalar("x0", modeled);
        }

        public void IndirectCall() {
            Opaque++;
            if (_ptr.TryGetValue("x2", out var p) && p.Space == "sp") SinkStackPtr = p.Off;
        }

        private static string Nearest(IReadOnlyList<MachoSymbols.Symbol> syms, ulong target) {
            string? best = null;
            ulong bestAddr = 0;
            foreach (var s in syms) {
                if (s.Value == 0 || string.IsNullOrEmpty(s.Name)) continue;
                if (s.Value <= target && s.Value >= bestAddr) {
                    bestAddr = s.Value;
                    best = s.Name;
                }
            }

            return best ?? $"0x{target:x}";
        }

        private static double ReinterpretFloat(long bits) => BitConverter.Int32BitsToSingle((int)(bits & 0xFFFFFFFF));

        private static bool IsVec(Arm64Operand op) =>
            op.VectorArrangementSpecifier != Arm64VectorArrangementSpecifier.Invalid;

        private static int LaneSize(Arm64Operand op) {
            var r = op.Register;
            return r is null ? 8 :
                r.Name.StartsWith('d') ? 8 :
                r.Name.StartsWith('s') || r.Name.StartsWith('w') ? 4 : 8;
        }

        private static string Norm(string name) =>
            name is "sp" or "wsp" ? "sp" : name is "wzr" or "xzr" ? "zr" : NormKey(name);
    }
}
