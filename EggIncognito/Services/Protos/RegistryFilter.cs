using EggIncognito.Services.Filtering;

namespace EggIncognito.Services.Protos;

public static class RegistryFilter {
    public static EgiFilterSchema<ProtoRegistryRow> Schema { get; } = new() {
        Fields = [
            new("appVersion", "App version", EgiFilterValueKind.Version, EgiFilterOps.Comparison,
                rows => EgiFilterOps.ByVersion(rows.Select(r => r.AppVersion))),
            new("build", "Build", EgiFilterValueKind.Text, EgiFilterOps.Text,
                rows => EgiFilterOps.ByText(rows.Select(r => r.Build))),
            new("client", "Client version", EgiFilterValueKind.Version, EgiFilterOps.Comparison,
                rows => EgiFilterOps.ByVersion(rows.Select(r => r.ClientVersion))),
            new("sha", "Proto SHA", EgiFilterValueKind.Text, EgiFilterOps.Text, null),
            new("source", "Source", EgiFilterValueKind.Select, EgiFilterOps.Equality,
                rows => EgiFilterOps.ByText(rows.Select(r => r.Source))),
            new("package", "Package", EgiFilterValueKind.Select, EgiFilterOps.Equality,
                rows => EgiFilterOps.ByText(rows.Select(r => r.Package))),
            new("detected", "Detected", EgiFilterValueKind.Date, EgiFilterOps.Date, null),
            new("hasText", "Stored text", EgiFilterValueKind.Bool, EgiFilterOps.Bool, null),
            new("badBuild", "Bad build", EgiFilterValueKind.Bool, EgiFilterOps.Bool, null),
            new("sortOrder", "Sort order", EgiFilterValueKind.Number, EgiFilterOps.Comparison, null),
            new("archived", "Archive sourced", EgiFilterValueKind.Bool, EgiFilterOps.Bool, null)
        ],
        Platform = r => r.Platform,
        Quick = (r, q) => EgiFilterOps.Has(r.AppVersion, q)
                          || EgiFilterOps.Has(r.Build, q)
                          || EgiFilterOps.Has(r.ClientVersion, q)
                          || EgiFilterOps.Prefix(r.ProtoSha, q),
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

    public static IReadOnlyList<EgiFilterFieldDef<ProtoRegistryRow>> Fields => Schema.Fields;

    public static IReadOnlyList<EgiFilterFieldDef<ProtoRegistryRow>> FieldsFor(bool admin) => Schema.FieldsFor(admin);

    public static EgiFilterFieldDef<ProtoRegistryRow>? Field(string? key) => Schema.Field(key);

    public static string OpLabel(string? field, EgiFilterOp op) => Schema.OpLabel(field, op);

    public static ListQuery Prune(ListQuery query) => EgiFilterOps.Prune(query);

    public static bool Matches(ProtoRegistryRow row, ListQuery query) => Schema.Matches(row, query);
}
