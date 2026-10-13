using EggIncognito.Core.Services.ProtoExtract;
using Google.Protobuf.Reflection;

namespace EggIncognito.Tests;

public class ProtoTextCompilerTests {
    private const string MultiLineFieldSample = """
        syntax = "proto2";
        package ei;
        message Misc {
            optional uint64 last_prestige_alert_soul_eggs_DEPRECATED = 10
                [default = 45];
            optional int32
                split_decl = 11;
        }
        """;

    private const string ShadowedReferenceSample = """
        syntax = "proto2";
        package ei;
        message Backup {
            message MissionInfo {
                optional string id = 1;
            }
            message Artifacts {
                optional MissionInfo.Spaceship last_fueled_ship = 9;
            }
        }
        message MissionInfo {
            enum Spaceship {
                CHICKEN_ONE = 0;
            }
        }
        """;

    [Fact]
    public void MultiLine_Declarations_Are_Joined() {
        var fdp = ProtoTextCompiler.Compile(MultiLineFieldSample);
        var m = Assert.Single(fdp.MessageType);
        Assert.Equal(2, m.Field.Count);
        Assert.Equal("45", m.Field[0].DefaultValue);
        Assert.Equal("split_decl", m.Field[1].Name);
    }

    [Fact]
    public void Shadowed_Reference_Resolves_Outward_When_Commit_Fails() {
        var fdp = ProtoTextCompiler.Compile(ShadowedReferenceSample);
        var artifacts = fdp.MessageType[0].NestedType.First(n => n.Name == "Artifacts");
        Assert.Equal(".ei.MissionInfo.Spaceship", artifacts.Field[0].TypeName);
    }

    [Fact]
    public void Shadowed_Reference_Prefers_Committed_Scope_When_It_Resolves() {
        const string p = """
            syntax = "proto2";
            package ei;
            message Backup {
                message MissionInfo {
                    optional string id = 1;
                }
                message Artifacts {
                    optional MissionInfo info = 1;
                }
            }
            message MissionInfo {
                optional bool other = 1;
            }
            """;
        var fdp = ProtoTextCompiler.Compile(p);
        var artifacts = fdp.MessageType[0].NestedType.First(n => n.Name == "Artifacts");
        Assert.Equal(".ei.Backup.MissionInfo", artifacts.Field[0].TypeName);
    }

    private const string LabelSample = """
        syntax = "proto2";
        package ei;
        message M {
            optional int32 a = 1;
            required string b = 2;
            repeated bool c = 3;
            uint32 d = 4;
        }
        """;

    private const string DefaultsSample = """
        syntax = "proto2";
        package ei;
        message ArtifactSpec {
            enum Rarity {
                COMMON = 0;
                RARE = 1;
            }
        }
        message D {
            optional uint32 a = 1 [default = 0];
            optional bool b = 2 [default = true];
            optional double c = 3 [default = 0.5];
            optional double d = 4 [default = 1e+06];
            optional ArtifactSpec.Rarity e = 5 [default = COMMON];
            optional string f = 6 [default = "a\nb"];
        }
        """;

    private const string OrderingSample = """
        syntax = "proto2";
        package ei;
        enum TopA {
            X = 0;
        }
        message Outer {
            optional int32 f1 = 1;
            message Inner1 {
                optional int32 g = 1;
            }
            enum InnerEnum {
                Y = 0;
                NEGATIVE = -3;
            }
            optional int32 f2 = 2;
            message Inner2 {
                optional int32 h = 1;
            }
        }
        enum TopB {
            Z = 0;
        }
        """;

    private const string ScopingSample = """
        syntax = "proto2";
        package ei;
        message ArtifactSpec {
            enum Rarity {
                COMMON = 0;
            }
        }
        message Outer {
            enum Kind {
                A = 0;
            }
            message Inner {
                enum Kind {
                    B = 0;
                }
                optional Kind k = 1;
                optional ArtifactSpec.Rarity r = 2;
            }
            optional Kind k = 1;
        }
        """;

    private const string OneofSample = """
        syntax = "proto2";
        package ei;
        message M {
            oneof pick {
                string a = 1;
                int32 b = 2;
            }
            optional bool c = 3;
        }
        """;

    private const string SkippedLinesSample = """
        syntax = "proto2";
        package ei;
        import "other.proto";
        option java_package = "com.example";
        message M {
            reserved 4, 5;
            extensions 100 to 199;
            option message_set_wire_format = false;
            optional int32 a = 1;
        }
        """;

    private const string UnresolvedSample = """
        syntax = "proto2";
        package ei;
        message M {
            optional Nope n = 1;
        }
        """;

    [Fact]
    public void Compile_SetsFileNameAndPackage_AndLeavesProto2SyntaxUnset() {
        var fdp = ProtoTextCompiler.Compile(LabelSample);
        Assert.Equal("ei.proto", fdp.Name);
        Assert.Equal("ei", fdp.Package);
        Assert.False(fdp.HasSyntax);
    }

