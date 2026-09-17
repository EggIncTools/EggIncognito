using EggIncognito.Core.Services.Farm;
using EggIncognito.Core.Services.ProtoExtract;

namespace EggIncognito.Tests.ProtoExtract;

public class FarmPlacementExtractorTests {
    private static readonly float[] ExpectedLabExtents = [10.2f, 9.2f, 10.5f, 13.2f, 18.2f, 18.5f];
    private static readonly float[] ExpectedDepotExtents = [9.0f, 9.0f, 10.1f, 11.8f, 13.8f, 15.9f, 23.1f];
    private static readonly float[] ExpectedSpacing = [3.2f, 4.75f, 7.2f, 1.1f, 2.2f, 1.0f];

    private static readonly float[] ExpectedCameraDistance =
        [1f, 1.3f, 3f, 2f, 0.7f, 0.7f, 1f, 1f, 1f, 1f, 1f, 1f, 1f];

    private static readonly float[] ExpectedCameraHeight =
        [5f, 4f, 0.5f, 0.3f, 1f, 1f, 5f, 5f, 5f, 5f, 5f, 5f, 1.5f];

    private static FarmPlacementExtractor.Result Extract(byte[] bin) {
        var habs = HabCatalogExtractor.Extract(bin);
        var eggs = EggCatalogExtractor.ExtractAuto(bin);
        var vehicles = VehicleCatalogExtractor.Read(bin);
        return FarmPlacementExtractor.Extract(bin, habs.Entries, eggs.Entries, vehicles.Entries, "test");
    }

    private static void Near(double expected, float actual, int precision = 4) =>
        Assert.Equal(expected, actual, precision);

    private static void Near(Vec3 expected, Vec3 actual) {
        Near(expected.X, actual.X);
        Near(expected.Y, actual.Y);
        Near(expected.Z, actual.Z);
    }

    [Fact]
    public void Extract_ReadsEveryLocator() {
        if (!BinaryFixture.TryLoad(out var bin)) return;
        var r = Extract(bin);
        Assert.Empty(r.Missing);
        Assert.True(r.Ok, r.Diagnostics);
        Assert.True(r.Data.IsComplete);
    }

    [Fact]
    public void Extract_ReadsHabRow() {
        if (!BinaryFixture.TryLoad(out var bin)) return;
        var d = Extract(bin).Data;
        Near(-12.0, d.HabAnchorX);
        Near(0.0, d.HabRowY);
        Near(-10.5, d.HabRowZ);
        Near(3.0, d.HabGap);
    }

    [Fact]
    public void Extract_ReadsSiloRow() {
        if (!BinaryFixture.TryLoad(out var bin)) return;
        var d = Extract(bin).Data;
        Near(-6.0, d.SiloStepX);
        Near(-5.0, d.SiloBaseX);
        Near(0.0, d.SiloY);
        Near(5.5, d.SiloZEven);
        Near(-0.5, d.SiloZOdd);
    }

    [Fact]
    public void Extract_ReadsTrophyGrid() {
        if (!BinaryFixture.TryLoad(out var bin)) return;
        var t = Extract(bin).Data.Trophy;
        Near(new Vec3(-5.45f, 0f, 11.254f), t.CasePos);
        Near(0.692, t.ColumnStepX);
        Near(-6.831, t.OriginX);
        Near(0.699, t.RowStepY);
        Near(0.143, t.OriginY);
        Near(-0.3, t.RowStepZ);
        Near(11.4539995, t.OriginZ);
        Assert.Equal(5, t.Columns);
        Assert.Equal(19, t.Count);
        Near(1.8, t.BonusScale);
        Near(new Vec3(-4.0629997f, 2.2399998f, 10.554f), t.BonusPos);
    }

    [Fact]
    public void Extract_ReadsBuildingExtentTables() {
        if (!BinaryFixture.TryLoad(out var bin)) return;
        var d = Extract(bin).Data;
        Assert.Equal(ExpectedLabExtents.Length, d.LabExtents.Count);
        for (int i = 0; i < ExpectedLabExtents.Length; i++) Near(ExpectedLabExtents[i], d.LabExtents[i]);
        Assert.Equal(ExpectedDepotExtents.Length, d.DepotExtents.Count);
        for (int i = 0; i < ExpectedDepotExtents.Length; i++) Near(ExpectedDepotExtents[i], d.DepotExtents[i]);
    }

