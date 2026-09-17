using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using EggIdentity.Contract;
using EggIncognito.Controllers;
using EggIncognito.Services;
using EggIncognito.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace EggIncognito.Tests;

public class ApiAccessGuardTests {
    private const byte TwoBytePrefix = 0xFE;
    private const int MinimumGatedActions = 40;

    private static readonly Dictionary<short, OpCode> OpCodeMap = BuildOpCodeMap();

    private static readonly short[] ConditionalBranches = [
        OpCodes.Brfalse_S.Value,
        OpCodes.Brtrue_S.Value,
        OpCodes.Brfalse.Value,
        OpCodes.Brtrue.Value
    ];

    private static readonly HashSet<string> FloorMismatchBaseline = ["CaptureController"];

    private static readonly int IsAtLeastToken =
        typeof(ICurrentUser).GetMethod(nameof(ICurrentUser.IsAtLeast))!.MetadataToken;

    [Fact]
    public void EveryController_DeclaresApiAccessPolicy() {
        var missing = new List<string>();
        foreach (var t in Controllers()) {
            if (t.GetCustomAttributes(typeof(ApiAccessAttribute), true).Length > 0) continue;

            var actions = Actions(t);
            if (actions.Length > 0 &&
                actions.All(m => m.GetCustomAttributes(typeof(ApiAccessAttribute), true).Length > 0)) {
                continue;
            }

            missing.Add(t.Name);
        }

        Assert.True(missing.Count == 0, "controllers missing [ApiAccess]: " + string.Join(", ", missing));
    }

    [Fact]
    public void ActionPolicy_NeverBelowControllerFloor() {
        var offenders = new List<string>();
        foreach (var t in Controllers()) {
            if (t.GetCustomAttribute<ApiAccessAttribute>(true)?.Level is not { } floor) continue;
            foreach (var a in Actions(t)) {
                if (a.GetCustomAttribute<ApiAccessAttribute>() is { } own && own.Level < floor)
                    offenders.Add($"{t.Name}.{a.Name} declares {own.Level} under controller floor {floor}");
            }
        }

        Assert.True(offenders.Count == 0,
            "action [ApiAccess] may not sit below its controller floor: " + string.Join("; ", offenders));
    }

    [Fact]
    public void DeclaredFloor_MatchesInMethodRoleGate() {
        var offenders = new List<string>();
        foreach (var t in Controllers()) {
            if (FloorMismatchBaseline.Contains(t.Name)) continue;
            foreach (var a in Actions(t)) {
                if (GateOf(t, a) is not { } gate) continue;
                var floor = DeclaredFloor(a);
                if (floor != gate)
                    offenders.Add($"{t.Name}.{a.Name} declares {Name(floor)} but gates on {gate}");
            }
        }

        Assert.True(offenders.Count == 0,
            "declared [ApiAccess] floor must equal the in-method role gate: " + string.Join("; ", offenders));
    }

    [Fact]
    public void RoleGateScanner_StillSeesKnownGates() {
        var devices = typeof(DevicesController);
        var designs = typeof(EnvDesignController);

        Assert.Equal(ApiAccessLevel.Admin, GateOf(devices, devices.GetMethod(nameof(DevicesController.JobHistory))!));
        Assert.Equal(ApiAccessLevel.Contributor,
            GateOf(designs, designs.GetMethod(nameof(EnvDesignController.Save))!));
        Assert.Equal(ApiAccessLevel.Contributor,
            GateOf(designs, designs.GetMethod(nameof(EnvDesignController.List))!));
        Assert.Null(GateOf(devices, devices.GetMethod(nameof(DevicesController.Status))!));
    }

    [Fact]
    public void RoleGateScanner_SeesGatesAcrossTheApiSurface() {
        int gated = Controllers().Sum(t => Actions(t).Count(a => GateOf(t, a) is not null));
        Assert.True(gated >= MinimumGatedActions,
            $"role-gate scanner found only {gated} gated actions; below {MinimumGatedActions} it is blind and " +
            "DeclaredFloor_MatchesInMethodRoleGate passes vacuously");
    }

