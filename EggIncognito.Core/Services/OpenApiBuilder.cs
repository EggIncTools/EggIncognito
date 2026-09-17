using System.Text.Json;
using System.Text.Json.Nodes;
using Google.Protobuf.Reflection;

namespace EggIncognito.Core.Services;

public static class OpenApiBuilder {
    private const string InfoDescription =
        "Stateless mock of the Egg Inc (auxbrain) API. Every operation is a POST with an " +
        "application/x-www-form-urlencoded body where data is a base64-encoded protobuf message. " +
        "Signing is permissive: the mock accepts unsigned requests. The real API requires the " +
        "request to be wrapped in a signed AuthenticatedMessage on routes marked " +
        "x-eggincognito-request-wrapped. Build, sign, and decode requests interactively at /protos#api.";

    private static readonly JsonSerializerOptions IndentedJson = JsonPresets.Indented;

    public static string BuildJson(IReadOnlyList<AuxbrainEntry> entries, IProtoReflection reflection) {
        var components = new SortedDictionary<string, JsonObject>(StringComparer.Ordinal);

        var paths = new JsonObject();
        foreach (var e in entries) {
            paths["/" + e.Path] = new JsonObject { ["post"] = Operation(e, reflection, components) };
            if (e.PathParam) {
                paths["/" + e.Path + "/{eid}"] = new JsonObject {
                    ["post"] = Operation(e, reflection, components, true)
                };
            }
        }

        var schemas = new JsonObject();
        foreach ((string name, var schema) in components) schemas[name] = schema;

        var doc = new JsonObject {
            ["openapi"] = "3.0.3",
            ["info"] = new JsonObject {
                ["title"] = "EggIncognito mock API",
                ["version"] = "1.0.0",
                ["description"] = InfoDescription
            },
            ["paths"] = paths,
            ["components"] = new JsonObject { ["schemas"] = schemas }
        };
        return doc.ToJsonString(IndentedJson);
    }

    private static JsonObject Operation(
        AuxbrainEntry e,
        IProtoReflection reflection,
        IDictionary<string, JsonObject> components,
        bool eidVariant = false) {
        var op = new JsonObject {
            ["operationId"] = e.Path.Replace('/', '_') + (eidVariant ? "_eid" : ""),
            ["summary"] = e.Path,
            ["description"] = Describe(e),
            ["tags"] = new JsonArray(e.Namespace)
        };

        if (eidVariant) {
            op["parameters"] = new JsonArray(new JsonObject {
                ["name"] = "eid",
                ["in"] = "path",
                ["required"] = true,
                ["schema"] = new JsonObject { ["type"] = "string" },
                ["description"] = "Egg Inc user id (EI...)."
            });
        }

        op["requestBody"] = RequestBody(e);
        op["responses"] = new JsonObject { ["200"] = Response(e, reflection, components) };

        op["x-eggincognito-status"] = AuxbrainCatalog.Label(e.Status);
        op["x-eggincognito-request-wrapped"] = e.RequestWrapped;
        op["x-eggincognito-response-wrapped"] = e.ResponseWrapped;
        if (e.Aliases.Count > 0)
            op["x-eggincognito-aliases"] = new JsonArray(e.Aliases.Select(a => (JsonNode)a).ToArray());
        return op;
    }

    private static string Describe(AuxbrainEntry e) {
        string req = (e.RequestType, e.RequestWrapped) switch {
            (null, true) => "Request: AuthenticatedMessage (inner type unknown).",
            (null, false) => "Request: unknown.",
            (var t, true) => $"Request: {t}, wrapped in a signed AuthenticatedMessage on the real API.",
            (var t, false) => $"Request: {t}."
        };
        string res = (e.ResponseType, e.ResponseWrapped) switch {
            (null, true) => "Response: AuthenticatedMessage (inner type unknown).",
            (null, false) => "Response: unknown.",
            (var t, true) => $"Response: {t}, AuthenticatedMessage-wrapped on the real API.",
            (var t, false) => $"Response: {t}."
        };
        return req + " " + res;
    }

