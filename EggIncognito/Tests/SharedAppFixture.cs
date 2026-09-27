using EggIncognito.Services.Auth;
using EggIncognito.Services.Devices.Fake;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace EggIncognito.Tests;

public abstract class EgiTestFactory : WebApplicationFactory<Program> {
    protected virtual void Configure(IWebHostBuilder builder) {
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder) {
        builder.UseSetting("NoBrowser", "true");
        builder.UseSetting("RateLimiting:Enabled", "false");
        builder.UseSetting(FakeDeviceGate.EnabledKey, "false");
        builder.UseSetting(LocalIdentityGate.EnabledKey, "false");
        Configure(builder);
        builder.ConfigureTestServices(services =>
            services.AddHttpClient(string.Empty).ConfigurePrimaryHttpMessageHandler(sp =>
                sp.GetRequiredService<IServer>() is TestServer ts ? ts.CreateHandler() : new HttpClientHandler()));
    }
}

public sealed class SharedAppFactory : EgiTestFactory {
}

[CollectionDefinition(Name)]
public sealed class SharedAppCollection : ICollectionFixture<SharedAppFactory> {
    public const string Name = "shared-app";
}

public sealed class HostedAppFactory : EgiTestFactory {
    protected override void Configure(IWebHostBuilder builder) => builder.UseSetting("AppMode", "Hosted");
}

[CollectionDefinition(Name)]
public sealed class HostedAppCollection : ICollectionFixture<HostedAppFactory> {
    public const string Name = "hosted-app";
}

public sealed class HostedCaptureAppFactory : EgiTestFactory {
    protected override void Configure(IWebHostBuilder builder) => builder
        .UseSetting("AppMode", "Hosted")
        .UseSetting("HostedCaptureEnabled", "true")
        .UseSetting("Capture:FrontDoorPort", "0")
        .UseSetting("Capture:AddressSecret", "test-secret");
}

[CollectionDefinition(Name)]
public sealed class HostedCaptureAppCollection : ICollectionFixture<HostedCaptureAppFactory> {
    public const string Name = "hosted-capture";
}

[CollectionDefinition(Name)]
public sealed class EggIncApiCollection : ICollectionFixture<EggIncApiFactory> {
    public const string Name = "egginc-api";
}
