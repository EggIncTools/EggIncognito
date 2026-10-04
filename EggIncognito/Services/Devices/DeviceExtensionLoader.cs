using System.Reflection;
using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices;

public sealed record DeviceExtensionCatalog(
    string Source, IReadOnlyList<string> Types, IReadOnlyList<string> Errors) {
    public static readonly DeviceExtensionCatalog Empty = new("", [], []);

    public int Loaded => Types.Count;
}

public static class DeviceExtensionLoader {
    public const string PathKey = "Devices:Extensions:Path";
    public const string DefaultDirectoryName = "extensions";

    public static DeviceExtensionCatalog Load(
        IServiceCollection services, IConfiguration config, string contentRoot) {
        string dir = config[PathKey] is { Length: > 0 } configured
            ? configured
            : Path.Combine(contentRoot, DefaultDirectoryName);
        if (!Directory.Exists(dir)) return DeviceExtensionCatalog.Empty;

        List<string> types = [];
        List<string> errors = [];
        var loaded = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetName().Name ?? "")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string file in Directory.EnumerateFiles(dir, "*.dll").Order(StringComparer.Ordinal)) {
            if (loaded.Contains(Path.GetFileNameWithoutExtension(file))) continue;
            try {
                foreach (var type in Assembly.LoadFrom(file).GetExportedTypes()) {
                    if (Register(services, type)) types.Add(type.Name);
                }
            } catch (Exception ex) {
                errors.Add($"{Path.GetFileName(file)}: {ex.GetType().Name}");
            }
        }

        return new DeviceExtensionCatalog(dir, types, errors);
    }

    private static bool Register(IServiceCollection services, Type type) {
        if (!type.IsClass || type.IsAbstract) return false;

        bool cookbook = typeof(IDeviceCookbook).IsAssignableFrom(type);
        bool responses = typeof(IDeviceResponseSources).IsAssignableFrom(type);
        bool transforms = typeof(IDeviceResponseTransforms).IsAssignableFrom(type);
        bool panel = typeof(IDevicePanel).IsAssignableFrom(type);
        bool inventory = typeof(ICoverageInventory).IsAssignableFrom(type);
        bool hosted = typeof(IHostedService).IsAssignableFrom(type);
        bool service = type.GetCustomAttributes(typeof(DeviceExtensionServiceAttribute), false).Length > 0;
        if (!cookbook && !responses && !transforms && !panel && !inventory && !hosted && !service) return false;

        services.AddSingleton(type, sp => ActivatorUtilities.CreateInstance(sp, type));
        if (cookbook)
            services.AddSingleton<IDeviceCookbook>(sp => (IDeviceCookbook)sp.GetRequiredService(type));
        if (responses)
            services.AddSingleton<IDeviceResponseSources>(sp => (IDeviceResponseSources)sp.GetRequiredService(type));
        if (transforms)
            services.AddSingleton<IDeviceResponseTransforms>(sp => (IDeviceResponseTransforms)sp.GetRequiredService(type));
        if (panel)
            services.AddSingleton<IDevicePanel>(sp => (IDevicePanel)sp.GetRequiredService(type));
        if (inventory)
            services.AddSingleton<ICoverageInventory>(sp => (ICoverageInventory)sp.GetRequiredService(type));
        if (hosted)
            services.AddSingleton<IHostedService>(sp => (IHostedService)sp.GetRequiredService(type));
        if (service) {
            foreach (var contract in type.GetInterfaces().Where(i => i.Assembly == type.Assembly))
                services.AddSingleton(contract, sp => sp.GetRequiredService(type));
        }

        return true;
    }
}
