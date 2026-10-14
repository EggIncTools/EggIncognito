using EggIncognito.Core.Services;
using EggIncognito.Services.DataApi;
using Ei;
using Google.Protobuf;

namespace EggIncognito.Services.Feed;

public sealed record ConfigChangeSummary(
    IReadOnlyList<string> Changed,
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Removed) {
    public bool Any => Changed.Count > 0 || Added.Count > 0 || Removed.Count > 0;
}

public static class ConfigAspects {
    public static ConfigChangeSummary? Diff(string feedId, string? previousJson, string nextJson) {
        if (previousJson is null) return null;
        try {
            return feedId switch {
                ConfigFeeds.Periodicals => Periodicals(
                    Parse<PeriodicalsResponse>(previousJson), Parse<PeriodicalsResponse>(nextJson)),
                ConfigFeeds.Config => Config(
                    Parse<ConfigResponse>(previousJson), Parse<ConfigResponse>(nextJson)),
                ConfigFeeds.Afx => Afx(
                    Parse<ArtifactsConfigurationResponse>(previousJson),
                    Parse<ArtifactsConfigurationResponse>(nextJson)),
                ConfigFeeds.Seasons => Seasons(
                    Parse<ContractSeasonInfos>(previousJson), Parse<ContractSeasonInfos>(nextJson)),
                _ => null
            };
        } catch (InvalidJsonException) {
            return null;
        } catch (InvalidProtocolBufferException) {
            return null;
        }
    }

    private static T Parse<T>(string json) where T : IMessage<T>, new() {
        var parsed = JsonParser.Default.Parse<T>(ProtoJson.StripVolatile(json));
        ProtoVolatileScrub.Scrub(parsed);
        return parsed;
    }

    private static ConfigChangeSummary Periodicals(PeriodicalsResponse prev, PeriodicalsResponse next) {
        PeriodicalsSanitizer.ScrubPlayerScope(prev);
        PeriodicalsSanitizer.ScrubPlayerScope(next);

        var changed = new List<string>();
        Mark(changed, "sales", prev.Sales, next.Sales);
        Mark(changed, "events", prev.Events, next.Events);
        Mark(changed, "liveConfig", prev.LiveConfig, next.LiveConfig);
        Mark(changed, "mail", prev.MailBag, next.MailBag);

        var prevContracts = prev.Contracts?.Clone();
        var nextContracts = next.Contracts?.Clone();
        prevContracts?.CustomEggs.Clear();
        nextContracts?.CustomEggs.Clear();
        Mark(changed, "contracts", prevContracts, nextContracts);

        var prevEggs = CustomEggs(prev);
        var nextEggs = CustomEggs(next);
        if (!prevEggs.SequenceEqual(nextEggs)) changed.Add("colleggtibles");

        var before = Ids("event", EventIds(prev))
            .Concat(Ids("contract", ContractIds(prev)))
            .Concat(Ids("colleggtible", prevEggs.Select(e => e.Identifier)));
        var after = Ids("event", EventIds(next))
            .Concat(Ids("contract", ContractIds(next)))
            .Concat(Ids("colleggtible", nextEggs.Select(e => e.Identifier)));
        return Summary(changed, before, after);
    }

    private static ConfigChangeSummary Config(ConfigResponse prev, ConfigResponse next) {
        var changed = new List<string>();
        Mark(changed, "liveConfig", prev.LiveConfig, next.LiveConfig);
        Mark(changed, "mail", prev.MailBag, next.MailBag);
        Mark(changed, "admin", prev.Admin, next.Admin);

        var prevDlc = prev.DlcCatalog;
        var nextDlc = next.DlcCatalog;
        MarkList(changed, "dlcItems", prevDlc?.Items, nextDlc?.Items);
        MarkList(changed, "shells", prevDlc?.Shells, nextDlc?.Shells);
        MarkList(changed, "shellSets", prevDlc?.ShellSets, nextDlc?.ShellSets);
        MarkList(changed, "decorators", prevDlc?.Decorators, nextDlc?.Decorators);
        MarkList(changed, "shellObjects", prevDlc?.ShellObjects, nextDlc?.ShellObjects);
        MarkList(changed, "shellGroups", prevDlc?.ShellGroups, nextDlc?.ShellGroups);
        MarkList(changed, "fontPacks", prevDlc?.FontPacks, nextDlc?.FontPacks);

        return Summary(changed, DlcIds(prevDlc), DlcIds(nextDlc));
    }

