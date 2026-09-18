using EggIncognito.Models.Contracts;
using EggIncognito.Services.Events;
using Ei;
using Google.Protobuf;

namespace EggIncognito.Services.Contracts;

public static class ContractMapper {
    public static IReadOnlyList<ContractObservation> FromPeriodicals(
        PeriodicalsResponse response, DateTimeOffset seenAt) {
        if (response.Contracts is null) return [];
        return [.. response.Contracts.Contracts
            .Select(c => FromProto(c, ContractSources.Device, seenAt))
            .OfType<ContractObservation>()];
    }

    public static IReadOnlyList<ContractObservation> FromCarpet(IReadOnlyList<CarpetContract> rows) {
        var list = new List<ContractObservation>();
        foreach (var row in rows) {
            Contract c;
            try {
                c = Contract.Parser.ParseFrom(Convert.FromBase64String(row.Proto));
            } catch (FormatException) {
                continue;
            } catch (InvalidProtocolBufferException) {
                continue;
            }
            var obs = FromProto(c, ContractSources.Carpet, default);
            if (obs is null) continue;
            list.Add(obs with { SeenAt = obs.Start });
        }
        return list;
    }

    public static ContractObservation? FromProto(Contract c, string source, DateTimeOffset seenAt) {
        if (string.IsNullOrEmpty(c.Identifier)) return null;
        if (c.Debug) return null;
        if (c.Identifier == "first-contract") return null;
        if (c.StartTime <= 0 && c.ExpirationTime <= 0) return null;

        double start = c.StartTime > 0 ? c.StartTime : c.ExpirationTime - c.LengthSeconds;
        double end = c.ExpirationTime > 0 ? c.ExpirationTime : start + c.LengthSeconds;

        return new ContractObservation(
            c.Identifier,
            c.Name,
            (int)c.Egg,
            string.IsNullOrEmpty(c.CustomEggId) ? null : c.CustomEggId,
            string.IsNullOrEmpty(c.SeasonId) ? null : c.SeasonId,
            UnixSeconds.ToTime(start),
            UnixSeconds.ToTime(end),
            c.LengthSeconds,
            c.Leggacy,
            c.CcOnly,
            ProphecyEggCount(c),
            c.CoopAllowed,
            (int)c.MaxCoopSize,
            c.MinutesPerToken,
            c.ToByteArray(),
            source,
            seenAt);
    }

    private static int ProphecyEggCount(Contract c) {
        var goals = c.GradeSpecs.Count > 0
            ? c.GradeSpecs.OrderByDescending(g => (int)g.Grade).First().Goals
            : c.Goals;
        return (int)goals.Where(g => g.RewardType == RewardType.EggsOfProphecy).Sum(g => g.RewardAmount);
    }
}
