using EggIncognito.Services.Filtering;

namespace EggIncognito.Services.Protos;

public static class SessionFilter {
    public const string Pending = "pending";
    public const string Failed = "failed";
    public const string Analysed = "analysed";

    public static string StateOf(StagedEntry e) {
        if (e.Status == "error" || e is { IsDone: true, IsAnalyzed: false }) return Failed;
        return e.IsAnalyzed ? Analysed : Pending;
    }

    public static FilterSchema<StagedEntry> Schema { get; } = new() {
        Fields = [
            new("state", "State", FilterValueKind.Select, FilterOps.Equality,
                _ => [new(Pending, "pending"), new(Failed, "failed"), new(Analysed, "analysed")]),
            new("appVersion", "App version", FilterValueKind.Version, FilterOps.Comparison,
                rows => FilterOps.ByVersion(rows.Select(r => r.AppVersion))),
            new("build", "Build", FilterValueKind.Text, FilterOps.Text,
                rows => FilterOps.ByText(rows.Select(r => r.Build))),
            new("client", "Client version", FilterValueKind.Version, FilterOps.Comparison,
                rows => FilterOps.ByVersion(rows.Select(r => r.ClientVersionText))),
            new("file", "File name", FilterValueKind.Text, FilterOps.Text, null),
            new("fileSha", "File SHA", FilterValueKind.Text, FilterOps.Text, null),
            new("sha", "Proto SHA", FilterValueKind.Text, FilterOps.Text, null),
            new("sizeMb", "Size (MB)", FilterValueKind.Number, FilterOps.Comparison, null)
        ],
        Platform = e => e.Platform,
        Quick = (e, q) => FilterOps.Has(e.FileName, q)
                          || FilterOps.Has(e.AppVersion, q)
                          || FilterOps.Has(e.Build, q)
                          || FilterOps.Has(e.ClientVersionText, q)
                          || FilterOps.Prefix(e.Result?.FileSha, q)
                          || FilterOps.Prefix(e.Result?.ProtoSha, q),
        Text = (e, key) => key switch {
            "state" => StateOf(e),
            "appVersion" => e.AppVersion,
            "build" => e.Build,
            "client" => e.ClientVersionText,
            "file" => e.FileName,
            "fileSha" => e.Result?.FileSha,
            "sha" => e.Result?.ProtoSha,
            _ => null
        },
        Number = (e, key) => key == "sizeMb" ? e.Size / (1024 * 1024) : null
    };
}
