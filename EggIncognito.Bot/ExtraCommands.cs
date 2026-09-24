using Discord;
using EggIdentity.Bot;
using EggIncognito.Core;
using EggIncognito.Core.Services;
using Microsoft.Extensions.Logging;

namespace EggIncognito.Bot;

public static class ExtraCommands {
    private static readonly ApplicationIntegrationType[] Integrations =
        [ApplicationIntegrationType.GuildInstall, ApplicationIntegrationType.UserInstall];

    private static readonly InteractionContextType[] Contexts =
        [InteractionContextType.Guild, InteractionContextType.BotDm, InteractionContextType.PrivateChannel];

    private static SlashCommandBuilder Base(string name, string desc) =>
        new SlashCommandBuilder()
            .WithName(name).WithDescription(desc)
            .WithIntegrationTypes(Integrations)
            .WithContextTypes(Contexts);

    public static EggIdentity.Bot.BotCommand HealthCommand(DateTimeOffset startedAt, ILogger? logger = null) =>
        new(Base("health", "Liveness check - pong + uptime.").Build(), "health",
            async ctx => await RunAsync(ctx, () => BotEmbeds.Health(DateTimeOffset.UtcNow - startedAt), logger));

    public static EggIdentity.Bot.BotCommand StatusCommand(IStatusProvider status, ILogger? logger = null) =>
        new(Base("status", "Show the running server's live status (mode, capture, DB, signing, uptime).").Build(),
            "status",
            async ctx => await RunAsync(ctx, () => BotEmbeds.Status(status.Build()), logger));

    public static EggIdentity.Bot.BotCommand EndpointsCommand(IStatusProvider status, ILogger? logger = null) =>
        new(Base("endpoints", "Show endpoint coverage (ok / empty / missing).").Build(), "endpoints",
            async ctx => await RunAsync(ctx, () => BotEmbeds.Endpoints(status.Build()), logger));

    private static async Task RunAsync(SocketSlashCommandContext ctx, Func<Embed> build, ILogger? logger) {
        await ctx.Command.DeferAsync(true);
        try {
            await ctx.Command.FollowupAsync(embed: build(), ephemeral: true);
        } catch (Exception ex) {
            logger?.LogError(ex, "bot: slash command failed");
            await ctx.Command.FollowupAsync(
                embed: BotEmbeds.Error("The command failed on the server. Details are in the server log."),
                ephemeral: true);
        }
    }

    public static EggIdentity.Bot.BotCommand ProtoCommand(IProtoReflection proto, ILogger? logger = null) =>
        new(
            Base("proto", "Look up Egg, Inc. proto message types.")
                .AddOption(new SlashCommandOptionBuilder()
                    .WithName("list").WithDescription("List proto message types.")
                    .WithType(ApplicationCommandOptionType.SubCommand)
                    .AddOption("page", ApplicationCommandOptionType.Integer, "Page number.", false))
                .AddOption(new SlashCommandOptionBuilder()
                    .WithName("type").WithDescription("Show one message type's fields.")
                    .WithType(ApplicationCommandOptionType.SubCommand)
                    .AddOption(new SlashCommandOptionBuilder()
                        .WithName("name").WithDescription("Message type name.")
                        .WithType(ApplicationCommandOptionType.String)
                        .WithRequired(true)
                        .WithAutocomplete(true)))
                .Build(),
            "proto",
            ctx => HandleProtoAsync(ctx, proto, logger),
            ctx => HandleProtoAutocompleteAsync(ctx, proto, logger));

    private static async Task HandleProtoAsync(SocketSlashCommandContext ctx, IProtoReflection proto,
        ILogger? logger) {
        var cmd = ctx.Command;
        await cmd.DeferAsync(true);
        try {
            var sub = cmd.Data.Options.FirstOrDefault();
            List<(string Name, object? Value)> opts = sub?.Options?
                .Select(o => (o.Name, (object?)o.Value))
                .ToList() ?? [];
            var args = CommandParsing.ParseProto(sub?.Name, opts);
            if (args.Error is not null) {
                await cmd.FollowupAsync(embed: BotEmbeds.Error(args.Error), ephemeral: true);
                return;
            }

            if (args.IsList) {
                (var slice, int p, int pages) = ProtoQuery.Page(proto.AllMessageTypeNames(), args.Page);
                var embed = new EmbedBuilder()
                    .WithTitle($"Proto message types (page {p}/{pages})")
                    .WithColor(new Color(0xEF7559))
                    .WithDescription(slice.Count == 0
                        ? "(none)"
                        : Strings.Truncate(string.Join("\n", slice), ProtoQuery.MaxDescription,
                            ProtoQuery.TruncatedMarker))
                    .Build();
                await cmd.FollowupAsync(embed: embed, ephemeral: true);
                return;
            }

            var schema = proto.Schema(args.TypeName!);
            if (schema is null) {
                await cmd.FollowupAsync(embed: BotEmbeds.Error($"Unknown proto type `{args.TypeName}`."),
                    ephemeral: true);
                return;
            }

            var detail = new EmbedBuilder()
                .WithTitle($"Ei.{schema.Name}")
                .WithColor(new Color(0xEF7559))
                .WithDescription("```\n" + Strings.Truncate(ProtoQuery.TypeLines(schema), ProtoQuery.MaxDescription,
                    ProtoQuery.TruncatedMarker) + "\n```")
                .Build();
            await cmd.FollowupAsync(embed: detail, ephemeral: true);
        } catch (Exception ex) {
            logger?.LogError(ex, "bot: /proto failed");
            await cmd.FollowupAsync(
                embed: BotEmbeds.Error("The command failed on the server. Details are in the server log."),
                ephemeral: true);
        }
    }

    private static async Task HandleProtoAutocompleteAsync(SocketAutocompleteContext ctx, IProtoReflection proto,
        ILogger? logger) {
        var ac = ctx.Interaction;
        try {
            if (ac.Data.CommandName != "proto") {
                await ac.RespondAsync(Array.Empty<AutocompleteResult>());
                return;
            }

            string current = ac.Data.Current.Value?.ToString() ?? "";
            var hits = ProtoQuery.Autocomplete(proto.AllMessageTypeNames(), current)
                .Select(n => new AutocompleteResult(n, n));
            await ac.RespondAsync(hits);
        } catch (Exception ex) {
            logger?.LogWarning(ex, "bot: /proto autocomplete failed");
            try {
                await ac.RespondAsync(Array.Empty<AutocompleteResult>());
            } catch (Exception fallback) {
                logger?.LogDebug(fallback, "bot: /proto autocomplete empty fallback also failed");
            }
        }
    }
}
