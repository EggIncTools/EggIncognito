using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Services;
using EggIncognito.Services.Devices;

namespace EggIncognito.Startup;

public static class VirtualDeviceServices {
    public static void AddVirtualDeviceServices(this WebApplicationBuilder builder, BootFlags boot) {
        var config = VirtualDeviceConfig.Bind(builder.Configuration);
        var transport = boot.DeviceTransportConfig;
        bool delegated = transport.Mode == DeviceTransportMode.Remote;
        DockerEndpoint endpoint = delegated
            ? new DockerEndpoint.Bridge(transport.RemoteBaseUrl ?? "", transport.ApiKey)
            : new DockerEndpoint.Unix(config.DockerSocket);
        builder.Services.AddSingleton(config);
        builder.Services.AddSingleton(_ => new DockerEngineClient(endpoint));
        builder.Services.AddSingleton(_ => new DockerSocketProxy(config.DockerSocket));
        if (delegated) builder.Services.AddSingleton<IDeviceProvisioner, BridgeProvisioner>();
        else builder.Services.AddSingleton<IDeviceProvisioner, RedroidProvisioner>();
        builder.Services.AddSingleton<IDeviceProvisioners, DeviceProvisioners>();
        builder.Services.AddSingleton<VirtualDeviceLifecycle>();

        if (!boot.DbEnabled || boot.FakeDevices) return;
        builder.Services.AddScoped<ProvisionedInstanceStore>();
        if (!delegated) {
            builder.Services.AddHostedService(sp => sp.GetRequiredService<VirtualDeviceLifecycle>());
            builder.Services.AddHostedService<DockerEventWatcher>();
        }

        builder.Services.AddHttpClient(ImageBuilder.HttpClientName, c => {
            c.Timeout = TimeSpan.FromMinutes(10);
            c.MaxResponseContentBufferSize = 512L * 1024 * 1024;
            c.DefaultRequestHeaders.UserAgent.ParseAdd("EggIncognito-ImageBuild/1.0");
        });
        builder.Services.AddSingleton<IImageBuildExecutor, LocalImageBuildExecutor>();
        builder.Services.AddSingleton<ImageBuildRunner>();
        builder.Services.AddScoped<ImageBuilder>();
    }
}
