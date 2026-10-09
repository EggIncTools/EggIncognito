using EggIncognito.Components.Devices;
using EggIncognito.Components.Shared;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Models;
using EggIncognito.Models.Coverage;
using EggIncognito.Models.Observations;
using EggIncognito.Services.Contracts;
using EggIncognito.Services.DataApi;
using EggIncognito.Services.Devices;

namespace EggIncognito.Tests.Extensibility;

public class ContractAssemblyTests {
    private const string ContractAssembly = "EggIncognito.Extensibility";

    private static readonly Type[] ContractTypes = [
        typeof(IGameDataDocuments), typeof(IDeviceCaptureHubs), typeof(IDeviceResponseSources),
        typeof(IDeviceResponseTransforms), typeof(IDeviceAppLauncher), typeof(IDeviceFleet), typeof(DeviceEntry),
        typeof(ArtifactByproductRow), typeof(ArtifactRewardRow), typeof(IDevicePanel), typeof(DevicePanel),
        typeof(DevicePanelShelves), typeof(DevicePanelContext), typeof(CoverageCellContext), typeof(ICoverageAnnotations),
        typeof(ICoverageInventory), typeof(IDeviceExtensionModule), typeof(DeviceExtension), typeof(IDeviceRinfo),
        typeof(IArtifactObservationSink), typeof(IArtifactObservationQuery), typeof(ICoverageMap), typeof(IContractReleases),
        typeof(ConsumeCoverageMap), typeof(CoverageFamily), typeof(CoverageTier), typeof(CoverageCell), typeof(CoverageSelection),
        typeof(CoverageTargetRequest), typeof(CoverageTargetRow), typeof(CoverageFamilyInfo), typeof(CoverageCatalogCell), typeof(CoverageSample),
        typeof(PanelShelf), typeof(ToolBar), typeof(BarTrack), typeof(PanelHead), typeof(Caret), typeof(DeviceStatusLine)
    ];

    [Fact]
    public void ContractTypes_LiveInTheContractAssembly() {
        var stray = ContractTypes.Where(t => t.Assembly.GetName().Name != ContractAssembly).Select(t => t.Name).ToList();
        Assert.True(stray.Count == 0, "contract types outside " + ContractAssembly + ": " + string.Join(", ", stray));
    }

    [Fact]
    public void ContractAssembly_ReferencesNeitherTheAppNorTheDataLayer() {
        var names = typeof(IDevicePanel).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToList();
        Assert.DoesNotContain("EggIncognito", names);
        Assert.DoesNotContain("EggIncognito.Data", names);
    }

    [Fact]
    public void RuntimeOrigin_MatchesTheDataLayerConstant() =>
        Assert.Equal(DeviceOrigins.Runtime, DeviceEntry.RuntimeOrigin);
}
