using System.Globalization;
using System.Text.Json;
using EggIdentity.Filters;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Services.Filtering;

namespace EggIncognito.Services.Feed;

public interface INotificationEvent {
    string Kind { get; }
    string DedupKey { get; }
    string Summary { get; }
    FilterSubjectValue Subject(string field);
    IReadOnlyDictionary<string, string> Vars();
    FeedEmbed Embed();
}

public sealed record FeedEmbedField(string Name, string Value, bool Inline);

public sealed record FeedEmbed(string Title, string Url, int Color, IReadOnlyList<FeedEmbedField> Fields);

public sealed record FeedSample(string Key, string Label, INotificationEvent Event);

public sealed record FeedSampleContext(EggIncognitoDbContext Db, FeedPublisher Pages);

public sealed record NotificationKind(
    string Key,
    string Label,
    string Description,
    IReadOnlyList<FilterFieldDef> Fields,
    FilterSet Defaults,
    Func<FeedSubscription, FilterSet> Legacy,
    IReadOnlyList<string> Vars,
    Func<FeedSampleContext, CancellationToken, Task<IReadOnlyList<FeedSample>>> LoadSamples,
    Func<FeedSampleContext, CancellationToken, Task<IReadOnlyDictionary<string, IReadOnlyList<FilterOption>>>>? LoadOptions = null) {
    private static readonly FilterSet Unreadable = FeedFilter.Rows([new FilterCondition("unreadable_filter", FilterOps.Eq, "stored")]);

    public FilterCatalog Catalog => new(Key, Fields, Defaults);

    public FilterSet FilterOf(FeedSubscription sub) {
        if (string.IsNullOrWhiteSpace(sub.FilterSet)) return Legacy(sub);
        FilterSet stored;
        try {
            stored = FilterCodec.Deserialize(sub.FilterSet);
        } catch (JsonException) {
            return Unreadable;
        }

        if (string.Equals(sub.EventKind, Key, StringComparison.Ordinal)) return stored;
        var legacy = Legacy(sub);
        IReadOnlyList<FilterCondition>[] legacyOr = [
            .. legacy.And.Select((_, i) => i < legacy.Or.Count ? legacy.Or[i] : [])
        ];
        return new FilterSet([.. legacy.And, .. stored.And], [.. legacyOr, .. stored.Or]);
    }

    public FilterResult Evaluate(INotificationEvent evt, FilterSet set) =>
        FilterEvaluator.Evaluate(set, Fields, evt.Subject);

    public IReadOnlyList<FilterProblem> Validate(FilterSet set) => FilterValidator.Validate(set, Fields);

    public string Describe(IReadOnlyList<FilterFailure> failed) =>
        string.Join("; ", failed.Select(f => $"{FieldLabel(f.Condition.Field)} {f.Condition.Op} {f.Condition.Value}".Trim() + $": {f.Reason}"));

    public string FieldLabel(string key) => FilterFields.Find(Fields, key)?.Label ?? key;
}

public static class FeedFilter {
    public static FilterCondition Is(string field, string value) => new(field, FilterOps.Eq, value);

    public static FilterCondition Flag(string field, bool on) => new(field, on ? FilterOps.True : FilterOps.False, "");

    public static FilterCondition Lacks(string field, string value) => new(field, FilterOps.NotContains, value);

    public static FilterCondition Has(string field, string value) => new(field, FilterOps.Contains, value);

    public static FilterCondition AtMost(string field, double value) =>
        new(field, FilterOps.Lte, value.ToString(CultureInfo.InvariantCulture));

    public static FilterCondition Above(string field, double value) =>
        new(field, FilterOps.Gt, value.ToString(CultureInfo.InvariantCulture));

    public static FilterSet Rows(params FilterCondition[][] rows) => Rows((IEnumerable<FilterCondition[]>)rows);

    public static FilterSet Rows(IEnumerable<FilterCondition[]> rows) {
        var list = rows.Where(r => r.Length > 0).ToList();
        return new FilterSet([.. list.Select(r => r[0])], [.. list.Select(r => (IReadOnlyList<FilterCondition>)[.. r.Skip(1)])]);
    }

    public static FilterCondition[]? AnyOf(string field, IEnumerable<string> values) {
        FilterCondition[] row = [.. values.Select(v => Is(field, v))];
        return row.Length == 0 ? null : row;
    }

    public static FilterFieldDef Select(string key, string label, IEnumerable<FilterOption> options) =>
        new(key, label, FilterValueKind.Select, FilterOps.Equality) { Options = [.. options] };

    public static FilterFieldDef Searchable(string key, string label, IEnumerable<FilterOption> options) =>
        new(key, label, FilterValueKind.SearchableSelect, FilterOps.Equality) { Options = [.. options] };

    public static FilterFieldDef Bool(string key, string label) => new(key, label, FilterValueKind.Bool, FilterOps.Bool);

    public static FilterFieldDef Number(string key, string label) => new(key, label, FilterValueKind.Number, FilterOps.Comparison);

    public static FilterFieldDef Version(string key, string label) => new(key, label, FilterValueKind.Version, FilterOps.Comparison);

    public static IReadOnlyList<FilterOption> Versions(IEnumerable<string?> values) => [
        .. EgiFilterOps.ByVersion(values).Select(o => new FilterOption(o.Value, o.Label))
    ];

    public static FilterFieldDef Text(string key, string label) => new(key, label, FilterValueKind.Text, FilterOps.Equality);

    public static FilterFieldDef Set(string key, string label, IEnumerable<FilterOption> options) =>
        new(key, label, FilterValueKind.Set, FilterOps.ContainsOps) { Options = [.. options] };

    public static IEnumerable<FilterOption> Options(params string[] values) => values.Select(v => new FilterOption(v, v));

    public static FilterOption[] Sources { get; } = [new("device", "device observation"), new("carpet", "history import")];
}