    [Fact]
    public void Extract_ReadsSingletons() {
        if (!BinaryFixture.TryLoad(out var bin)) return;
        var d = Extract(bin).Data;

        Assert.Equal(3, d.MissionControlPose.Count);
        Near(new Vec3(2.8f, 0f, 3.7f), d.MissionControlPose[0]);
        Near(new Vec3(4.5f, 0f, 6.0f), d.MissionControlPose[1]);
        Near(new Vec3(5.5f, 0f, 6.0f), d.MissionControlPose[2]);

        Assert.Equal(ExpectedSpacing.Length, d.FuelTankSpacing.Count);
        for (int i = 0; i < ExpectedSpacing.Length; i++) Near(ExpectedSpacing[i], d.FuelTankSpacing[i]);

        Near(10.0, d.SingletonFloor);
        Near(2.0, d.HoaHomeOffset);
        Near(1.7, d.HoaAltOffset);
        Near(-3.5, d.HoaZ);
        Near(1.5, d.MissionControlOffset);
        Near(1.5, d.FuelTankBaseOffset);
        Near(1.5, d.FuelTankLockedExtra);
        Near(3.7, d.FuelTankZUnlocked);
        Near(4.2, d.FuelTankZLocked);
    }

    [Fact]
    public void Extract_ReadsCameraTables() {
        if (!BinaryFixture.TryLoad(out var bin)) return;
        var d = Extract(bin).Data;
        Assert.Equal(ExpectedCameraDistance.Length, d.CameraDistance.Count);
        for (int i = 0; i < ExpectedCameraDistance.Length; i++) Near(ExpectedCameraDistance[i], d.CameraDistance[i]);
        Assert.Equal(ExpectedCameraHeight.Length, d.CameraHeight.Count);
        for (int i = 0; i < ExpectedCameraHeight.Length; i++) Near(ExpectedCameraHeight[i], d.CameraHeight[i]);

        Near(40.0, d.CameraUiDivisor);
        Near(0.5, d.CameraUiHeightScale);
        Near(0.1, d.CameraUiDistanceScale);
    }

    [Fact]
    public void Extract_ReadsCameraFocusPoints() {
        if (!BinaryFixture.TryLoad(out var bin)) return;
        var d = Extract(bin).Data;

        Assert.Equal(13, d.CameraStaticFocus.Count);
        Near(new Vec3(-3.5f, 0f, 10.5f), d.CameraStaticFocus[2]);
        Near(new Vec3(-5.5f, 0f, 11.0f), d.CameraStaticFocus[3]);
        Near(new Vec3(0f, 0f, 0f), d.CameraStaticFocus[4]);
        Near(new Vec3(-3.0f, 0f, 0f), d.CameraStaticFocus[5]);
        Near(new Vec3(12.0f, 0f, 21.0f), d.CameraStaticFocus[6]);

        Near(new Vec3(0f, 0f, -2.0f), d.HabFocusOffset);
        Near(new Vec3(3.5f, 0f, -1.0f), d.LabFocusBase);
        Near(new Vec3(3.5f, 0f, 9.5f), d.DepotFocusBase);
        Near(new Vec3(3.5f, 0f, 2.5999999f), d.HatcheryFocusBase);
        Near(new Vec3(1.0f, 0f, -1.0f), d.FuelTankFocusOffset);
        Near(3.5, d.FocusExtentPivot);
        Near(0.5, d.FocusExtentScale);
        Near(3.0, d.HoaFocusExtra);
    }

    [Fact]
    public void Extract_ReadsRoad() {
        if (!BinaryFixture.TryLoad(out var bin)) return;
        var road = Extract(bin).Data.Road;
        Near(48.0, road.SpawnX);
        Near(13.33, road.RoadZ, 3);
        Near(0.0, road.RoadY);
        Near(7.1, road.DepotStopX);
        Near(-35.0, road.DespawnX);
        Near(2.5, road.FollowGap);
        Near(1.5, road.MaxSpeedMult);
        Near(100.0, road.RoundTripSeconds);
        Assert.Equal(11, road.HyperloopVehicleIndex);
        Assert.Equal(12, road.EmptyVehicleIndex);
    }

