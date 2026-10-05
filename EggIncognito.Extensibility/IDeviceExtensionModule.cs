using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EggIncognito.Core.Services.Devices;

public interface IDeviceExtensionModule {
    string Name { get; }
    void Register(IServiceCollection services, IConfiguration config);
}

public sealed record DeviceExtension(string Name, string Directory, string? Stylesheet);
