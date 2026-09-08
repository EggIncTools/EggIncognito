using EggIncognito.Core.Services;

namespace EggIncognito.Tests;

public class ProtoReflectionEnumTests {
    [Fact]
    public void AllMessageTypeNames_IsNonEmpty_SortedAndDistinct() {
        var names = new ProtoReflection().AllMessageTypeNames();
        Assert.NotEmpty(names);
        Assert.Equal(names.Count, names.Distinct().Count());
        var sorted = names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        Assert.Equal(sorted, names);
    }

    [Fact]
    public void AllMessageTypeNames_IncludesKnownTypes() {
        var names = new ProtoReflection().AllMessageTypeNames();

        Assert.Contains("Contract", names);
        Assert.Contains("EggIncFirstContactResponse", names);
    }

    [Fact]
    public void AllMessageTypeNames_AreResolvable() {
        var refl = new ProtoReflection();

        foreach (string name in refl.AllMessageTypeNames().Take(25))
            Assert.NotNull(refl.Schema(name));
    }

    [Fact]
    public void AllMessageTypes_IncludesNestedTypeWithParent() {
        var types = new ProtoReflection().AllMessageTypes();

        Assert.Contains(types, t => t is { Name: "Backup.Game", Parent: "Backup" });
        Assert.Contains(types, t => t is { Name: "Backup", Parent: null });
    }

    [Fact]
    public void AllMessageTypes_IsSortedAndDistinct() {
        var types = new ProtoReflection().AllMessageTypes();
        var names = types.Select(t => t.Name).ToList();

        Assert.Equal(names.Count, names.Distinct().Count());
        var sorted = names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        Assert.Equal(sorted, names);
    }

    [Fact]
    public void AllMessageTypes_ContainsEveryTopLevelName() {
        var refl = new ProtoReflection();
        var byName = refl.AllMessageTypes().ToDictionary(t => t.Name, StringComparer.Ordinal);

        foreach (string name in refl.AllMessageTypeNames()) {
            Assert.True(byName.TryGetValue(name, out var info));
            Assert.Null(info!.Parent);
        }
    }

    [Fact]
    public void NestedType_ResolvesAndIsReferencedByParentField() {
        var refl = new ProtoReflection();

        Assert.NotNull(refl.Schema("Backup.Game"));
        var backup = refl.Schema("Backup");
        Assert.NotNull(backup);
        Assert.Contains(backup.Fields, f => f.Name == "game" && f.MessageType == "Backup.Game");
    }
}
