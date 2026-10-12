using System.Globalization;
using System.Text;
using EggIncognito.Core.Services.ProtoExtract;

namespace EggIncognito.Services.Filtering;

public enum EgiFilterValueKind {
    Select,
    Text,
    Version,
    Number,
    Date,
    Bool
}

public enum EgiFilterOp {
    Is,
    IsNot,
    Greater,
    Less,
    AtLeast,
    AtMost,
    Contains,
    NotContains,
    StartsWith,
    On,
    Before,
    After,
    OnOrBefore,
    OnOrAfter,
    True,
    False
}

public sealed record EgiFilterOpDef(EgiFilterOp Op, string Label);

public sealed record EgiFilterOption(string Value, string Label);

public sealed record EgiFilterCondition(string Field, EgiFilterOp Op, string Value) {
    public bool Complete =>
        !string.IsNullOrWhiteSpace(Field)
        && (Op is EgiFilterOp.True or EgiFilterOp.False || !string.IsNullOrWhiteSpace(Value));
}

public sealed record EgiFilterGroup(IReadOnlyList<EgiFilterCondition> Conditions);

public sealed record ListQuery(string Platform, string Quick, IReadOnlyList<EgiFilterGroup> Groups) {
    public static readonly ListQuery Empty = new("", "", []);

    public bool IsEmpty => Platform.Length == 0 && Quick.Length == 0 && Groups.Count == 0;

    public string Signature() {
        var text = new StringBuilder();
        Part(text, Platform);
        Part(text, Quick);
        foreach (EgiFilterGroup group in Groups) {
            text.Append("g|");
            foreach (EgiFilterCondition condition in group.Conditions) {
                text.Append("c|");
                Part(text, condition.Field);
                text.Append((int)condition.Op).Append('|');
                Part(text, condition.Value);
            }
        }

        return text.ToString();
    }

    private static void Part(StringBuilder text, string? value) {
        string part = value ?? "";
        text.Append(part.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(part).Append('|');
    }
}

public sealed record EgiFilterFieldDef<TItem>(
    string Key,
    string Label,
    EgiFilterValueKind Kind,
    IReadOnlyList<EgiFilterOpDef> Ops,
    Func<IReadOnlyList<TItem>, IReadOnlyList<EgiFilterOption>>? Options,
    bool AdminOnly = false);

public static class EgiFilterOps {
    public static IReadOnlyList<EgiFilterOpDef> Equality { get; } = [
        new(EgiFilterOp.Is, "is"),
        new(EgiFilterOp.IsNot, "is not")
    ];

    public static IReadOnlyList<EgiFilterOpDef> Comparison { get; } = [
        new(EgiFilterOp.Is, "is"),
        new(EgiFilterOp.IsNot, "is not"),
        new(EgiFilterOp.Greater, "greater than"),
        new(EgiFilterOp.Less, "less than"),
        new(EgiFilterOp.AtLeast, "at least"),
        new(EgiFilterOp.AtMost, "at most")
    ];

    public static IReadOnlyList<EgiFilterOpDef> Text { get; } = [
        new(EgiFilterOp.Is, "is"),
        new(EgiFilterOp.IsNot, "is not"),
        new(EgiFilterOp.Contains, "contains"),
        new(EgiFilterOp.NotContains, "does not contain"),
        new(EgiFilterOp.StartsWith, "starts with")
    ];

    public static IReadOnlyList<EgiFilterOpDef> Date { get; } = [
        new(EgiFilterOp.On, "on"),
        new(EgiFilterOp.Before, "before"),
        new(EgiFilterOp.After, "after"),
        new(EgiFilterOp.OnOrBefore, "on or before"),
        new(EgiFilterOp.OnOrAfter, "on or after")
    ];

    public static IReadOnlyList<EgiFilterOpDef> Bool { get; } = [
        new(EgiFilterOp.True, "True"),
        new(EgiFilterOp.False, "False")
    ];

    public static ListQuery Prune(ListQuery query) {
        List<EgiFilterGroup> groups = [.. query.Groups
            .Select(group => group.Conditions.Where(c => c.Complete).ToList())
            .Where(kept => kept.Count > 0)
            .Select(kept => new EgiFilterGroup(kept))];
        return query with { Groups = groups };
    }

    public static bool Has(string? field, string needle) =>
        field is { Length: > 0 } && field.Contains(needle, StringComparison.OrdinalIgnoreCase);

    public static bool Prefix(string? field, string needle) =>
        field is { Length: > 0 } && field.StartsWith(needle, StringComparison.OrdinalIgnoreCase);

    public static List<EgiFilterOption> ByVersion(IEnumerable<string?> values) => [
        .. Distinct(values)
            .OrderByDescending(ProtoVersionQuality.DottedVersionKey)
            .ThenBy(v => v, StringComparer.OrdinalIgnoreCase)
            .Select(v => new EgiFilterOption(v, v))
    ];

    public static List<EgiFilterOption> ByText(IEnumerable<string?> values) => [
        .. Distinct(values)
            .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
            .Select(v => new EgiFilterOption(v, v))
    ];

    private static IEnumerable<string> Distinct(IEnumerable<string?> values) =>
        values.OfType<string>()
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase);
}

public sealed class EgiFilterSchema<TItem> {
    public required IReadOnlyList<EgiFilterFieldDef<TItem>> Fields { get; init; }
    public required Func<TItem, string> Platform { get; init; }
    public required Func<TItem, string, bool> Quick { get; init; }
    public Func<TItem, string, string?> Text { get; init; } = (_, _) => null;
    public Func<TItem, string, long?> Number { get; init; } = (_, _) => null;
    public Func<TItem, string, bool> Flag { get; init; } = (_, _) => false;
    public Func<TItem, string, DateTime?> Date { get; init; } = (_, _) => null;

