using EggIncognito.Models.Contracts;
using EggIncognito.Services.Contracts;

namespace EggIncognito.Tests.Contracts;

public class ContractIngestorTests {
    private static readonly DateTimeOffset Start = new(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);

    private static ContractObservation Obs(
        string contractId = "coop-tourney-1", string name = "Coop Tourney", string source = ContractSources.Device,
        DateTimeOffset? start = null, DateTimeOffset? end = null, DateTimeOffset? seenAt = null,
        byte[]? proto = null) {
        var s = start ?? Start;
        return new ContractObservation(
            contractId, name, 5, null, null, s, end ?? s.AddDays(3), 259200,
            false, false, 0, true, 4, 60,
            proto ?? [1, 2, 3], source, seenAt ?? s);
    }

    [Fact]
    public void Create_NewRow_FirstAndLastSeenEqualSeenAt() {
        var row = ContractIngestor.Create(Obs(seenAt: Start.AddHours(2)));
        Assert.Equal(Start.AddHours(2), row.FirstSeenAt);
        Assert.Equal(Start.AddHours(2), row.LastSeenAt);
        Assert.Equal(ContractSources.Device, row.Source);
    }

    [Fact]
    public void SameRelease_WithinWindow() {
        var row = ContractIngestor.Create(Obs());
        Assert.True(ContractIngestor.SameRelease(row, Obs(start: Start.AddHours(47))));
        Assert.False(ContractIngestor.SameRelease(row, Obs(start: Start.AddHours(49))));
        Assert.False(ContractIngestor.SameRelease(row, Obs(contractId: "other", start: Start)));
    }

    [Fact]
    public void Apply_CarpetOverDeviceOnlyMovesLastSeenAt() {
        var row = ContractIngestor.Create(Obs(seenAt: Start));
        bool changed = ContractIngestor.Apply(
            row, Obs(source: ContractSources.Carpet, name: "Renamed", seenAt: Start.AddHours(5)));
        Assert.True(changed);
        Assert.Equal("Coop Tourney", row.Name);
        Assert.Equal(ContractSources.Device, row.Source);
        Assert.Equal(Start.AddHours(5), row.LastSeenAt);
    }

    [Fact]
    public void Apply_CarpetOverDeviceWithOlderSeenAt_NoChange() {
        var row = ContractIngestor.Create(Obs(seenAt: Start.AddHours(5)));
        bool changed = ContractIngestor.Apply(
            row, Obs(source: ContractSources.Carpet, name: "Renamed", seenAt: Start));
        Assert.False(changed);
        Assert.Equal(Start.AddHours(5), row.LastSeenAt);
    }

    [Fact]
    public void Apply_DeviceOverCarpetOverwritesFieldsAndFlipsSource() {
        var row = ContractIngestor.Create(Obs(source: ContractSources.Carpet, name: "Old Name"));
        bool changed = ContractIngestor.Apply(
            row, Obs(source: ContractSources.Device, name: "New Name", proto: [9, 9, 9]));
        Assert.True(changed);
        Assert.Equal(ContractSources.Device, row.Source);
        Assert.Equal("New Name", row.Name);
        Assert.Equal("\t\t\t"u8.ToArray(), row.Proto);
    }

    [Fact]
    public void Apply_IdenticalObservation_NoChange() {
        var obs = Obs();
        var row = ContractIngestor.Create(obs);
        Assert.False(ContractIngestor.Apply(row, obs));
    }
}