    [Fact]
    public void Compile_Labels_BareFieldBecomesOptional() {
        var m = ProtoTextCompiler.Compile(LabelSample).MessageType.Single();
        Assert.Equal(FieldDescriptorProto.Types.Label.Optional, m.Field[0].Label);
        Assert.Equal(FieldDescriptorProto.Types.Label.Required, m.Field[1].Label);
        Assert.Equal(FieldDescriptorProto.Types.Label.Repeated, m.Field[2].Label);
        Assert.Equal(FieldDescriptorProto.Types.Label.Optional, m.Field[3].Label);
        Assert.Equal(FieldDescriptorProto.Types.Type.Uint32, m.Field[3].Type);
    }

    [Fact]
    public void Compile_Defaults_KeepsTokenVerbatimExceptStrings() {
        var d = ProtoTextCompiler.Compile(DefaultsSample).MessageType.Single(m => m.Name == "D");
        Assert.Equal("0", d.Field[0].DefaultValue);
        Assert.Equal("true", d.Field[1].DefaultValue);
        Assert.Equal("0.5", d.Field[2].DefaultValue);
        Assert.Equal("1e+06", d.Field[3].DefaultValue);
        Assert.Equal("COMMON", d.Field[4].DefaultValue);
        Assert.Equal(".ei.ArtifactSpec.Rarity", d.Field[4].TypeName);
        Assert.Equal("a\nb", d.Field[5].DefaultValue);
    }

    [Fact]
    public void Compile_NoDefault_LeavesDefaultValueUnset() {
        var m = ProtoTextCompiler.Compile(LabelSample).MessageType.Single();
        Assert.All(m.Field, f => Assert.False(f.HasDefaultValue));
    }

    [Fact]
    public void Compile_PreservesDeclarationOrderWithinEachCategory() {
        var fdp = ProtoTextCompiler.Compile(OrderingSample);
        Assert.Equal(["TopA", "TopB"], [.. fdp.EnumType.Select(e => e.Name)]);
        Assert.Equal(["Outer"], [.. fdp.MessageType.Select(m => m.Name)]);

        var outer = fdp.MessageType[0];
        Assert.Equal(["f1", "f2"], [.. outer.Field.Select(f => f.Name)]);
        Assert.Equal(["Inner1", "Inner2"], [.. outer.NestedType.Select(m => m.Name)]);
        Assert.Equal(["InnerEnum"], [.. outer.EnumType.Select(e => e.Name)]);
        Assert.Equal(-3, outer.EnumType[0].Value[1].Number);
    }

    [Fact]
    public void Compile_ResolvesScopedAndShadowedTypes() {
        var fdp = ProtoTextCompiler.Compile(ScopingSample);
        var outer = fdp.MessageType.Single(m => m.Name == "Outer");
        var inner = outer.NestedType.Single();

        Assert.Equal(".ei.Outer.Inner.Kind", inner.Field[0].TypeName);
        Assert.Equal(FieldDescriptorProto.Types.Type.Enum, inner.Field[0].Type);
        Assert.Equal(".ei.ArtifactSpec.Rarity", inner.Field[1].TypeName);
        Assert.Equal(".ei.Outer.Kind", outer.Field[0].TypeName);
    }

    [Fact]
    public void Compile_ResolvesPackageQualifiedTypeNames() {
        const string text = """
            syntax = "proto2";
            package ei;
            message Leaf {
                optional int32 a = 1;
            }
            message Holder {
                optional ei.Leaf leaf = 1;
            }
            """;

        var holder = ProtoTextCompiler.Compile(text).MessageType.Single(m => m.Name == "Holder");
        Assert.Equal(".ei.Leaf", holder.Field[0].TypeName);
        Assert.Equal(FieldDescriptorProto.Types.Type.Message, holder.Field[0].Type);
    }

    [Fact]
    public void Compile_FlattensOneofIntoPlainOptionalFields() {
        var m = ProtoTextCompiler.Compile(OneofSample).MessageType.Single();
        Assert.Empty(m.OneofDecl);
        Assert.Equal(["a", "b", "c"], [.. m.Field.Select(f => f.Name)]);
        Assert.All(m.Field, f => Assert.Equal(FieldDescriptorProto.Types.Label.Optional, f.Label));
        Assert.All(m.Field, f => Assert.False(f.HasOneofIndex));
    }

    [Fact]
    public void Compile_IgnoresOptionReservedExtensionsAndImport() {
        var fdp = ProtoTextCompiler.Compile(SkippedLinesSample);
        Assert.Empty(fdp.Dependency);
        var m = fdp.MessageType.Single();
        Assert.Empty(m.ExtensionRange);
        Assert.Equal(["a"], [.. m.Field.Select(f => f.Name)]);
    }

