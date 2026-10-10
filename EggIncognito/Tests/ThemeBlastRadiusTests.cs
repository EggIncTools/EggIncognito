using System.Reflection;
using EggIdentity.Auth;
using EggIncognito.Controllers;
using EggIncognito.Services;
using EggIncognito.Services.Theme;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace EggIncognito.Tests;

public class ThemeBlastRadiusTests {
    [Fact]
    public async Task Resolver_ReturnsNullWithoutADatabase() {
        var resolver = BuildResolver(new FakeUser(Guid.NewGuid()).Accessor());
        Assert.Null(await resolver.ResolveAsync());
    }

    [Fact]
    public void CacheKeys_AreDistinctPerUser() {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        Assert.NotEqual(ThemeResolver.CacheKey(a), ThemeResolver.CacheKey(b));
        Assert.Equal(ThemeResolver.CacheKey(a), ThemeResolver.CacheKey(a));
    }

    [Fact]
    public void NoThemeRoute_TakesAnIdParameter() {
        foreach (var action in typeof(ThemeController)
                     .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)) {
            foreach (var attr in action.GetCustomAttributes<HttpMethodAttribute>()) {
                string template = attr.Template ?? "";
                Assert.DoesNotContain("{id", template);
                Assert.DoesNotContain("{userId", template);
                Assert.DoesNotContain("{owner", template);
            }
        }
    }

    [Fact]
    public void ControllerSource_ScopesEveryStoreReadToTheCaller() {
        string source = File.ReadAllText(Path.Combine(FindRepoRoot(),
            "EggIncognito", "Controllers", "ThemeController.cs"));
        Assert.DoesNotContain("AllAsync", source);
        int getById = CountOccurrences(source, "GetByIdAsync");
        Assert.True(getById <= 1, "GetByIdAsync may appear only in the admin policy read");
        Assert.Contains("store.GetAsync(uid,", source);
    }

    [Fact]
    public void StoredModel_IsJson_NeverSerializedCss() {
        string entity = File.ReadAllText(Path.Combine(FindRepoRoot(),
            "EggIncognito.Data", "Models", "UserTheme.cs"));
        Assert.Contains("public string Model", entity);
        Assert.DoesNotContain("Css", entity);
    }

    private static ThemeResolver BuildResolver(ICurrentUser user) {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var config = new ConfigurationBuilder().Build();
        var identitySync = new ThemeIdentitySync(new AuthState(false), new HttpContextAccessor(),
            NullLogger<ThemeIdentitySync>.Instance);
        return new ThemeResolver(user, identitySync, cache, ThemeTestSupport.Serializer(), config,
            new FakeWebHostEnvironment());
    }

    private static int CountOccurrences(string haystack, string needle) {
        int count = 0;
        int at = 0;
        while ((at = haystack.IndexOf(needle, at, StringComparison.Ordinal)) >= 0) {
            count++;
            at += needle.Length;
        }

        return count;
    }

    private static string FindRepoRoot() {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null) {
            if (dir.GetFiles("*.slnx").Length > 0 || dir.GetFiles("*.sln").Length > 0) return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("repo root not found");
    }
}
