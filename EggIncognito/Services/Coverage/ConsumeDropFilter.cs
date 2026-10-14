using System.Linq.Expressions;
using EggIncognito.Data.Models;

namespace EggIncognito.Services.Coverage;

public static class ConsumeDropFilter {
    public const string ConsumeAction = "consume";

    public static readonly Expression<Func<ArtifactConsumeObservation, bool>> Counted =
        o => o.Success && !o.ServerDenied;

    public static IQueryable<ArtifactConsumeObservation> CountedConsumes(this IQueryable<ArtifactConsumeObservation> rows) =>
        rows.Where(Counted).Where(o => o.Action == ConsumeAction);
}
