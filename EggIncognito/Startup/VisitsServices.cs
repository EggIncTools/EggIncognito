using EggIdentity.Visits;

namespace EggIncognito.Startup;

public static class VisitsServices {
    public static void AddVisitsServices(this WebApplicationBuilder builder, BootFlags boot) {
        if (!boot.DbEnabled) return;

        builder.Services.AddEggIdentityVisits(new VisitsOptions("eggincognito") {
            HostedBehindProxy = boot.HostedBehindProxy
        });
    }
}
