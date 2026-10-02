namespace EggIncognito.Models.Filtering;

public sealed record FilterListSection<TItem>(string Key, string Label, IReadOnlyList<TItem> Items, bool Collapsed = false, string? Tone = null);
