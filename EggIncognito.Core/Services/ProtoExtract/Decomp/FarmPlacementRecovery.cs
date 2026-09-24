using System.Collections.Frozen;
using System.Text.Json.Nodes;

namespace EggIncognito.Core.Services.ProtoExtract.Decomp;

public static class FarmPlacementRecovery {
    private static readonly long[] BuildingExtentFields = [0x3d0, 0x3d4, 0x3d8];

    private static readonly FrozenDictionary<long, string> ExtentNames = new Dictionary<long, string> {
        [0x3d0] = "labExtent",
        [0x3d4] = "depotExtent",
        [0x3d8] = "hatcheryExtent"
    }.ToFrozenDictionary();

    public static Vec3Model Recover(byte[] bin, string needle) {
        if (bin is null || bin.Length < 64)
            return new Vec3Model(false, needle, null, null, null, 0, "binary too short");
        if (!MachoText.TryFindText(bin, out int tfo, out _, out ulong tvm))
            return new Vec3Model(false, needle, null, null, null, 0, "no __text");

        var syms = MachoSymbols.Read(bin);
        if (!MachoSymbols.TryFindFunc(syms, [needle], out var fn))
            return new Vec3Model(false, needle, null, null, null, 0, $"symbol not found: {needle}");

        if (!Arm64Decode.SliceFunction(bin, fn.Start, fn.End, tvm, tfo, out byte[] code, out _))
            return new Vec3Model(false, fn.Name, null, null, null, 0, "function range out of bounds");

        var bases = new Dictionary<string, string> { ["x0"] = "fs", ["x1"] = "gc", ["x8"] = "ret" };
        var exec = Arm64SymbolicExecutor.Run(code, fn, syms, new Dictionary<string, ExprNode>(), bases,
            KnownCallModels.Resolve);

        ExprNode? Axis(long off) {
            return exec.RetVec.TryGetValue(off, out var e) ? NameExtents(ExprNode.Fold(e)) : null;
        }

        var x = Axis(0);
        var y = Axis(4);
        var z = Axis(8);
        bool ok = x is not null;
        string diag = ok ? "ok" : "out-param X not captured";
        return new Vec3Model(ok, fn.Name, x, y, z, exec.Opaque, diag);
    }

    private static ExprNode NameExtents(ExprNode n) {
        return n switch {
            Field f when IsExtentField(f) => new Input(ExtentNames[f.Offset]),
            Unary u => new Unary(u.Op, NameExtents(u.X)),
            Binary b => new Binary(b.Op, NameExtents(b.A), NameExtents(b.B)),
            SelectExpr s => new SelectExpr(NameExtents(s.Cond), NameExtents(s.A), NameExtents(s.B)),
            _ => n
        };
    }

    private static bool IsExtentField(Field f) =>
        f.Base == "fs" && Array.IndexOf(BuildingExtentFields, f.Offset) >= 0;

    public readonly record struct Vec3Model(
        bool Ok,
        string Function,
        ExprNode? X,
        ExprNode? Y,
        ExprNode? Z,
        int OpaqueCount,
        string Diagnostics) {
        public JsonObject ToJson() => new() {
            ["ok"] = Ok,
            ["function"] = Function,
            ["x"] = X is null ? null : ExprNode.ToJson(X),
            ["y"] = Y is null ? null : ExprNode.ToJson(Y),
            ["z"] = Z is null ? null : ExprNode.ToJson(Z),
            ["opaqueCount"] = OpaqueCount,
            ["diagnostics"] = Diagnostics
        };
    }
}