    public IReadOnlyList<EgiFilterFieldDef<TItem>> FieldsFor(bool admin) =>
        admin ? Fields : [.. Fields.Where(f => !f.AdminOnly)];

    public EgiFilterFieldDef<TItem>? Field(string? key) {
        if (string.IsNullOrWhiteSpace(key)) return null;
        foreach (EgiFilterFieldDef<TItem> def in Fields) {
            if (string.Equals(def.Key, key, StringComparison.Ordinal)) return def;
        }

        return null;
    }

    public string OpLabel(string? field, EgiFilterOp op) {
        IReadOnlyList<EgiFilterOpDef> ops = Field(field)?.Ops ?? [];
        foreach (EgiFilterOpDef def in ops) {
            if (def.Op == op) return def.Label;
        }

        return op.ToString().ToLowerInvariant();
    }

    public string ConditionText(EgiFilterCondition condition) {
        string label = Field(condition.Field)?.Label ?? condition.Field;
        string op = OpLabel(condition.Field, condition.Op);
        if (condition.Op is EgiFilterOp.True or EgiFilterOp.False) return $"{label} is {op}";
        return $"{label} {op} {condition.Value}";
    }

    public bool Matches(TItem item, ListQuery query) {
        if (query.Platform.Length > 0
            && !string.Equals(Platform(item), query.Platform, StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        if (query.Quick.Length > 0 && !Quick(item, query.Quick)) return false;
        if (query.Groups.Count == 0) return true;

        foreach (EgiFilterGroup group in query.Groups) {
            if (GroupMatches(item, group)) return true;
        }

        return false;
    }

    private bool GroupMatches(TItem item, EgiFilterGroup group) {
        foreach (EgiFilterCondition condition in group.Conditions) {
            if (!condition.Complete) continue;
            if (!ConditionMatches(item, condition)) return false;
        }

        return true;
    }

    private bool ConditionMatches(TItem item, EgiFilterCondition condition) {
        if (Field(condition.Field) is not { } def) return false;

        return def.Kind switch {
            EgiFilterValueKind.Bool => BoolMatch(Flag(item, def.Key), condition.Op),
            EgiFilterValueKind.Date => DateMatch(Date(item, def.Key), condition),
            EgiFilterValueKind.Number => NumberMatch(Number(item, def.Key), condition),
            EgiFilterValueKind.Version => VersionMatch(Text(item, def.Key), condition),
            _ => TextMatch(Text(item, def.Key), condition)
        };
    }

    private static bool TextMatch(string? value, EgiFilterCondition condition) {
        if (string.IsNullOrWhiteSpace(value)) return condition.Op is EgiFilterOp.IsNot or EgiFilterOp.NotContains;

        return condition.Op switch {
            EgiFilterOp.Is => string.Equals(value, condition.Value, StringComparison.OrdinalIgnoreCase),
            EgiFilterOp.IsNot => !string.Equals(value, condition.Value, StringComparison.OrdinalIgnoreCase),
            EgiFilterOp.Contains => value.Contains(condition.Value, StringComparison.OrdinalIgnoreCase),
            EgiFilterOp.NotContains => !value.Contains(condition.Value, StringComparison.OrdinalIgnoreCase),
            EgiFilterOp.StartsWith => value.StartsWith(condition.Value, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static bool VersionMatch(string? value, EgiFilterCondition condition) {
        long left = ProtoVersionQuality.DottedVersionKey(value);
        if (left == long.MinValue) return condition.Op == EgiFilterOp.IsNot;

        long right = ProtoVersionQuality.DottedVersionKey(condition.Value);
        return right != long.MinValue && CompareMatch(left.CompareTo(right), condition.Op);
    }

    private static bool NumberMatch(long? value, EgiFilterCondition condition) {
        if (value is not { } left) return condition.Op == EgiFilterOp.IsNot;
        if (!long.TryParse(condition.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long right)) {
            return false;
        }

        return CompareMatch(left.CompareTo(right), condition.Op);
    }

    private static bool DateMatch(DateTime? value, EgiFilterCondition condition) {
        if (value is not { } stamp) return false;
        if (!DateTime.TryParse(condition.Value, CultureInfo.InvariantCulture, DateTimeStyles.None,
                out DateTime target)) {
            return false;
        }

        int cmp = stamp.ToLocalTime().Date.CompareTo(target.Date);
        return condition.Op switch {
            EgiFilterOp.On => cmp == 0,
            EgiFilterOp.Before => cmp < 0,
            EgiFilterOp.After => cmp > 0,
            EgiFilterOp.OnOrBefore => cmp <= 0,
            EgiFilterOp.OnOrAfter => cmp >= 0,
            _ => false
        };
    }

    private static bool BoolMatch(bool value, EgiFilterOp op) => op switch {
        EgiFilterOp.True => value,
        EgiFilterOp.False => !value,
        _ => false
    };

    private static bool CompareMatch(int cmp, EgiFilterOp op) => op switch {
        EgiFilterOp.Is => cmp == 0,
        EgiFilterOp.IsNot => cmp != 0,
        EgiFilterOp.Greater => cmp > 0,
        EgiFilterOp.Less => cmp < 0,
        EgiFilterOp.AtLeast => cmp >= 0,
        EgiFilterOp.AtMost => cmp <= 0,
        _ => false
    };
}
