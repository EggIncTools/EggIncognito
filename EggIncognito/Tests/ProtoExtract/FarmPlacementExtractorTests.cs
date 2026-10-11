using EggIncognito.Core.Services.ProtoExtract;

namespace EggIncognito.Tests.ProtoExtract;

public class FarmPlacementExtractorTests {
    private static bool TryLoadCapture(string file, out byte[] bin) {
        bin = [];
        string? dir = AppContext.BaseDirectory;
        while (dir is not null) {
            string candidate = Path.Combine(dir, "EggIncognito", "captures", file);
            if (File.Exists(candidate)) {
                bin = File.ReadAllBytes(candidate);
                return bin.Length > 0;
            }

            dir = Path.GetDirectoryName(dir);
        }

        return false;
    }

    [Theory]
    [InlineData("egginc-android-1.37.so")]
    [InlineData("egginc")]
    public void Extract_ReadsEveryLocator(string capture) {
        if (!TryLoadCapture(capture, out byte[] bin)) return;

        var img = BinaryImage.Load(bin);
        Assert.NotNull(img);
        var habs = HabCatalogExtractor.ExtractWith(bin, img.Symbols, img.Sections);
        var eggs = EggCatalogExtractor.ExtractAuto(bin);
        var vehicles = VehicleCatalogExtractor.ReadWith(bin, img.Symbols, img.Sections);
        Assert.True(habs.Ok, habs.Diagnostics);
        Assert.True(eggs.Ok, eggs.Diagnostics);
        Assert.True(vehicles.Ok, vehicles.Diagnostics);

        var r = FarmPlacementExtractor.Extract(bin, habs.Entries, eggs.Entries, vehicles.Entries, capture);
        Assert.True(r.Ok, r.Diagnostics);
        Assert.Empty(r.Missing);

        var d = r.Data;
        Assert.Equal(-6, d.SiloStepX);
        Assert.Equal(-5, d.SiloBaseX);
        Assert.Equal(5.5f, d.SiloZEven);
        Assert.Equal(-0.5f, d.SiloZOdd);
        Assert.Equal(5, d.Trophy.Columns);
        Assert.Equal(19, d.Trophy.Count);
        Assert.Equal(1.8f, d.Trophy.BonusScale, 1e-6f);
        Assert.Equal([10.2f, 9.2f, 10.5f, 13.2f, 18.2f, 18.5f], d.LabExtents);
        Assert.Equal([9.0f, 9.0f, 10.1f, 11.8f, 13.8f, 15.9f, 23.1f], d.DepotExtents);
        Assert.Equal(3.5f, d.LabFocusBase.X);
        Assert.Equal(3.5f, d.DepotFocusBase.X);
        Assert.Equal(10f, d.SingletonFloor);
        Assert.Equal(1.5f, d.MissionControlOffset);
        Assert.Equal(40f, d.CameraUiDivisor);
        Assert.Equal(0.1f, d.CameraUiDistanceScale, 1e-6f);
        Assert.Equal(new Core.Services.Farm.Vec3(-3.5f, 0f, 10.5f), d.CameraStaticFocus[2]);
        Assert.Equal(new Core.Services.Farm.Vec3(12f, 0f, 21f), d.CameraStaticFocus[6]);
        Assert.Equal(3.5f, d.FocusExtentPivot);
        Assert.Equal(0.5f, d.FocusExtentScale);
        Assert.Equal(3f, d.HoaFocusExtra);
        Assert.Equal(48f, d.Road.SpawnX);
        Assert.Equal(-35f, d.Road.DespawnX);
        Assert.Equal(2.5f, d.Road.FollowGap);
        Assert.Equal(1.5f, d.Road.MaxSpeedMult);
        Assert.Equal(100f, d.Road.RoundTripSeconds);
        Assert.Equal(11, d.Road.HyperloopVehicleIndex);
        Assert.Equal(12, d.Road.EmptyVehicleIndex);
    }
}
