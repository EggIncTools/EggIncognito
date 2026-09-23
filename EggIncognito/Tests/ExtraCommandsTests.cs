using EggIncognito.Bot;
using EggIncognito.Core.Services;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace EggIncognito.Tests;

public class ExtraCommandsTests {
    [Fact]
    public void ProtoCommand_HasExpectedNameAndAutocompleteHandler() {
        var cmd = ExtraCommands.ProtoCommand(new FakeProtoReflection());
        Assert.Equal("proto", cmd.Name);
        Assert.NotNull(cmd.AutocompleteHandler);
    }

    internal sealed class FakeProtoReflection : IProtoReflection {
        public MessageDescriptor? FindMessage(string typeName) => null;
        public MessageParser? FindParser(string typeName) => null;
        public SchemaMessage? Schema(string typeName) => null;
        public IReadOnlyList<string> AllMessageTypeNames() => Array.Empty<string>();
        public IReadOnlyList<MessageTypeInfo> AllMessageTypes() => [];
    }
}