    [Fact]
    public void Extract_CarriesCatalogColumns() {
        if (!BinaryFixture.TryLoad(out var bin)) return;
        var d = Extract(bin).Data;

        Assert.Equal(19, d.Habs.Count);
        Assert.Equal("COOP", d.Habs[0].Name);
        Assert.Equal(3.0, d.HabWidth(0), 5);
        Assert.Equal(9.5, d.HabWidth(18), 5);
        Assert.Equal(2.2, d.HabDepth(0), 5);
        Assert.Equal(4.0, d.HabDepth(18), 5);

        Assert.NotEmpty(d.Eggs);
        Assert.NotEmpty(d.Vehicles);
        Assert.Contains(d.Vehicles, v => v.Length > 0);
    }

    [Fact]
    public void Extract_HabRowOrdersSlotsTwoZeroOneThree() {
        if (!BinaryFixture.TryLoad(out var bin)) return;
        var d = Extract(bin).Data;
        var state = new FarmState { Habs = [0, 0, 0, 0] };

        float x0 = FarmPlacementEngine.HabPosition(state, d, 0).X;
        float x1 = FarmPlacementEngine.HabPosition(state, d, 1).X;
        float x2 = FarmPlacementEngine.HabPosition(state, d, 2).X;
        float x3 = FarmPlacementEngine.HabPosition(state, d, 3).X;

        Assert.True(x2 < x0);
        Assert.True(x0 < x1);
        Assert.True(x1 < x3);
        Near(-12.0, x0);
        Near(-6.0, x1);
        Near(-18.0, x2);
        Near(0.0, x3);
    }

    private static byte[] WithFunctionCleared(byte[] bin, string needle) {
        var copy = (byte[])bin.Clone();
        Assert.True(MachoSymbols.TryFindFunc(MachoSymbols.Read(copy), [needle], out var fn));
        Assert.True(MachoSections.TryVaToFileOffset(MachoSections.Read(copy), fn.Start, out int at, out _));
        Array.Clear(copy, at, (int)(fn.End - fn.Start));
        return copy;
    }

    [Fact]
    public void Extract_OneWreckedFunction_ReportsOnlyItsOwnFields() {
        if (!BinaryFixture.TryLoad(out var bin)) return;
        var r = Extract(WithFunctionCleared(bin, "FarmScene10updateSilo"));
        Assert.False(r.Ok);
        Assert.All(r.Missing, m => Assert.StartsWith("silo", m, StringComparison.Ordinal));
        Assert.Contains("unreadable fields", r.Diagnostics);
    }

    [Fact]
    public void Extract_WidespreadLocatorFailure_SaysBinaryNotRecognised() {
        if (!BinaryFixture.TryLoad(out var bin)) return;
        var wrecked = WithFunctionCleared(bin, "FarmScene10updateSilo");
        wrecked = WithFunctionCleared(wrecked, "FarmScene16updateTrophyCase");
        wrecked = WithFunctionCleared(wrecked, "VehicleManager6update");

        var r = Extract(wrecked);
        Assert.False(r.Ok);
        Assert.True(r.Missing.Count >= 8, $"expected a wrong-build-sized miss list, got {r.Missing.Count}");
        Assert.Contains("not recognised", r.Diagnostics);
        Assert.DoesNotContain("unreadable fields", r.Diagnostics);
    }

    [Fact]
    public void Extract_NoPlacementSymbols_BailsBeforeReadingAnyLocator() {
        if (!BinaryFixture.TryLoad(out var bin)) return;
        var r = FarmPlacementExtractor.Extract(bin[..0x40_000], [], [], [], "truncated");
        Assert.False(r.Ok);
        Assert.Empty(r.Missing);
        Assert.Contains("not found by symbol", r.Diagnostics);
    }
}
