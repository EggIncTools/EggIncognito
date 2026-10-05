using System.Reflection;
using System.Runtime.Loader;
using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices;

public sealed record DeviceExtensionCatalog(
    string Source, IReadOnlyList<string> Types, IReadOnlyList<string> Errors, IReadOnlyList<DeviceExtension> Extensions) {
    public static readonly DeviceExtensionCatalog Empty = new("", [], [], []);

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
        List<DeviceExtension> extensions = [];
        foreach (string folder in Directory.EnumerateDirectories(dir).Order(StringComparer.Ordinal)) {
            string name = Path.GetFileName(folder);
            string file = Path.Combine(folder, name + ".dll");
            if (!File.Exists(file)) continue;

            try {
                var assembly = new ExtensionLoadContext(file).LoadFromAssemblyPath(Path.GetFullPath(file));
                foreach (var type in assembly.GetExportedTypes()) {
                    if (RegisterModule(services, config, type, out string? module) && module is not null) types.Add(module);
                }

                string asmName = assembly.GetName().Name ?? name;
                string css = asmName + ".bundle.scp.css";
                bool hasCss = File.Exists(Path.Combine(folder, "wwwroot", css));
                extensions.Add(new DeviceExtension(name, Path.GetFullPath(folder), hasCss ? css : null));
            } catch (Exception ex) {
                errors.Add($"{Path.GetFileName(file)}: {ex.GetType().Name}");
            }
        }

        return new DeviceExtensionCatalog(dir, types, errors, extensions);
    }

    internal static bool RegisterModule(IServiceCollection services, IConfiguration config, Type type) =>
        RegisterModule(services, config, type, out _);

    private static bool RegisterModule(IServiceCollection services, IConfiguration config, Type type, out string? name) {
        name = null;
        if (!type.IsClass || type.IsAbstract || !typeof(IDeviceExtensionModule).IsAssignableFrom(type)) return false;

        var module = (IDeviceExtensionModule)Activator.CreateInstance(type)!;
        module.Register(services, config);
        name = module.Name;
        return true;
    }

    private sealed class ExtensionLoadContext(string mainAssemblyPath)
        : AssemblyLoadContext(Path.GetFileNameWithoutExtension(mainAssemblyPath)) {
        private static readonly Lazy<HashSet<string>> Platform = new(() =>
            (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(Path.GetFileNameWithoutExtension)
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase));

        private readonly AssemblyDependencyResolver _resolver = new(mainAssemblyPath);

        protected override Assembly? Load(AssemblyName assemblyName) {
            if (Shared(assemblyName.Name)) return null;
            return _resolver.ResolveAssemblyToPath(assemblyName) is { } path ? LoadFromAssemblyPath(path) : null;
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName) =>
            _resolver.ResolveUnmanagedDllToPath(unmanagedDllName) is { } path ? LoadUnmanagedDllFromPath(path) : IntPtr.Zero;

        private static bool Shared(string? name) =>
            name is not null
            && (Platform.Value.Contains(name)
                || Default.Assemblies.Any(a => string.Equals(a.GetName().Name, name, StringComparison.OrdinalIgnoreCase)));
    }
}
