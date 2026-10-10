using EggIncognito.Capture;
using EggIncognito.Data.Services;
using EggIncognito.Services.Contributions;

namespace EggIncognito.Startup;

public static class ContributionServices {
    public static void AddContributionServices(this WebApplicationBuilder builder, BootFlags boot) {
        var options = ContributionOptions.Bind(builder.Configuration);
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<ICaptureContributionKinds, CaptureContributionKinds>();

        if (!boot.DbEnabled) return;

        builder.Services.AddScoped<ContributionStore>();
        if (!options.Enabled) return;

        builder.Services.AddSingleton<ContributionRecorder>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<ContributionRecorder>());
    }
}
