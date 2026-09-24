using System.Diagnostics.CodeAnalysis;

namespace EggIncognito.GameData;

public sealed class EffectRow {
    public EffectRow(EffectSchema schema, string id, IReadOnlyDictionary<string, object> values) {
        Schema = schema;
        Id = id;
        Values = values;
        Validate();
    }

    public EffectSchema Schema { get; }
    public string Id { get; }
    public IReadOnlyDictionary<string, object> Values { get; }

    public double GetDouble(string field) => Convert.ToDouble(Values[field]);
    public int GetInt(string field) => Convert.ToInt32(Values[field]);
    public string GetString(string field) => (string)Values[field];
    public bool GetBool(string field) => (bool)Values[field];

    public bool TryGet(string field, [MaybeNullWhen(false)] out object value) => Values.TryGetValue(field, out value);

    private void Validate() {
        foreach (string name in Schema.RequiredNames) {
            if (!Values.ContainsKey(name))
                throw new GameDataSchemaException($"Row '{Id}' missing required field '{name}'.");
        }

        foreach ((string key, var value) in Values) {
            if (!Schema.TryGetField(key, out var field))
                throw new GameDataSchemaException($"Row '{Id}' has unknown field '{key}'.");

            if (!TypeMatches(field.Type, value)) {
                throw new GameDataSchemaException(
                    $"Row '{Id}' field '{key}' expected {field.Type}, got {value?.GetType().Name ?? "null"}.");
            }
        }
    }

    private static bool TypeMatches(EffectFieldType type, object value) => type switch {
        EffectFieldType.Double => value is double or int or long or float,
        EffectFieldType.Int => value is int or long || (value is double d && d == Math.Floor(d)),
        EffectFieldType.String => value is string,
        EffectFieldType.Bool => value is bool,
        _ => false
    };
}

public sealed class GameDataSchemaException(string message) : Exception(message);
