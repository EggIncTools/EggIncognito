using System.Linq.Expressions;
using EggIncognito.Core.Services;
using EggIncognito.Data.Models;
using Ei;

namespace EggIncognito.Services.Coverage;

public static class ConsumeDropFilter {
    public const string ConsumeAction = "consume";

    private static readonly string CommonRarity = ProtoEnumNames.RarityName(ArtifactSpec.Types.Rarity.Common);

    public static readonly Expression<Func<ArtifactConsumeObservation, bool>> Counted =
        o => o.Success && !(o.Action == ConsumeAction && o.SpecRarity != CommonRarity && o.GoldenEggs == 1);

    public static IQueryable<ArtifactConsumeObservation> CountedConsumes(this IQueryable<ArtifactConsumeObservation> rows) =>
        rows.Where(Counted).Where(o => o.Action == ConsumeAction);
}
