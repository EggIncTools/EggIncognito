using EggIncognito.Bot;
using EggIncognito.Core.Services;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace EggIncognito.Tests;

public class ExtraCommandsTests {
    [Fact]
    public void HealthCommand_HasExpectedName() {
        var cmd = ExtraCommands.HealthCommand(DateTimeOffset.UtcNow);
        Assert.Equal("health", cmd.Name);
    }

    [Fact]
    public void StatusCommand_HasExpectedName() {
        var cmd = ExtraCommands.StatusCommand(new FakeStatusProvider());
        Assert.Equal("status", cmd.Name);
    }

    [Fact]
    public void EndpointsCommand_HasExpectedName() {
        var cmd = ExtraCommands.EndpointsCommand(new FakeStatusProvider());
        Assert.Equal("endpoints", cmd.Name);
    }

    [Fact]
    public void ProtoCommand_HasExpectedNameAndAutocompleteHandler() {
        var cmd = ExtraCommands.ProtoCommand(new FakeProtoReflection());
        Assert.Equal("proto", cmd.Name);
        Assert.NotNull(cmd.AutocompleteHandler);
    }

    internal sealed class FakeStatusProvider : IStatusProvider {
        public StatusSnapshot Build() => new(
            "Local", true, true, "Idle",
            false, 0, 0, 0,
            false, false, TimeSpan.Zero,
            new BuildInfo("0.0.0", "unknown", "unknown", "unknown", "https://example.com"),
            0, 0, 0);
    }

    internal sealed class FakeProtoReflection : IProtoReflection {
        public MessageDescriptor? FindMessage(string typeName) => null;
        public MessageParser? FindParser(string typeName) => null;
        public SchemaMessage? Schema(string typeName) => null;
        public IReadOnlyList<string> AllMessageTypeNames() => Array.Empty<string>();
        public IReadOnlyList<MessageTypeInfo> AllMessageTypes() => [];
    }
}
