using EggIncognito.Bot;

namespace EggIncognito.Tests;

public class CommandParsingTests {
    private static readonly IReadOnlyList<(string Name, object? Value)> NoOptions = [];

    [Theory]
    [InlineData("health", BotCommand.Health)]
    [InlineData("status", BotCommand.Status)]
    [InlineData("verify", BotCommand.Verify)]
    [InlineData("endpoints", BotCommand.Endpoints)]
    [InlineData("proto", BotCommand.Proto)]
    [InlineData("nope", BotCommand.Unknown)]
    [InlineData("", BotCommand.Unknown)]
    [InlineData(null, BotCommand.Unknown)]
    public void Resolve_MapsCommandNames(string? name, BotCommand expected) =>
        Assert.Equal(expected, CommandParsing.Resolve(name));

    [Fact]
    public void ParseProto_List_DefaultsPageTo1() {
        var args = CommandParsing.ParseProto("list", NoOptions);
        Assert.True(args.IsList);
        Assert.Equal(1, args.Page);
        Assert.Null(args.Error);
    }

    [Fact]
    public void ParseProto_List_ReadsLongPageValue() {
        var args = CommandParsing.ParseProto("list", [("page", 3L)]);
        Assert.True(args.IsList);
        Assert.Equal(3, args.Page);
    }

    [Fact]
    public void ParseProto_List_NonNumericPage_DefaultsTo1() {
        var args = CommandParsing.ParseProto("list", [("page", "abc")]);
        Assert.True(args.IsList);
        Assert.Equal(1, args.Page);
    }

    [Fact]
    public void ParseProto_Type_ReadsName() {
        var args = CommandParsing.ParseProto("type", [("name", "Backup")]);
        Assert.False(args.IsList);
        Assert.Equal("Backup", args.TypeName);
        Assert.Null(args.Error);
    }

    [Fact]
    public void ParseProto_Type_MissingName_IsInvalid() {
        var args = CommandParsing.ParseProto("type", NoOptions);
        Assert.NotNull(args.Error);
        Assert.Contains("name", args.Error);
    }

    [Fact]
    public void ParseProto_Type_NonStringName_IsInvalid() {
        var args = CommandParsing.ParseProto("type", [("name", 42L)]);
        Assert.NotNull(args.Error);
    }

    [Fact]
    public void ParseProto_NullSubcommand_IsInvalid() {
        var args = CommandParsing.ParseProto(null, NoOptions);
        Assert.NotNull(args.Error);
    }

    [Fact]
    public void ParseProto_UnknownSubcommand_IsInvalid() {
        var args = CommandParsing.ParseProto("frobnicate", NoOptions);
        Assert.NotNull(args.Error);
        Assert.Contains("frobnicate", args.Error);
    }
}