    private static JsonObject RequestBody(AuxbrainEntry e) {
        string inner = e.RequestType ?? "request";
        string desc = e.RequestWrapped
            ? $"base64-encoded AuthenticatedMessage wrapping {inner} (mock also accepts the bare inner message)"
            : $"base64-encoded {inner} protobuf";
        return new JsonObject {
            ["required"] = true,
            ["content"] = new JsonObject {
                ["application/x-www-form-urlencoded"] = new JsonObject {
                    ["schema"] = new JsonObject {
                        ["type"] = "object",
                        ["required"] = new JsonArray("data"),
                        ["properties"] = new JsonObject {
                            ["data"] = new JsonObject {
                                ["type"] = "string",
                                ["format"] = "byte",
                                ["description"] = desc
                            }
                        }
                    }
                }
            }
        };
    }

    private static JsonObject Response(
        AuxbrainEntry e, IProtoReflection reflection, IDictionary<string, JsonObject> components) {
        var desc = e.ResponseType is null ? null : reflection.FindMessage(e.ResponseType);
        var response = new JsonObject {
            ["description"] = e.ResponseType is null
                ? "Canned response; no response type known."
                : $"Canned {e.ResponseType}, shown decoded as JSON."
        };
        if (desc is not null) {
            response["content"] = new JsonObject {
                ["application/json"] = new JsonObject { ["schema"] = Ref(desc, components) }
            };
        }

        return response;
    }

    private static JsonObject Ref(MessageDescriptor d, IDictionary<string, JsonObject> components) {
        string name = ComponentName(d);
        if (!components.ContainsKey(name)) {
            var schema = new JsonObject { ["type"] = "object" };
            components[name] = schema;
            var props = new JsonObject();
            foreach (var f in d.Fields.InFieldNumberOrder())
                props[f.JsonName] = FieldSchema(f, components);
            schema["properties"] = props;
        }

        return new JsonObject { ["$ref"] = "#/components/schemas/" + name };
    }

    private static string ComponentName(MessageDescriptor d) =>
        d.FullName.StartsWith("ei.", StringComparison.Ordinal) ? d.FullName[3..] : d.FullName;

    private static JsonObject FieldSchema(FieldDescriptor f, IDictionary<string, JsonObject> components) {
        if (f.IsMap) {
            var value = f.MessageType.FindFieldByNumber(2);
            return new JsonObject {
                ["type"] = "object",
                ["additionalProperties"] = SingleSchema(value, components)
            };
        }

        return f.IsRepeated
            ? new JsonObject { ["type"] = "array", ["items"] = SingleSchema(f, components) }
            : SingleSchema(f, components);
    }

    private static JsonObject SingleSchema(FieldDescriptor f, IDictionary<string, JsonObject> components) {
        return f.FieldType switch {
            FieldType.Message or FieldType.Group => Ref(f.MessageType, components),
            FieldType.Enum => new JsonObject {
                ["type"] = "string",
                ["enum"] = new JsonArray(f.EnumType.Values.Select(v => (JsonNode)v.Name).ToArray())
            },
            FieldType.Double => Scalar("number", "double"),
            FieldType.Float => Scalar("number", "float"),
            FieldType.Int32 or FieldType.SInt32 or FieldType.SFixed32 => Scalar("integer", "int32"),
            FieldType.UInt32 or FieldType.Fixed32 => Scalar("integer", "int64"),
            FieldType.Int64 or FieldType.SInt64 or FieldType.SFixed64 => Scalar("string", "int64"),
            FieldType.UInt64 or FieldType.Fixed64 => Scalar("string", "uint64"),
            FieldType.Bool => Scalar("boolean", null),
            FieldType.Bytes => Scalar("string", "byte"),
            _ => Scalar("string", null)
        };
    }

    private static JsonObject Scalar(string type, string? format) {
        var o = new JsonObject { ["type"] = type };
        if (format is not null) o["format"] = format;
        return o;
    }
}
