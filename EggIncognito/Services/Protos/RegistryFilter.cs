using EggIncognito.Services.Filtering;

namespace EggIncognito.Services.Protos;

public static class RegistryFilter {
    public static FilterSchema<ProtoRegistryRow> Schema { get; } = new() {
        Fields = [
            new("appVersion", "App version", FilterValueKind.Version, FilterOps.Comparison,
                rows => FilterOps.ByVersion(rows.Select(r => r.AppVersion))),
            new("build", "Build", FilterValueKind.Text, FilterOps.Text,
                rows => FilterOps.ByText(rows.Select(r => r.Build))),
            new("client", "Client version", FilterValueKind.Version, FilterOps.Comparison,
                rows => FilterOps.ByVersion(rows.Select(r => r.ClientVersion))),
            new("sha", "Proto SHA", FilterValueKind.Text, FilterOps.Text, null),
            new("source", "Source", FilterValueKind.Select, FilterOps.Equality,
                rows => FilterOps.ByText(rows.Select(r => r.Source))),
            new("package", "Package", FilterValueKind.Select, FilterOps.Equality,
                rows => FilterOps.ByText(rows.Select(r => r.Package))),
            new("detected", "Detected", FilterValueKind.Date, FilterOps.Date, null),
            new("hasText", "Stored text", FilterValueKind.Bool, FilterOps.Bool, null),
            new("badBuild", "Bad build", FilterValueKind.Bool, FilterOps.Bool, null),
            new("sortOrder", "Sort order", FilterValueKind.Number, FilterOps.Comparison, null),
            new("archived", "Archive sourced", FilterValueKind.Bool, FilterOps.Bool, null)
        ],
        Platform = r => r.Platform,
        Quick = (r, q) => FilterOps.Has(r.AppVersion, q)
                          || FilterOps.Has(r.Build, q)
                          || FilterOps.Has(r.ClientVersion, q)
                          || FilterOps.Prefix(r.ProtoSha, q),
        Text = (r, key) => key switch {
            "appVersion" => r.AppVersion,
            "build" => r.Build,
            "client" => r.ClientVersion,
            "sha" => r.ProtoSha,
            "source" => r.Source,
            "package" => r.Package,
            _ => null
        },
        Number = (r, key) => key == "sortOrder" ? r.SortOrder : null,
        Flag = (r, key) => key switch {
            "hasText" => !string.IsNullOrWhiteSpace(r.ProtoSha),
            "badBuild" => !string.IsNullOrWhiteSpace(r.BuildFlag),
            "archived" => r.ArchiveSourced == true,
            _ => false
        },
        Date = (r, key) => key == "detected" ? r.DetectedAt : null
    };

    public static IReadOnlyList<FilterFieldDef<ProtoRegistryRow>> Fields => Schema.Fields;

    public static IReadOnlyList<FilterFieldDef<ProtoRegistryRow>> FieldsFor(bool admin) => Schema.FieldsFor(admin);

    public static FilterFieldDef<ProtoRegistryRow>? Field(string? key) => Schema.Field(key);

    public static string OpLabel(string? field, FilterOp op) => Schema.OpLabel(field, op);

    public static ListQuery Prune(ListQuery query) => FilterOps.Prune(query);

    public static bool Matches(ProtoRegistryRow row, ListQuery query) => Schema.Matches(row, query);
}
