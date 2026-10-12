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

    public static EgiFilterSchema<StagedEntry> Schema { get; } = new() {
        Fields = [
            new("state", "State", EgiFilterValueKind.Select, EgiFilterOps.Equality,
                _ => [new(Pending, "pending"), new(Failed, "failed"), new(Analysed, "analysed")]),
            new("appVersion", "App version", EgiFilterValueKind.Version, EgiFilterOps.Comparison,
                rows => EgiFilterOps.ByVersion(rows.Select(r => r.AppVersion))),
            new("build", "Build", EgiFilterValueKind.Text, EgiFilterOps.Text,
                rows => EgiFilterOps.ByText(rows.Select(r => r.Build))),
            new("client", "Client version", EgiFilterValueKind.Version, EgiFilterOps.Comparison,
                rows => EgiFilterOps.ByVersion(rows.Select(r => r.ClientVersionText))),
            new("file", "File name", EgiFilterValueKind.Text, EgiFilterOps.Text, null),
            new("fileSha", "File SHA", EgiFilterValueKind.Text, EgiFilterOps.Text, null),
            new("sha", "Proto SHA", EgiFilterValueKind.Text, EgiFilterOps.Text, null),
            new("sizeMb", "Size (MB)", EgiFilterValueKind.Number, EgiFilterOps.Comparison, null)
        ],
        Platform = e => e.Platform,
        Quick = (e, q) => EgiFilterOps.Has(e.FileName, q)
                          || EgiFilterOps.Has(e.AppVersion, q)
                          || EgiFilterOps.Has(e.Build, q)
                          || EgiFilterOps.Has(e.ClientVersionText, q)
                          || EgiFilterOps.Prefix(e.Result?.FileSha, q)
                          || EgiFilterOps.Prefix(e.Result?.ProtoSha, q),
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
