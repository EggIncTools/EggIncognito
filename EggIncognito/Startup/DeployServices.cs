using EggIdentity.Deploy;
using EggIdentity.Deploy.AdminUi;

namespace EggIncognito.Startup;

public static class DeployServices {
    public static void AddDeployServices(this WebApplicationBuilder builder, BootFlags boot) {
        if (!boot.IdentityApiEnabled) return;

        builder.Services.AddEggIdentityDeploy(
            new DeployOptions(boot.IdentityApiUrl!, "eggincognito", boot.IdentityApiSecret!));
        builder.Services.AddEggIdentityDeployToasts();
    }
}
