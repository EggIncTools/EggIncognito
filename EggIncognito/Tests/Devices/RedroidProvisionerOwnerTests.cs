using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Tests.Devices;

public class RedroidProvisionerOwnerTests {
    private static Dictionary<string, string> Labels(string? owner) {
        var labels = new Dictionary<string, string>(StringComparer.Ordinal) {
            [RedroidProvisioner.OwnerLabel] = "1"
        };
        if (owner is not null) labels[RedroidProvisioner.InstanceOwnerLabel] = owner;
        return labels;
    }

    [Fact]
    public void Owns_MissingLabel_CountsAsDefaultOwner() {
        Assert.True(RedroidProvisioner.Owns(Labels(null), VirtualDeviceConfig.DefaultOwner));
        Assert.False(RedroidProvisioner.Owns(Labels(null), "workstation"));
    }

    [Fact]
    public void Owns_MatchingLabel_IsOwned() =>
        Assert.True(RedroidProvisioner.Owns(Labels("workstation"), "workstation"));

    [Fact]
    public void Owns_ForeignLabel_IsNotOwned() {
        Assert.False(RedroidProvisioner.Owns(Labels("workstation"), VirtualDeviceConfig.DefaultOwner));
        Assert.False(RedroidProvisioner.Owns(Labels("other"), "workstation"));
    }

    [Fact]
    public void DefaultConfigOwner_IsTheDefaultSoProdContainersAreAdopted() =>
        Assert.Equal(VirtualDeviceConfig.DefaultOwner, new VirtualDeviceConfig().Owner);
}
