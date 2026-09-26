namespace EggIncognito.Bot;

public enum BotCommand {
    Health,
    Status,
    Verify,
    Endpoints,
    Proto,
    Unknown
}

public sealed record ProtoArgs(bool IsList, int Page, string? TypeName, string? Error) {
    public static ProtoArgs ListPage(int page) => new(true, page, null, null);
    public static ProtoArgs ForType(string name) => new(false, 0, name, null);
    public static ProtoArgs Invalid(string error) => new(false, 0, null, error);
}

public static class CommandParsing {
    public static BotCommand Resolve(string? name) => name switch {
        "health" => BotCommand.Health,
        "status" => BotCommand.Status,
        "verify" => BotCommand.Verify,
        "endpoints" => BotCommand.Endpoints,
        "proto" => BotCommand.Proto,
        _ => BotCommand.Unknown
    };

    public static ProtoArgs ParseProto(string? subcommand, IReadOnlyList<(string Name, object? Value)> options) =>
        subcommand switch {
            "list" => ProtoArgs.ListPage(
                options.FirstOrDefault(o => o.Name == "page").Value switch { long l => (int)l, int i => i, _ => 1 }),
            "type" => options.FirstOrDefault(o => o.Name == "name").Value is string name &&
                      !string.IsNullOrWhiteSpace(name)
                ? ProtoArgs.ForType(name)
                : ProtoArgs.Invalid("Missing required option `name`. Use `/proto type name:<MessageType>`."),
            null or "" => ProtoArgs.Invalid("Missing subcommand. Use `/proto list` or `/proto type`."),
            _ => ProtoArgs.Invalid($"Unknown subcommand `{subcommand}`.")
        };
}
