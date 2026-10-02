using System.Globalization;
using System.Text;
using EggIncognito.Core.Services.ProtoExtract;

namespace EggIncognito.Services.Filtering;

public enum FilterValueKind {
    Select,
    Text,
    Version,
    Number,
    Date,
    Bool
}

public enum FilterOp {
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

public sealed record FilterOpDef(FilterOp Op, string Label);

public sealed record FilterOption(string Value, string Label);

public sealed record FilterCondition(string Field, FilterOp Op, string Value) {
    public bool Complete =>
        !string.IsNullOrWhiteSpace(Field)
        && (Op is FilterOp.True or FilterOp.False || !string.IsNullOrWhiteSpace(Value));
}

public sealed record FilterGroup(IReadOnlyList<FilterCondition> Conditions);

public sealed record ListQuery(string Platform, string Quick, IReadOnlyList<FilterGroup> Groups) {
    public static readonly ListQuery Empty = new("", "", []);

    public bool IsEmpty => Platform.Length == 0 && Quick.Length == 0 && Groups.Count == 0;

    public string Signature() {
        var text = new StringBuilder();
        Part(text, Platform);
        Part(text, Quick);
        foreach (FilterGroup group in Groups) {
            text.Append("g|");
            foreach (FilterCondition condition in group.Conditions) {
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

public sealed record FilterFieldDef<TItem>(
    string Key,
    string Label,
    FilterValueKind Kind,
    IReadOnlyList<FilterOpDef> Ops,
    Func<IReadOnlyList<TItem>, IReadOnlyList<FilterOption>>? Options,
    bool AdminOnly = false);

public static class FilterOps {
    public static IReadOnlyList<FilterOpDef> Equality { get; } = [
        new(FilterOp.Is, "is"),
        new(FilterOp.IsNot, "is not")
    ];

    public static IReadOnlyList<FilterOpDef> Comparison { get; } = [
        new(FilterOp.Is, "is"),
        new(FilterOp.IsNot, "is not"),
        new(FilterOp.Greater, "greater than"),
        new(FilterOp.Less, "less than"),
        new(FilterOp.AtLeast, "at least"),
        new(FilterOp.AtMost, "at most")
    ];

    public static IReadOnlyList<FilterOpDef> Text { get; } = [
        new(FilterOp.Is, "is"),
        new(FilterOp.IsNot, "is not"),
        new(FilterOp.Contains, "contains"),
        new(FilterOp.NotContains, "does not contain"),
        new(FilterOp.StartsWith, "starts with")
    ];

    public static IReadOnlyList<FilterOpDef> Date { get; } = [
        new(FilterOp.On, "on"),
        new(FilterOp.Before, "before"),
        new(FilterOp.After, "after"),
        new(FilterOp.OnOrBefore, "on or before"),
        new(FilterOp.OnOrAfter, "on or after")
    ];

    public static IReadOnlyList<FilterOpDef> Bool { get; } = [
        new(FilterOp.True, "True"),
        new(FilterOp.False, "False")
    ];

    public static ListQuery Prune(ListQuery query) {
        List<FilterGroup> groups = [.. query.Groups
            .Select(group => group.Conditions.Where(c => c.Complete).ToList())
            .Where(kept => kept.Count > 0)
            .Select(kept => new FilterGroup(kept))];
        return query with { Groups = groups };
    }

    public static bool Has(string? field, string needle) =>
        field is { Length: > 0 } && field.Contains(needle, StringComparison.OrdinalIgnoreCase);

    public static bool Prefix(string? field, string needle) =>
        field is { Length: > 0 } && field.StartsWith(needle, StringComparison.OrdinalIgnoreCase);

    public static List<FilterOption> ByVersion(IEnumerable<string?> values) => [
        .. Distinct(values)
            .OrderByDescending(ProtoVersionQuality.DottedVersionKey)
            .ThenBy(v => v, StringComparer.OrdinalIgnoreCase)
            .Select(v => new FilterOption(v, v))
    ];

    public static List<FilterOption> ByText(IEnumerable<string?> values) => [
        .. Distinct(values)
            .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
            .Select(v => new FilterOption(v, v))
    ];

    private static IEnumerable<string> Distinct(IEnumerable<string?> values) =>
        values.OfType<string>()
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase);
}

public sealed class FilterSchema<TItem> {
    public required IReadOnlyList<FilterFieldDef<TItem>> Fields { get; init; }
    public required Func<TItem, string> Platform { get; init; }
    public required Func<TItem, string, bool> Quick { get; init; }
    public Func<TItem, string, string?> Text { get; init; } = (_, _) => null;
    public Func<TItem, string, long?> Number { get; init; } = (_, _) => null;
    public Func<TItem, string, bool> Flag { get; init; } = (_, _) => false;
    public Func<TItem, string, DateTime?> Date { get; init; } = (_, _) => null;

    public IReadOnlyList<FilterFieldDef<TItem>> FieldsFor(bool admin) =>
        admin ? Fields : [.. Fields.Where(f => !f.AdminOnly)];

    public FilterFieldDef<TItem>? Field(string? key) {
        if (string.IsNullOrWhiteSpace(key)) return null;
        foreach (FilterFieldDef<TItem> def in Fields) {
            if (string.Equals(def.Key, key, StringComparison.Ordinal)) return def;
        }

        return null;
    }

    public string OpLabel(string? field, FilterOp op) {
        IReadOnlyList<FilterOpDef> ops = Field(field)?.Ops ?? [];
        foreach (FilterOpDef def in ops) {
            if (def.Op == op) return def.Label;
        }

        return op.ToString().ToLowerInvariant();
    }

    public string ConditionText(FilterCondition condition) {
        string label = Field(condition.Field)?.Label ?? condition.Field;
        string op = OpLabel(condition.Field, condition.Op);
        if (condition.Op is FilterOp.True or FilterOp.False) return $"{label} is {op}";
        return $"{label} {op} {condition.Value}";
    }

    public bool Matches(TItem item, ListQuery query) {
        if (query.Platform.Length > 0
            && !string.Equals(Platform(item), query.Platform, StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        if (query.Quick.Length > 0 && !Quick(item, query.Quick)) return false;
        if (query.Groups.Count == 0) return true;

        foreach (FilterGroup group in query.Groups) {
            if (GroupMatches(item, group)) return true;
        }

        return false;
    }

    private bool GroupMatches(TItem item, FilterGroup group) {
        foreach (FilterCondition condition in group.Conditions) {
            if (!condition.Complete) continue;
            if (!ConditionMatches(item, condition)) return false;
        }

        return true;
    }

    private bool ConditionMatches(TItem item, FilterCondition condition) {
        if (Field(condition.Field) is not { } def) return false;

        return def.Kind switch {
            FilterValueKind.Bool => BoolMatch(Flag(item, def.Key), condition.Op),
            FilterValueKind.Date => DateMatch(Date(item, def.Key), condition),
            FilterValueKind.Number => NumberMatch(Number(item, def.Key), condition),
            FilterValueKind.Version => VersionMatch(Text(item, def.Key), condition),
            _ => TextMatch(Text(item, def.Key), condition)
        };
    }

    private static bool TextMatch(string? value, FilterCondition condition) {
        if (string.IsNullOrWhiteSpace(value)) return condition.Op is FilterOp.IsNot or FilterOp.NotContains;

        return condition.Op switch {
            FilterOp.Is => string.Equals(value, condition.Value, StringComparison.OrdinalIgnoreCase),
            FilterOp.IsNot => !string.Equals(value, condition.Value, StringComparison.OrdinalIgnoreCase),
            FilterOp.Contains => value.Contains(condition.Value, StringComparison.OrdinalIgnoreCase),
            FilterOp.NotContains => !value.Contains(condition.Value, StringComparison.OrdinalIgnoreCase),
            FilterOp.StartsWith => value.StartsWith(condition.Value, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static bool VersionMatch(string? value, FilterCondition condition) {
        long left = ProtoVersionQuality.DottedVersionKey(value);
        if (left == long.MinValue) return condition.Op == FilterOp.IsNot;

        long right = ProtoVersionQuality.DottedVersionKey(condition.Value);
        return right != long.MinValue && CompareMatch(left.CompareTo(right), condition.Op);
    }

    private static bool NumberMatch(long? value, FilterCondition condition) {
        if (value is not { } left) return condition.Op == FilterOp.IsNot;
        if (!long.TryParse(condition.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long right)) {
            return false;
        }

        return CompareMatch(left.CompareTo(right), condition.Op);
    }

    private static bool DateMatch(DateTime? value, FilterCondition condition) {
        if (value is not { } stamp) return false;
        if (!DateTime.TryParse(condition.Value, CultureInfo.InvariantCulture, DateTimeStyles.None,
                out DateTime target)) {
            return false;
        }

        int cmp = stamp.ToLocalTime().Date.CompareTo(target.Date);
        return condition.Op switch {
            FilterOp.On => cmp == 0,
            FilterOp.Before => cmp < 0,
            FilterOp.After => cmp > 0,
            FilterOp.OnOrBefore => cmp <= 0,
            FilterOp.OnOrAfter => cmp >= 0,
            _ => false
        };
    }

    private static bool BoolMatch(bool value, FilterOp op) => op switch {
        FilterOp.True => value,
        FilterOp.False => !value,
        _ => false
    };

    private static bool CompareMatch(int cmp, FilterOp op) => op switch {
        FilterOp.Is => cmp == 0,
        FilterOp.IsNot => cmp != 0,
        FilterOp.Greater => cmp > 0,
        FilterOp.Less => cmp < 0,
        FilterOp.AtLeast => cmp >= 0,
        FilterOp.AtMost => cmp <= 0,
        _ => false
    };
}
