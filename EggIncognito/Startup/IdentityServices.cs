using System.Net.Http.Headers;
using EggIdentity.Client;
using EggIdentity.Metrics;
using EggIncognito.Services;
using EggIncognito.Services.Auth;
using EggIncognito.Services.Metrics;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EggIncognito.Startup;

public static class IdentityServices {
    public static void AddIdentityServices(this WebApplicationBuilder builder, BootFlags boot) {
        if (boot.IdentityApiEnabled) {
            builder.Services.AddHttpClient<IdentityApiClient>(c => {
                c.BaseAddress = new Uri(boot.IdentityApiUrl!);
                c.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", boot.IdentityApiSecret);
            });
        }

        if (boot.Session is not null) builder.Services.AddSingleton(boot.Session);
        builder.AddEggIdentityAuthIfConfigured(boot.IdentityApiEnabled, boot.Session);
        if (boot.LocalIdentity is not null) builder.AddLocalIdentityAuth(boot.LocalIdentity);

        builder.Services.AddSingleton(boot.AuthState);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddEggIdentityRequestMetrics(o => {
            o.PathPrefix = "/api";
            o.InternalMarkerHeader = SelfCallClient.InternalMarkerHeader;
            o.HostedBehindProxy = boot.HostedBehindProxy;
        });
        builder.Services.AddSingleton<ITrafficSource, TrafficSource>();
        builder.Services.TryAddScoped<ICurrentUser, CurrentUser>();
    }
}
