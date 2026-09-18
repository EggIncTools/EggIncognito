using EggIdentity.Consent;

namespace EggIncognito.Startup;

public static class ConsentServices {
    public static void AddConsentServices(this WebApplicationBuilder builder, BootFlags boot) {
        builder.Services.AddEggIdentityConsent(new ConsentOptions {
            CookieDomain = boot.Session?.CookieDomain
        });
    }
}
