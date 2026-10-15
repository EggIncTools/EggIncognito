using System.Text;
using EggIncognito.Core.Services.ProtoExtract;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace EggIncognito.Tests;

public class CarvedProtoTests {
    private static string EiBase64() {
        var fdp = new FileDescriptorProto { Name = "ei.proto", Syntax = "proto2", Package = "ei" };
        fdp.MessageType.Add(new DescriptorProto { Name = "Foo" });
        return Convert.ToBase64String(fdp.ToByteArray());
    }

    [Fact]
    public void FromCarvedBase64_ValidDescriptor_EmitsProtoAndPassesClientVersion() {
        var r = DescriptorProtoCarver.FromCarvedBase64(EiBase64(), null, 70123);
        Assert.True(r.Ok);
        Assert.Contains("message Foo", r.Proto);
        Assert.Equal(70123, r.ClientVersion);
        Assert.False(string.IsNullOrEmpty(r.ProtoSha));
    }

    [Fact]
    public void EmitProto_HouseFormat_RelativeNames_EnumsAfterMessages() {
        var fdp = new FileDescriptorProto { Name = "ei.proto", Syntax = "proto2", Package = "ei" };
        var backup = new DescriptorProto { Name = "Backup" };
        backup.NestedType.Add(new DescriptorProto {
            Name = "Settings",
            Field = { new FieldDescriptorProto {
                Name = "sfx", Number = 1,
                Label = FieldDescriptorProto.Types.Label.Optional,
                Type = FieldDescriptorProto.Types.Type.Bool,
            } },
        });
        backup.Field.Add(new FieldDescriptorProto {
            Name = "settings",
            Number = 4,
            Label = FieldDescriptorProto.Types.Label.Optional,
            Type = FieldDescriptorProto.Types.Type.Message,
            TypeName = ".ei.Backup.Settings",
        });
        backup.Field.Add(new FieldDescriptorProto {
            Name = "platform",
            Number = 5,
            Label = FieldDescriptorProto.Types.Label.Optional,
            Type = FieldDescriptorProto.Types.Type.Enum,
            TypeName = ".ei.Platform",
        });
        fdp.MessageType.Add(backup);
        fdp.EnumType.Add(new EnumDescriptorProto {
            Name = "Platform",
            Value = { new EnumValueDescriptorProto { Name = "UNKNOWN_PLATFORM", Number = 0 } },
        });

        const string expected =
            "syntax = \"proto2\";\n" +
            "\n" +
            "package ei;\n" +
            "\n" +
            "message Backup {\n" +
            "    message Settings {\n" +
            "        optional bool sfx = 1;\n" +
            "    }\n" +
            "\n" +
            "    optional Settings settings = 4;\n" +
            "    optional Platform platform = 5;\n" +
            "}\n" +
            "\n" +
            "enum Platform {\n" +
            "    UNKNOWN_PLATFORM = 0;\n" +
            "}\n";

        Assert.Equal(expected, DescriptorProtoCarver.EmitProto(fdp));
    }

    [Fact]
    public void FromCarvedBase64_InvalidBase64_Fails() {
        var r = DescriptorProtoCarver.FromCarvedBase64("not base64!!!", null, null);
        Assert.False(r.Ok);
    }

    [Fact]
    public void Manifest_TryParse_RoundTrips() {
        string json = $"{{\"v\":1,\"fileSha\":\"deadbeef\",\"clientVersion\":70123,\"ei\":\"{EiBase64()}\",\"common\":null}}";
        var m = CarvedManifest.TryParse(Encoding.UTF8.GetBytes(json));
        Assert.NotNull(m);
        Assert.Equal("deadbeef", m!.FileSha);
        Assert.Equal(70123, m.ClientVersion);
        Assert.Equal(EiBase64(), m.Ei);
        Assert.Null(m.AppVersion);
        Assert.Null(m.Build);
    }

    [Fact]
    public void Manifest_TryParse_BindsArchiveVersionMeta() {
        string json =
            $"{{\"v\":1,\"fileSha\":\"x\",\"clientVersion\":1,\"ei\":\"{EiBase64()}\",\"common\":null,\"appVersion\":\"1.35.8\",\"build\":\"111780\"}}";
        var m = CarvedManifest.TryParse(Encoding.UTF8.GetBytes(json));
        Assert.NotNull(m);
        Assert.Equal("1.35.8", m!.AppVersion);
        Assert.Equal("111780", m.Build);
    }

    [Fact]
    public void Manifest_LooksLikeManifest_RejectsBinary() {
        Assert.True(CarvedManifest.LooksLikeManifest("{"u8.ToArray()));
        Assert.False(CarvedManifest.LooksLikeManifest([0x7f, 0x45, 0x4c, 0x46]));
        Assert.False(CarvedManifest.LooksLikeManifest([0x50, 0x4b, 0x03, 0x04]));
    }

    [Fact]
    public void Manifest_TryParse_MissingEi_ReturnsNull() {
        var m = CarvedManifest.TryParse(Encoding.UTF8.GetBytes("{\"v\":1,\"fileSha\":\"x\"}"));
        Assert.Null(m);
    }
}
