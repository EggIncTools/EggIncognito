using System.Text.Json;
using System.Text.Json.Nodes;

namespace EggIncognito.Core.Services;

public static class PostmanBundle {
    private static readonly JsonSerializerOptions IndentedJson = JsonPresets.Indented;

    private static readonly string[] PreRequest = [
        "const userId = pm.collectionVariables.get('userId') || '';",
        "if (userId) {",
        "    const bytes = [];",
        "    for (let i = 0; i < userId.length; i++) bytes.push(userId.charCodeAt(i));",
        "    const proto = [0x32, bytes.length, ...bytes];",
        "    const base64 = btoa(String.fromCharCode(...proto));",
        "    pm.variables.set('authData', base64);",
        "} else {",
        "    pm.variables.set('authData', '');",
        "}"
    ];

    public static string BuildJson(string yamlPath) {
        var catalog = new RouteCatalog(yamlPath);
        var reflection = new ProtoReflection();

        string Describe(string? requestType, string? responseType) {
            string req = requestType is null
                ? "Request: (unknown)"
                : "Request: " + requestType + FieldLines(reflection, requestType);
            string res = responseType is null
                ? "Response: (unknown)"
                : "Response: " + responseType + FieldLines(reflection, responseType);
            return req + "\n\n" + res +
                   "\n\nSubmit base64-encoded proto bytes as the 'data' form field. Response body is also base64 proto.";
        }

        var groups = catalog.All()
            .Where(r => r.RawResponse is null)
            .GroupBy(r => r.Path.Split('/')[0])
            .OrderBy(g => g.Key);

        var folders = new JsonArray();
        foreach (var g in groups) {
            var requests = new JsonArray();
            foreach (var e in g) {
                var parts = new JsonArray();
                foreach (string p in e.Path.Split('/')) parts.Add(p);
                requests.Add(new JsonObject {
                    ["name"] = e.Path,
                    ["event"] = new JsonArray {
                        new JsonObject {
                            ["listen"] = "prerequest",
                            ["script"] = new JsonObject {
                                ["type"] = "text/javascript",
                                ["exec"] = new JsonArray(PreRequest.Select(l => (JsonNode)l).ToArray())
                            }
                        }
                    },
                    ["request"] = new JsonObject {
                        ["method"] = "POST",
                        ["header"] = new JsonArray(),
                        ["body"] = new JsonObject {
                            ["mode"] = "urlencoded",
                            ["urlencoded"] = new JsonArray {
                                new JsonObject {
                                    ["key"] = "data",
                                    ["value"] = "{{authData}}",
                                    ["description"] = $"base64({e.Request} proto bytes)",
                                    ["type"] = "text"
                                }
                            }
                        },
                        ["url"] = new JsonObject {
                            ["raw"] = "{{baseUrl}}/" + e.Path,
                            ["host"] = new JsonArray { "{{baseUrl}}" },
                            ["path"] = parts,
                            ["query"] = new JsonArray {
                                new JsonObject {
                                    ["key"] = "sim",
                                    ["value"] = "",
                                    ["description"] = "Simulation behavior. Leave blank for normal response.",
                                    ["disabled"] = true
                                }
                            }
                        },
                        ["description"] = Describe(e.Request, e.Response)
                    },
                    ["response"] = new JsonArray()
                });
            }

            folders.Add(new JsonObject { ["name"] = g.Key, ["item"] = requests });
        }

        folders.Add(SimulationFolder());

        var collection = new JsonObject {
            ["info"] = new JsonObject {
                ["_postman_id"] = "egg-inc-test-api",
                ["name"] = "EggIncognito",
                ["description"] = "Mock server for the Egg, Inc. API. POST base64 protobuf as form field 'data'.",
                ["schema"] = "https://schema.getpostman.com/json/collection/v2.1.0/collection.json"
            },
            ["variable"] = new JsonArray {
                new JsonObject { ["key"] = "baseUrl", ["value"] = "http://localhost:5080", ["type"] = "string" },
                new JsonObject { ["key"] = "userId", ["value"] = "", ["type"] = "string" }
            },
            ["item"] = folders
        };

        return collection.ToJsonString(IndentedJson);
    }

    private static string FieldLines(ProtoReflection reflection, string typeName) {
        var schema = reflection.Schema(typeName);
        return schema is null || schema.Fields.Count == 0
            ? ""
            : "\n" + string.Join("\n", schema.Fields.Select(f => $"  {f.Name} {f.Type}"));
    }

    private static JsonObject SimulationFolder() => new() {
        ["name"] = "Simulation",
        ["item"] = new JsonArray {
            new JsonObject {
                ["name"] = "OPTIONS / (all behaviors)",
                ["request"] = new JsonObject {
                    ["method"] = "OPTIONS",
                    ["header"] = new JsonArray(),
                    ["url"] = new JsonObject {
                        ["raw"] = "{{baseUrl}}/",
                        ["host"] = new JsonArray { "{{baseUrl}}" },
                        ["path"] = new JsonArray { "" }
                    },
                    ["description"] = "Returns JSON array of all simulation behaviors."
                },
                ["response"] = new JsonArray()
            }
        }
    };
}
