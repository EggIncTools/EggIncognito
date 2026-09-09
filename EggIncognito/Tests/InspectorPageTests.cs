using System.Net;
using System.Text.Json.Nodes;
using Bunit;
using EggIncognito.Components.Inspector;
using EggIncognito.Core.Services;
using EggIncognito.Services.Workbench;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EggIncognito.Tests;

public class InspectorPageTests {
    [Collection(SharedAppCollection.Name)]
    public class Integration(SharedAppFactory f) {
        private readonly WebApplicationFactory<Program> _f = f;

        [Fact]
        public async Task Inspector_RendersShell() {
            var c = _f.CreateClient();
            var r = await c.GetAsync("/inspector");
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        }

        [Fact]
        public async Task Inspector_DeadRoutes_AreGone() {
            var c = _f.CreateClient();
            var endpoints = await c.GetAsync("/api/inspector/endpoints");
            var schema = await c.GetAsync("/api/inspector/schema/EggIncFirstContactRequest");
            Assert.False(endpoints.IsSuccessStatusCode);
            Assert.False(schema.IsSuccessStatusCode);
        }

        [Fact]
        public async Task Inspector_MessagesRoute_Survives() {
            var c = _f.CreateClient();
            var r = await c.GetAsync("/api/inspector/messages");
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        }

        [Fact]
        public async Task RinfoSeed_NeverReturnsAHardcodedVersion() {
            var c = _f.CreateClient();
            var r = await c.GetAsync("/api/inspector/rinfo-seed");
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            string body = await r.Content.ReadAsStringAsync();
            Assert.DoesNotContain("1.35.7", body);
            Assert.DoesNotContain("111343", body);
        }
    }

    public class FieldTreeComponent : BunitContext {
        private static SchemaMessage Inner() => new("Inner", new List<SchemaField> {
            new("flag", "flag", 1, "bool", false, false, null, null)
        });

        private static SchemaMessage Root() => new("Root", new List<SchemaField> {
            new("name", "name", 1, "string", false, false, null, null),
            new("kind", "kind", 2, "enum", false, false, null,
                new List<SchemaEnumValue> { new("A", 0), new("B", 1) }),
            new("ids", "ids", 3, "int32", true, false, null, null),
            new("inner", "inner", 4, "message", false, false, "Inner", null)
        });

        [Fact]
        public void FieldTree_RendersAllFieldKinds() {
            var nodes = FieldTreeBuilder.Build(Root(),
                t => t == "Inner" ? Inner() : null);
            Services.AddWorkbenchStates();
            var cut = Render<FieldTree>(p => p.Add(c => c.Nodes, nodes));

            Assert.NotEmpty(cut.FindAll("input.field-input"));
            Assert.NotEmpty(cut.FindAll("select.field-input"));
            Assert.Contains(cut.FindAll("button.btn-mini"), b => b.TextContent.Trim() == "Add");

            Assert.Contains("flag", cut.Markup);
        }

        [Fact]
        public void Collect_WalksEditedTreeIntoProtoJson() {
            var nodes = FieldTreeBuilder.Build(Root(),
                t => t == "Inner" ? Inner() : null);

            nodes.First(n => n.Field.JsonName == "name").Value = "hi";
            nodes.First(n => n.Field.JsonName == "inner").Children
                .First(c => c.Field.JsonName == "flag").Value = "true";
            var ids = nodes.First(n => n.Field.JsonName == "ids");
            ids.Items.Add("7");

            string json = FieldTreeBuilder.Collect(nodes).ToJsonString();
            Assert.Contains("\"name\":\"hi\"", json);
            Assert.Contains("\"flag\":true", json);
            Assert.Contains("\"ids\":[7]", json);
        }

        [Fact]
        public void Apply_MapsRawJsonBackOntoTree() {
            var nodes = FieldTreeBuilder.Build(Root(),
                t => t == "Inner" ? Inner() : null);
            var obj = (JsonObject)JsonNode.Parse("{\"name\":\"hi\",\"ids\":[3,4],\"inner\":{\"flag\":true}}")!;

            FieldTreeBuilder.Apply(nodes, obj);

            Assert.Equal("hi", nodes.First(n => n.Field.JsonName == "name").Value);
            Assert.Equal(new[] { "3", "4" }, nodes.First(n => n.Field.JsonName == "ids").Items);
            Assert.Equal("true", nodes.First(n => n.Field.JsonName == "inner").Children
                .First(c => c.Field.JsonName == "flag").Value);

            FieldTreeBuilder.Apply(nodes, []);
            Assert.Equal("", nodes.First(n => n.Field.JsonName == "name").Value);
            Assert.Empty(nodes.First(n => n.Field.JsonName == "ids").Items);
            Assert.Equal("", nodes.First(n => n.Field.JsonName == "inner").Children
                .First(c => c.Field.JsonName == "flag").Value);
        }
    }
}
