using EggIncognito.Services.Filtering;

namespace EggIncognito.Models.Filtering;

public sealed class DraftRow {
    public string Field { get; set; } = "";
    public EgiFilterOp Op { get; set; }
    public bool OpSet { get; set; }
    public string Value { get; set; } = "";
}
