using EggIncognito.Models.Calendar;

namespace EggIncognito.Models.Contracts;

public sealed record ContractCalendarItem(
    string Key,
    double Start,
    double End,
    ContractReleaseDto? Release,
    ContractSlotPrediction? Slot) : ICalendarSpan {
    public bool Ghost => Slot is not null;

    public ContractSlotKind Kind => Slot?.Kind ?? KindOf(Release!);

    public static ContractSlotKind KindOf(ContractReleaseDto c) {
        if (!c.Leggacy) return ContractSlotKind.NewContract;
        if (c.ProphecyEggs <= 0) return ContractSlotKind.Leggacy;
        return c.UltraOnly ? ContractSlotKind.PeLeggacyUltra : ContractSlotKind.PeLeggacy;
    }

    public static ContractCalendarItem Of(ContractReleaseDto c) => new(
        FormattableString.Invariant($"c:{c.ContractId}@{c.StartTimestamp:0}"), c.StartTimestamp, c.EndTimestamp, c, null);

    public static ContractCalendarItem Of(ContractSlotPrediction s) => new(
        FormattableString.Invariant($"pc:{s.Kind}@{s.SlotTime:0}"), s.SlotTime, s.SlotTime + s.LengthSeconds, null, s);
}
