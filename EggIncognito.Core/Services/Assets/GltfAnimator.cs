using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using SharpGLTF.Schema2;
using SharpGLTF.Transforms;

namespace EggIncognito.Core.Services.Assets;

public static class GltfAnimator {
    public enum AnimationKind {
        SpinY,

        SpinZ,

        HoverSpin
    }

    public static Result Animate(byte[] glb, Options? options = null) {
        var opts = options ?? new Options();
        if (glb is null || glb.Length < 12) return Fail("input glb too short", opts);

        ModelRoot model;
        try {
            model = ModelRoot.ParseGLB(glb);
        } catch (Exception ex) {
            return Fail($"not a valid glb: {ex.Message}", opts);
        }

        var node = TargetNode(model);
        if (node is null) return Fail("glb has no node to animate", opts);

        string name = opts.Kind.ToString();
        try {
            ApplyAnimation(node, opts, name);
        } catch (Exception ex) {
            return Fail($"animation authoring failed: {ex.Message}", opts);
        }

        byte[] outGlb;
        try {
            outGlb = [.. model.WriteGLB()];
        } catch (Exception ex) {
            return Fail($"glb write failed: {ex.Message}", opts);
        }

        return new Result(true, outGlb, "ok", name, opts.DurationSeconds);
    }

    private static Node? TargetNode(ModelRoot model) {
        var scene = model.DefaultScene ?? (model.LogicalScenes.Count > 0 ? model.LogicalScenes[0] : null);
        var fromScene = scene?.VisualChildren.FirstOrDefault(n => n.Mesh is not null)
                        ?? scene?.VisualChildren.FirstOrDefault();
        return fromScene
               ?? model.LogicalNodes.FirstOrDefault(n => n.Mesh is not null)
               ?? (model.LogicalNodes.Count > 0 ? model.LogicalNodes[0] : null);
    }

    private static void ApplyAnimation(Node node, Options opts, string name) {
        float d = opts.DurationSeconds <= 0 ? 6f : opts.DurationSeconds;

        var pivot = RepivotToCenter(node);

        switch (opts.Kind) {
            case AnimationKind.SpinY:
                pivot.WithRotationAnimation(name, SpinKeys(Vector3.UnitY, d));
                break;
            case AnimationKind.SpinZ:
                pivot.WithRotationAnimation(name, SpinKeys(Vector3.UnitZ, d));
                break;
            case AnimationKind.HoverSpin:
                pivot.WithRotationAnimation(name, SpinKeys(Vector3.UnitY, d));
                pivot.WithTranslationAnimation(name, BobKeys(opts.BobAmplitude, d, pivot.LocalTransform.Translation));
                break;
            default:
                pivot.WithRotationAnimation(name, SpinKeys(Vector3.UnitY, d));
                break;
        }
    }

    private static Node RepivotToCenter(Node node) {
        if (node.Mesh is null) return node;
        var center = MeshCenter(node.Mesh);
        if (center is null) return node;
        var c = center.Value;
        if (c.Length() < 1e-6f) return node;

        var mesh = node.Mesh;
        var keepTranslation = node.LocalTransform.Translation;

        var child = node.CreateNode($"{node.Name ?? "mesh"}_geom");
        child.Mesh = mesh;
        child.LocalTransform = AffineTransform.CreateFromAny(null, null, null, -c);

        node.Mesh = null;
        node.LocalTransform = AffineTransform.CreateFromAny(null, null, null, keepTranslation + c);
        return node;
    }

    private static Vector3? MeshCenter(Mesh mesh) {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        bool any = false;
        foreach (var prim in mesh.Primitives) {
            var pos = prim.GetVertexAccessor("POSITION");
            if (pos is null) continue;
            foreach (var v in pos.AsVector3Array()) {
                any = true;
                min = Vector3.Min(min, v);
                max = Vector3.Max(max, v);
            }
        }

        return any ? (min + max) * 0.5f : null;
    }

    private static (float, Quaternion)[] SpinKeys(Vector3 axis, float duration) {
        var a = Vector3.Normalize(axis);
        const int steps = 4;
        var keys = new (float, Quaternion)[steps + 1];
        for (int i = 0; i <= steps; i++) {
            float t = duration * i / steps;
            float angle = MathF.PI * 2f * i / steps;
            keys[i] = (t, Quaternion.CreateFromAxisAngle(a, angle));
        }

        return keys;
    }

    private static (float, Vector3)[] BobKeys(float amplitude, float duration, Vector3 baseTranslation) {
        float half = duration / 2f;
        return [
            (0f, baseTranslation),
            (half, baseTranslation + new Vector3(0f, amplitude, 0f)),
            (duration, baseTranslation)
        ];
    }

    private static Result Fail(string why, Options opts) =>
        new(false, null, why, opts.Kind.ToString(), opts.DurationSeconds);

    public static AnimationKind ParseKind(string? s) =>
        Enum.TryParse<AnimationKind>(s, true, out var k) ? k : AnimationKind.SpinY;

    public sealed record Options(
        AnimationKind Kind = AnimationKind.SpinY,
        float DurationSeconds = 6f,
        float BobAmplitude = 0.15f) {
        public static Options Spin(float seconds = 6f) => new(AnimationKind.SpinY, seconds);
    }

    public sealed record Result(bool Ok, byte[]? Glb, string Diagnostics, string AnimationName, float DurationSeconds) {
        [MemberNotNullWhen(true, nameof(Glb))]
        public bool Ok { get; init; } = Ok;
    }
}
