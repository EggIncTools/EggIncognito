using System.Globalization;
using System.Text;
using EggIncognito.Core.Services.ProtoExtract;
namespace EggIncognito.Services.Protos;

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

public sealed record FilterFieldDef(
    string Key,
    string Label,
    FilterValueKind Kind,
    IReadOnlyList<FilterOpDef> Ops,
    Func<IReadOnlyList<ProtoRegistryRow>, IReadOnlyList<FilterOption>>? Options);

public sealed record FilterCondition(string Field, FilterOp Op, string Value) {
    public bool Complete =>
        !string.IsNullOrWhiteSpace(Field)
        && (Op is FilterOp.True or FilterOp.False || !string.IsNullOrWhiteSpace(Value));
}

public sealed record FilterGroup(IReadOnlyList<FilterCondition> Conditions);

public sealed record RegistryQuery(string Platform, string Quick, IReadOnlyList<FilterGroup> Groups) {
    public static readonly RegistryQuery Empty = new("", "", []);

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

public static class RegistryFilter {
    private static readonly IReadOnlyList<FilterOpDef> Equality = [
        new(FilterOp.Is, "is"),
        new(FilterOp.IsNot, "is not")
    ];

    private static readonly IReadOnlyList<FilterOpDef> Comparison = [
        new(FilterOp.Is, "is"),
        new(FilterOp.IsNot, "is not"),
        new(FilterOp.Greater, "greater than"),
        new(FilterOp.Less, "less than"),
        new(FilterOp.AtLeast, "at least"),
        new(FilterOp.AtMost, "at most")
    ];

    private static readonly IReadOnlyList<FilterOpDef> TextOps = [
        new(FilterOp.Is, "is"),
        new(FilterOp.IsNot, "is not"),
        new(FilterOp.Contains, "contains"),
        new(FilterOp.NotContains, "does not contain"),
        new(FilterOp.StartsWith, "starts with")
    ];

    private static readonly IReadOnlyList<FilterOpDef> DateOps = [
        new(FilterOp.On, "on"),
        new(FilterOp.Before, "before"),
        new(FilterOp.After, "after"),
        new(FilterOp.OnOrBefore, "on or before"),
        new(FilterOp.OnOrAfter, "on or after")
    ];

    private static readonly IReadOnlyList<FilterOpDef> BoolOps = [
        new(FilterOp.True, "True"),
        new(FilterOp.False, "False")
    ];

    public static IReadOnlyList<FilterFieldDef> Fields { get; } = [
        new("appVersion", "App version", FilterValueKind.Version, Comparison,
            rows => ByVersion(rows.Select(r => r.AppVersion))),
        new("build", "Build", FilterValueKind.Text, TextOps,
            rows => ByText(rows.Select(r => r.Build))),
        new("client", "Client version", FilterValueKind.Version, Comparison,
            rows => ByVersion(rows.Select(r => r.ClientVersion))),
        new("sha", "Proto SHA", FilterValueKind.Text, TextOps, null),
        new("source", "Source", FilterValueKind.Select, Equality,
            rows => ByText(rows.Select(r => r.Source))),
        new("package", "Package", FilterValueKind.Select, Equality,
            rows => ByText(rows.Select(r => r.Package))),
        new("detected", "Detected", FilterValueKind.Date, DateOps, null),
        new("hasText", "Stored text", FilterValueKind.Bool, BoolOps, null),
        new("badBuild", "Bad build", FilterValueKind.Bool, BoolOps, null),
        new("sortOrder", "Sort order", FilterValueKind.Number, Comparison, null)
    ];

    public static FilterFieldDef? Field(string? key) {
        if (string.IsNullOrWhiteSpace(key)) return null;
        foreach (FilterFieldDef def in Fields) {
            if (string.Equals(def.Key, key, StringComparison.Ordinal)) return def;
        }

        return null;
    }

    public static string OpLabel(string? field, FilterOp op) {
        IReadOnlyList<FilterOpDef> ops = Field(field)?.Ops ?? [];
        foreach (FilterOpDef def in ops) {
            if (def.Op == op) return def.Label;
        }

        return op.ToString().ToLowerInvariant();
    }

    public static RegistryQuery Prune(RegistryQuery query) {
        List<FilterGroup> groups = [.. query.Groups
            .Select(group => group.Conditions.Where(c => c.Complete).ToList())
            .Where(kept => kept.Count > 0)
            .Select(kept => new FilterGroup(kept))];
        return query with { Groups = groups };
    }

    public static bool Matches(ProtoRegistryRow row, RegistryQuery query) {
        if (query.Platform.Length > 0
            && !string.Equals(row.Platform, query.Platform, StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        if (!QuickMatch(row, query.Quick)) return false;
        if (query.Groups.Count == 0) return true;

        foreach (FilterGroup group in query.Groups) {
            if (GroupMatches(row, group)) return true;
        }

        return false;
    }

    private static bool GroupMatches(ProtoRegistryRow row, FilterGroup group) {
        foreach (FilterCondition condition in group.Conditions) {
            if (!condition.Complete) continue;
            if (!ConditionMatches(row, condition)) return false;
        }

        return true;
    }

    private static bool ConditionMatches(ProtoRegistryRow row, FilterCondition condition) {
        if (Field(condition.Field) is not { } def) return false;

        return def.Kind switch {
            FilterValueKind.Bool => BoolMatch(BoolValue(row, def.Key), condition.Op),
            FilterValueKind.Date => DateMatch(row.DetectedAt, condition),
            FilterValueKind.Number => NumberMatch(NumberValue(row, def.Key), condition),
            FilterValueKind.Version => VersionMatch(TextValue(row, def.Key), condition),
            _ => TextMatch(TextValue(row, def.Key), condition)
        };
    }

    private static bool QuickMatch(ProtoRegistryRow row, string quick) {
        if (quick.Length == 0) return true;
        return Has(row.AppVersion, quick)
               || Has(row.Build, quick)
               || Has(row.ClientVersion, quick)
               || (row.ProtoSha is { Length: > 0 } sha && sha.StartsWith(quick, StringComparison.OrdinalIgnoreCase));
    }

    private static bool Has(string? field, string needle) =>
        field is { Length: > 0 } && field.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static string? TextValue(ProtoRegistryRow row, string key) => key switch {
        "appVersion" => row.AppVersion,
        "build" => row.Build,
        "client" => row.ClientVersion,
        "sha" => row.ProtoSha,
        "source" => row.Source,
        "package" => row.Package,
        _ => null
    };

    private static long? NumberValue(ProtoRegistryRow row, string key) =>
        key == "sortOrder" ? row.SortOrder : null;

    private static bool BoolValue(ProtoRegistryRow row, string key) => key switch {
        "hasText" => !string.IsNullOrWhiteSpace(row.ProtoSha),
        "badBuild" => !string.IsNullOrWhiteSpace(row.BuildFlag),
        _ => false
    };

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

    private static List<FilterOption> ByVersion(IEnumerable<string?> values) => [
        .. Distinct(values)
            .OrderByDescending(ProtoVersionQuality.DottedVersionKey)
            .ThenBy(v => v, StringComparer.OrdinalIgnoreCase)
            .Select(v => new FilterOption(v, v))
    ];

    private static List<FilterOption> ByText(IEnumerable<string?> values) => [
        .. Distinct(values)
            .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
            .Select(v => new FilterOption(v, v))
    ];

    private static IEnumerable<string> Distinct(IEnumerable<string?> values) =>
        values.Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!)
            .Distinct(StringComparer.OrdinalIgnoreCase);
}