    private static ConfigChangeSummary Afx(
        ArtifactsConfigurationResponse prev, ArtifactsConfigurationResponse next) {
        var changed = new List<string>();
        MarkList(changed, "artifacts", prev.ArtifactParameters, next.ArtifactParameters);
        MarkList(changed, "missions", prev.MissionParameters, next.MissionParameters);
        MarkList(changed, "crafting", prev.CraftingLevelInfos, next.CraftingLevelInfos);

        return Summary(changed,
            Ids("artifact", prev.ArtifactParameters.Select(a => SpecKey(a.Spec))),
            Ids("artifact", next.ArtifactParameters.Select(a => SpecKey(a.Spec))));
    }

    private static ConfigChangeSummary Seasons(ContractSeasonInfos prev, ContractSeasonInfos next) {
        var changed = new List<string>();
        MarkList(changed, "seasons", prev.Infos, next.Infos);
        return Summary(changed,
            Ids("season", prev.Infos.Select(s => s.Id)),
            Ids("season", next.Infos.Select(s => s.Id)));
    }

    private static string SpecKey(ArtifactSpec? spec) =>
        spec is null ? "" : $"{spec.Name}/{spec.Level}/{spec.Rarity}";

    private static IEnumerable<string> DlcIds(DLCCatalog? dlc) {
        if (dlc is null) return [];
        return Ids("shell", dlc.Shells.Select(s => s.Identifier))
            .Concat(Ids("shellSet", dlc.ShellSets.Select(s => s.Identifier)))
            .Concat(Ids("decorator", dlc.Decorators.Select(s => s.Identifier)))
            .Concat(Ids("shellObject", dlc.ShellObjects.Select(s => s.Identifier)))
            .Concat(Ids("shellGroup", dlc.ShellGroups.Select(s => s.Identifier)))
            .Concat(Ids("dlcItem", dlc.Items.Select(i => i.Name)));
    }

    private static IEnumerable<string> Ids(string prefix, IEnumerable<string> raw) =>
        raw.Where(id => !string.IsNullOrEmpty(id)).Select(id => $"{prefix}:{id}");

    private static void Mark(List<string> changed, string aspect, IMessage? prev, IMessage? next) {
        if (!Equals(prev, next)) changed.Add(aspect);
    }

    private static void MarkList<T>(List<string> changed, string aspect,
        IEnumerable<T>? prev, IEnumerable<T>? next) {
        if (!(prev ?? []).SequenceEqual(next ?? [])) changed.Add(aspect);
    }

    private static ConfigChangeSummary Summary(
        List<string> changed, IEnumerable<string> before, IEnumerable<string> after) {
        var prevIds = before.ToHashSet(StringComparer.Ordinal);
        var nextIds = after.ToHashSet(StringComparer.Ordinal);
        return new ConfigChangeSummary(
            changed,
            [.. nextIds.Except(prevIds, StringComparer.Ordinal).Order(StringComparer.Ordinal)],
            [.. prevIds.Except(nextIds, StringComparer.Ordinal).Order(StringComparer.Ordinal)]);
    }

    private static IReadOnlyList<ColleggtibleDef> CustomEggs(PeriodicalsResponse r) =>
        ColleggtibleExtractor.FromPeriodicals(r).Eggs;

    private static IEnumerable<string> EventIds(PeriodicalsResponse r) =>
        (r.Events?.Events ?? []).Select(e => e.Identifier);

    private static IEnumerable<string> ContractIds(PeriodicalsResponse r) =>
        (r.Contracts?.Contracts ?? []).Select(c => c.Identifier);
}