    private static IEnumerable<Type> Controllers() =>
        typeof(Program).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && t.IsClass && !t.IsAbstract);

    private static MethodInfo[] Actions(Type t) =>
        [
            .. t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttributes(typeof(HttpMethodAttribute), true).Length > 0)
        ];

    private static ApiAccessLevel? DeclaredFloor(MethodInfo action) =>
        action.GetCustomAttribute<ApiAccessAttribute>()?.Level
        ?? action.DeclaringType?.GetCustomAttribute<ApiAccessAttribute>(true)?.Level;

    private static ApiAccessLevel? GateOf(Type controller, MethodInfo action) {
        var levels = RolesIn(BodyOf(action), controller, true).Select(LevelFor).ToList();
        return levels.Count == 0 ? null : levels.Min();
    }

    private static List<UserRole> RolesIn(byte[] il, Type controller, bool followHelpers) {
        var found = new List<UserRole>();
        var code = Decode(il);
        if (code.Count == 0) return found;

        HashSet<int> roleArgHelpers = followHelpers ? RoleArgHelpers(controller) : [];
        Dictionary<int, UserRole> flatHelpers = followHelpers ? FlatHelpers(controller) : [];

        for (int i = 0; i < code.Count; i++) {
            if (code[i].Op != OpCodes.Call && code[i].Op != OpCodes.Callvirt) continue;
            int token = code[i].Operand;

            if (token == IsAtLeastToken && GuardsBranch(code, i) && RoleAt(code, i) is { } direct) {
                found.Add(direct);
                continue;
            }

            if (roleArgHelpers.Contains(token) && RoleAt(code, i) is { } passed) {
                found.Add(passed);
                continue;
            }

            if (flatHelpers.TryGetValue(token, out var helperRole)) found.Add(helperRole);
        }

        return found;
    }

    private static HashSet<int> RoleArgHelpers(Type controller) =>
        [
            .. GateShapedMethods(controller)
                .Where(m => m.GetParameters() is [var p] && p.ParameterType == typeof(UserRole))
                .Select(m => m.MetadataToken)
        ];

    private static Dictionary<int, UserRole> FlatHelpers(Type controller) {
        var map = new Dictionary<int, UserRole>();
        foreach (var m in GateShapedMethods(controller).Where(m => m.GetParameters().Length == 0)) {
            var roles = RolesIn(BodyOf(m), controller, false);
            if (roles.Count > 0) map[m.MetadataToken] = roles.Min();
        }

        return map;
    }

    private static IEnumerable<MethodInfo> GateShapedMethods(Type controller) =>
        controller.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public |
                              BindingFlags.DeclaredOnly)
            .Where(m => typeof(IActionResult).IsAssignableFrom(m.ReturnType));

    private static bool GuardsBranch(List<Instruction> code, int callAt) =>
        callAt + 1 < code.Count && Array.IndexOf(ConditionalBranches, code[callAt + 1].Op.Value) >= 0;

    private static UserRole? RoleAt(List<Instruction> code, int callAt) {
        if (callAt == 0 || ConstOf(code[callAt - 1]) is not { } v) return null;
        return Enum.IsDefined((UserRole)v) ? (UserRole)v : null;
    }

    private static int? ConstOf(Instruction ins) {
        if (ins.Op == OpCodes.Ldc_I4 || ins.Op == OpCodes.Ldc_I4_S) return ins.Operand;
        if (ins.Op.Value >= OpCodes.Ldc_I4_0.Value && ins.Op.Value <= OpCodes.Ldc_I4_8.Value)
            return ins.Op.Value - OpCodes.Ldc_I4_0.Value;
        return null;
    }

    private static List<Instruction> Decode(byte[] il) {
        var code = new List<Instruction>();
        int i = 0;
        while (i < il.Length) {
            int at = i;
            bool wide = il[i] == TwoBytePrefix;
            if (wide && i + 1 >= il.Length) throw new InvalidOperationException($"truncated opcode at IL_{at:x4}");
            short value = wide ? unchecked((short)((TwoBytePrefix << 8) | il[i + 1])) : il[i];
            i += wide ? 2 : 1;
            if (!OpCodeMap.TryGetValue(value, out var op))
                throw new InvalidOperationException($"unknown opcode 0x{value:x4} at IL_{at:x4}");

            int size = OperandSize(op, il, i);
            if (size < 0 || i + size > il.Length)
                throw new InvalidOperationException($"bad operand for {op.Name} at IL_{at:x4}");

            code.Add(new Instruction(op, size switch {
                1 => (sbyte)il[i],
                4 => BitConverter.ToInt32(il, i),
                _ => 0
            }));
            i += size;
        }

        return code;
    }

    private static int OperandSize(OpCode op, byte[] il, int operandAt) => op.OperandType switch {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineBrTarget or OperandType.InlineField or OperandType.InlineI or OperandType.InlineMethod
            or OperandType.InlineSig or OperandType.InlineString or OperandType.InlineTok
            or OperandType.InlineType or OperandType.ShortInlineR => 4,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        OperandType.InlineSwitch => SwitchSize(il, operandAt),
        _ => -1
    };

    private static int SwitchSize(byte[] il, int operandAt) {
        if (operandAt + 4 > il.Length) return -1;
        int count = BitConverter.ToInt32(il, operandAt);
        return count < 0 ? -1 : 4 + (count * 4);
    }

    private static Dictionary<short, OpCode> BuildOpCodeMap() {
        var map = new Dictionary<short, OpCode>();
        foreach (var f in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)) {
            if (f.GetValue(null) is OpCode op) map[op.Value] = op;
        }

        return map;
    }

    private static byte[] BodyOf(MethodInfo m) {
        var machine = m.GetCustomAttribute<StateMachineAttribute>()?.StateMachineType;
        var move = machine?.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .FirstOrDefault(x => x.Name.EndsWith("MoveNext", StringComparison.Ordinal));
        return ((MethodBase?)move ?? m).GetMethodBody()?.GetILAsByteArray() ?? [];
    }

    private static ApiAccessLevel LevelFor(UserRole role) => role switch {
        UserRole.Admin => ApiAccessLevel.Admin,
        UserRole.Contributor => ApiAccessLevel.Contributor,
        _ => ApiAccessLevel.Authenticated
    };

    private static string Name(ApiAccessLevel? level) => level?.ToString() ?? "nothing";

    private readonly record struct Instruction(OpCode Op, int Operand);
}
