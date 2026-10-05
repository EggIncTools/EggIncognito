using EggIncognito.Core.Services.Devices;
using EggIncognito.Services.Devices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EggIncognito.Tests.Extensibility;

public sealed class DeviceExtensionLoaderTests : IDisposable {
    private readonly string _root = Path.Combine(Path.GetTempPath(), "egi-ext-" + Guid.NewGuid().ToString("N"));

    public void Dispose() {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static IConfiguration Config(string path) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [DeviceExtensionLoader.PathKey] = path })
            .Build();

    [Fact]
    public void MissingDirectory_LoadsNothing() {
        var catalog = DeviceExtensionLoader.Load(new ServiceCollection(), Config(_root), _root);
        Assert.Same(DeviceExtensionCatalog.Empty, catalog);
    }

    [Fact]
    public void EmptyDirectory_LoadsNothing() {
        Directory.CreateDirectory(_root);
        var catalog = DeviceExtensionLoader.Load(new ServiceCollection(), Config(_root), _root);
        Assert.Equal(0, catalog.Loaded);
        Assert.Empty(catalog.Errors);
        Assert.Empty(catalog.Extensions);
    }

    [Fact]
    public void NonAssemblyDll_RecordsOneErrorWithoutThrowing() {
        string folder = Path.Combine(_root, "x");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "x.dll"), [1, 2, 3, 4]);

        var catalog = DeviceExtensionLoader.Load(new ServiceCollection(), Config(_root), _root);

        Assert.Single(catalog.Errors);
        Assert.Equal(0, catalog.Loaded);
    }

    [Fact]
    public void RegisterModule_RunsTheModulesRegistration() {
        var services = new ServiceCollection();
        Assert.True(DeviceExtensionLoader.RegisterModule(services, Config(_root), typeof(SampleModule)));
        Assert.IsType<SampleService>(services.BuildServiceProvider().GetRequiredService<SampleService>());
    }

    [Fact]
    public void RegisterModule_IgnoresNonModules() =>
        Assert.False(DeviceExtensionLoader.RegisterModule(new ServiceCollection(), Config(_root), typeof(SampleService)));

    public sealed class SampleService;

    public sealed class SampleModule : IDeviceExtensionModule {
        public string Name => "sample";

        public void Register(IServiceCollection services, IConfiguration config) => services.AddSingleton<SampleService>();
    }
}
