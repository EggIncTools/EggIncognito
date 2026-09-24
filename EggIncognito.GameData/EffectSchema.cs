using System.Diagnostics.CodeAnalysis;

namespace EggIncognito.GameData;

public enum EffectFieldType {
    Double,
    Int,
    String,
    Bool
}

public sealed record EffectField(string Name, EffectFieldType Type, bool Required = true);

public sealed record EffectSchema(IReadOnlyList<EffectField> Fields) {
    private readonly Dictionary<string, EffectField> _byName =
        Fields.ToDictionary(f => f.Name, StringComparer.Ordinal);

    public IReadOnlyList<string> RequiredNames { get; } =
        Fields.Where(f => f.Required).Select(f => f.Name).ToArray();

    public bool TryGetField(string name, [MaybeNullWhen(false)] out EffectField field) =>
        _byName.TryGetValue(name, out field);
}
