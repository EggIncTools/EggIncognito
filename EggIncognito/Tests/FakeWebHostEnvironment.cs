using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

namespace EggIncognito.Tests;

public sealed class FakeWebHostEnvironment(string environmentName = "Development") : IWebHostEnvironment {
    public string WebRootPath { get; set; } = "";
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    public string ApplicationName { get; set; } = "EggIncognito.Tests";
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public string ContentRootPath { get; set; } = "";
    public string EnvironmentName { get; set; } = environmentName;
}