    [Fact]
    public void Compile_StripsComments() {
        const string text = """
            syntax = "proto2";
            package ei;
            /* block
               comment */
            message M {
                // leading comment
                optional int32 a = 1; // trailing comment
            }
            """;

        var m = ProtoTextCompiler.Compile(text).MessageType.Single();
        Assert.Equal(["a"], [.. m.Field.Select(f => f.Name)]);
    }

    [Fact]
    public void Compile_UnresolvedType_ThrowsWithLineNumber() {
        var ex = Assert.Throws<FormatException>(() => ProtoTextCompiler.Compile(UnresolvedSample));
        Assert.Contains("line 4", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Nope", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Compile_GarbageLine_ThrowsWithLineNumber() {
        const string text = """
            syntax = "proto2";
            package ei;
            message M {
                this is not a field
            }
            """;

        var ex = Assert.Throws<FormatException>(() => ProtoTextCompiler.Compile(text));
        Assert.Contains("line 4", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Compile_OneLineMessageAfterCloseBrace_ParsesBothMessages() {
        const string text = """
            syntax = "proto2";
            package ei;
            message M {
                optional int32 a = 1;
            } message N { optional int32 b = 1; }
            """;

        var fdp = ProtoTextCompiler.Compile(text);
        Assert.Equal(["M", "N"], [.. fdp.MessageType.Select(m => m.Name)]);
        Assert.Equal(["a"], [.. fdp.MessageType[0].Field.Select(f => f.Name)]);
        Assert.Equal(["b"], [.. fdp.MessageType[1].Field.Select(f => f.Name)]);
    }

    [Fact]
    public void Compile_OneLineMessageWithBody_ParsesField() {
        const string text = """
            syntax = "proto2";
            package ei;
            enum AdNetwork { VUNGLE = 0; }
            message EggIncAdConfig { repeated AdNetwork network_priority = 1; }
            """;

        var fdp = ProtoTextCompiler.Compile(text);
        var msg = fdp.MessageType.Single(m => m.Name == "EggIncAdConfig");
        Assert.Equal("network_priority", msg.Field[0].Name);
        Assert.Equal(FieldDescriptorProto.Types.Label.Repeated, msg.Field[0].Label);
    }

    [Fact]
    public void Compile_OneLineEnumWithValues_ParsesEveryValue() {
        const string text = """
            syntax = "proto2";
            package ei;
            enum Platform { UNKNOWN_PLATFORM = 0; IOS = 1; DROID = 2; }
            """;

        var fdp = ProtoTextCompiler.Compile(text);
        var e = fdp.EnumType.Single(x => x.Name == "Platform");
        Assert.Equal(["UNKNOWN_PLATFORM", "IOS", "DROID"], [.. e.Value.Select(v => v.Name)]);
    }

    [Fact]
    public void Normalize_OneLineMessageAfterCloseBrace_NormalizesToMultiLine() {
        const string text = """
            syntax = "proto2";
            package ei;
            message M {
                optional int32 a = 1;
            } message N { optional int32 b = 1; }
            """;

        var result = ProtoCanonicalForm.Normalize(text);
        Assert.True(result.Ok, $"normalize failed: {result.Error}");
        Assert.NotNull(result.Sha);
        Assert.Contains("message M {", result.Text!, StringComparison.Ordinal);
        Assert.Contains("message N {", result.Text!, StringComparison.Ordinal);

        var renorm = ProtoCanonicalForm.Normalize(result.Text!);
        Assert.True(renorm.Ok, $"renormalize failed: {renorm.Error}");
        Assert.Equal(result.Text, renorm.Text);
        Assert.Equal(result.Sha, renorm.Sha);
    }

    [Fact]
    public void Compile_StackedCloseBracesOnOneLine_PopsEveryFrame() {
        const string text = """
            syntax = "proto2";
            package ei;
            message Outer {
                message Inner {
                    optional int32 a = 1;
                }}
            message Other {
                optional int32 b = 1;
            }
            """;

        var fdp = ProtoTextCompiler.Compile(text);
        Assert.Equal(["Outer", "Other"], [.. fdp.MessageType.Select(m => m.Name)]);
        Assert.Equal(["Inner"], [.. fdp.MessageType[0].NestedType.Select(m => m.Name)]);
        Assert.Empty(fdp.MessageType[0].Field);
    }

    [Fact]
    public void Compile_ExtraCloseBrace_Throws() {
        const string text = """
            syntax = "proto2";
            package ei;
            message M {
                optional int32 a = 1;
            }}
            """;

        var ex = Assert.Throws<FormatException>(() => ProtoTextCompiler.Compile(text));
        Assert.Contains("line 5", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Normalize_FieldNumberOutOfRange_ReportsError() {
        const string text = """
            syntax = "proto2";
            package ei;
            message M {
                optional int32 a = 99999999999;
            }
            """;

        var result = ProtoCanonicalForm.Normalize(text);
        Assert.False(result.Ok);
        Assert.Null(result.Sha);
        Assert.Contains("line 4", result.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void Normalize_EnumValueOutOfRange_ReportsError() {
        const string text = """
            syntax = "proto2";
            package ei;
            enum E {
                A = 99999999999;
            }
            """;

        var result = ProtoCanonicalForm.Normalize(text);
        Assert.False(result.Ok);
        Assert.Null(result.Sha);
        Assert.Contains("line 4", result.Error!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n\t\n  ")]
    [InlineData("syntax = \"proto2\";\npackage ei;\n")]
    [InlineData("// only a comment\n")]
    public void Normalize_NoDeclarations_ReportsError(string text) {
        var result = ProtoCanonicalForm.Normalize(text);
        Assert.False(result.Ok);
        Assert.Null(result.Text);
        Assert.Null(result.Sha);
        Assert.Equal("no messages or enums", result.Error);
    }

    [Fact]
    public void Normalize_ProducesHouseFormatCanonicalTextAndIsIdempotent() {
        var first = ProtoCanonicalForm.Normalize(ScopingSample);
        Assert.True(first.Ok, $"normalize failed: {first.Error}");
        Assert.NotNull(first.Text);
        Assert.NotNull(first.Sha);
        Assert.StartsWith("syntax = \"proto2\";\n\npackage ei;\n", first.Text!, StringComparison.Ordinal);
        Assert.Contains("\n    enum Kind {\n", first.Text!, StringComparison.Ordinal);
        Assert.Contains("\n    message Inner {\n", first.Text!, StringComparison.Ordinal);

        var second = ProtoCanonicalForm.Normalize(first.Text!);
        Assert.True(second.Ok, $"renormalize failed: {second.Error}");
        Assert.Equal(first.Text, second.Text);
        Assert.Equal(first.Sha, second.Sha);
    }

    [Fact]
    public void Normalize_UnparseableInput_ReportsError() {
        var result = ProtoCanonicalForm.Normalize(UnresolvedSample);
        Assert.False(result.Ok);
        Assert.Null(result.Text);
        Assert.Null(result.Sha);
        Assert.Contains("line 4", result.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void Normalize_LegacySplitFileText_MergesCommonAndResolvesAux() {
        const string legacy = """
            syntax = "proto2";

            package ei;

            import "common.proto";

            message M {
                optional aux.Platform platform = 1;
                optional aux.DeviceFormFactor form_factor = 2;
                optional aux.AdNetwork ad_network = 3;
            }
            """;

        var result = ProtoCanonicalForm.Normalize(legacy);
        Assert.True(result.Ok, $"normalize failed: {result.Error}");
        Assert.NotNull(result.Text);
        Assert.NotNull(result.Sha);
        Assert.DoesNotContain("aux.", result.Text!, StringComparison.Ordinal);
        Assert.DoesNotContain("import", result.Text!, StringComparison.Ordinal);
        Assert.Contains("enum Platform {", result.Text!, StringComparison.Ordinal);
        Assert.Contains("DROID = 2;", result.Text!, StringComparison.Ordinal);
        Assert.Contains("optional Platform platform = 1;", result.Text!, StringComparison.Ordinal);

        var renorm = ProtoCanonicalForm.Normalize(result.Text!);
        Assert.True(renorm.Ok, $"renormalize failed: {renorm.Error}");
        Assert.Equal(result.Text, renorm.Text);
        Assert.Equal(result.Sha, renorm.Sha);
    }

    [Fact]
    public void Normalize_NonAuxUnresolvedType_StillFails() {
        const string text = """
            syntax = "proto2";
            package ei;
            message M {
                optional auxiliary.Thing t = 1;
            }
            """;

        var result = ProtoCanonicalForm.Normalize(text);
        Assert.False(result.Ok);
        Assert.Null(result.Sha);
        Assert.Contains("auxiliary.Thing", result.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void Normalize_RepoEiProto_MatchesCompiledDescriptorHash() {
        string text = ReadRepoEiProto();
        Assert.False(string.IsNullOrEmpty(text), "EggSharp.Protos ei.proto not found next to the test assembly");

        var result = ProtoCanonicalForm.Normalize(text);
        Assert.True(result.Ok, $"normalize failed: {result.Error}");
        Assert.Equal(EggIncognito.Core.ProtoHash.Current(), result.Sha);
    }

    private static string ReadRepoEiProto() {
        string path = Path.Combine(AppContext.BaseDirectory, "Proto", "ei.proto");
        return File.Exists(path) ? File.ReadAllText(path) : "";
    }
}
