using Bunit;
using EggIncognito.Capture;
using EggIncognito.Components.Capture;
using EggIncognito.Components.Shared.Code;
using EggIncognito.Core.Services.Syntax;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace EggIncognito.Tests;

public class CapturePageTests {
    public class Components : BunitContext {
        public Components() {
            Services.AddSingleton<IWebHostEnvironment>(new FakeWebHostEnvironment());
        }

        [Fact]
        public void CodeTree_RendersNodesForObject() {
            var root = CodeTreeNode.Parse("{\"a\":1,\"b\":{\"c\":\"hi\"}}");
            Assert.NotNull(root);
            var cut = Render<CodeTree>(p => p
                .Add(c => c.Root, root)
                .Add(c => c.View, new CaptureViewState()));

            Assert.NotEmpty(cut.FindAll(".jtree-key"));
            Assert.Contains("a", cut.Markup);
            Assert.Contains("c", cut.Markup);
            Assert.NotEmpty(cut.FindAll(".jv"));
            Assert.Contains("Expand all", cut.Markup);
        }

        [Fact]
        public void CodeTree_LeafColorsComeFromTokenPalette() {
            var root = CodeTreeNode.Parse("{\"s\":\"hi\",\"n\":1,\"b\":true,\"z\":null}");
            var cut = Render<CodeTree>(p => p
                .Add(c => c.Root, root)
                .Add(c => c.View, new CaptureViewState()));

            Assert.NotEmpty(cut.FindAll(".jv.tok-string"));
            Assert.NotEmpty(cut.FindAll(".jv.tok-number"));
            Assert.NotEmpty(cut.FindAll(".jv.tok-bool"));
            Assert.NotEmpty(cut.FindAll(".jv.tok-null"));
            Assert.DoesNotContain("jv-string", cut.Markup);
        }

        [Fact]
        public void CodeTree_Search_MarksMatchesAndDimsOthers() {
            var root = CodeTreeNode.Parse("{\"alpha\":1,\"beta\":2}");
            var cut = Render<CodeTree>(p => p
                .Add(c => c.Root, root)
                .Add(c => c.View, new CaptureViewState()));

            cut.Find(".jtree-search").Input("alpha");

            cut.WaitForAssertion(() => {
                Assert.Contains("<mark>", cut.Markup);
                Assert.Contains("jtree-dim", cut.Markup);
                Assert.Contains("1 match", cut.Markup);
            }, TimeSpan.FromSeconds(2));
        }

        [Fact]
        public void CodeFormats_BlurredValue_TogglesRevealOnClick() {
            var view = new CaptureViewState { RedactionMode = "blur", DefaultFormat = "json" };
            var cut = Render<CodeFormats>(p => p
                .Add(c => c.Key, "Request")
                .Add(c => c.Json, "{\"eiUserId\":\"EI1234567890\"}")
                .Add(c => c.View, view));

            var span = cut.Find(".mask");
            Assert.DoesNotContain("revealed", span.GetAttribute("class"));
            span.Click();
            Assert.Contains("revealed", cut.Find(".mask").GetAttribute("class"));
            cut.Find(".mask").Click();
            Assert.DoesNotContain("revealed", cut.Find(".mask").GetAttribute("class"));
        }

        [Fact]
        public void FlowList_AutoScroll_FollowsNewestRow() {
            var flows = new List<DashboardFlow> {
                new(1, "12:00:00", "ei/first_contact", "POST", 200, null, null, "", null)
            };
            var cut = Render<FlowList>(p => p
                .Add(c => c.Flows, flows)
                .Add(c => c.View, new CaptureViewState { AutoScroll = true }));

            Assert.Contains("flow-wrap-follow", cut.Find("#flowList").GetAttribute("class"));
            Assert.NotNull(cut.Find("#flowList > .flow-anchor"));
        }

        [Fact]
        public void FlowList_AutoScrollOff_DoesNotFollow() {
            var flows = new List<DashboardFlow> {
                new(1, "12:00:00", "ei/first_contact", "POST", 200, null, null, "", null)
            };
            var cut = Render<FlowList>(p => p
                .Add(c => c.Flows, flows)
                .Add(c => c.View, new CaptureViewState { AutoScroll = false }));

            Assert.DoesNotContain("flow-wrap-follow", cut.Find("#flowList").GetAttribute("class"));
        }

        [Fact]
        public void CodeFormats_RendersHexOffsetsInTheGutter() {
            var view = new CaptureViewState { DefaultFormat = "hex" };
            var cut = Render<CodeFormats>(p => p
                .Add(c => c.Key, "Response")
                .Add(c => c.Json, null)
                .Add(c => c.RawBase64, "AAEC")
                .Add(c => c.View, view));

            var gutter = cut.Find(".code-gutter");
            Assert.Equal("00000000", gutter.TextContent);
            Assert.NotEmpty(cut.FindAll(".code-line .tok-byte"));
            Assert.DoesNotContain("00000000  00 01", cut.Find(".code-line").TextContent);
        }
    }

    public class Format {
        [Fact]
        public void Yaml_RendersNestedObject() {
            string y = DataFormats.JsonToText("{\"a\":1,\"b\":{\"c\":2}}", "yaml");
            Assert.Contains("a: 1", y);
            Assert.Contains("b:", y);
            Assert.Contains("c: 2", y);
        }

        [Fact]
        public void Hex_EmptyInput_ReportsEmpty() => Assert.Equal("(empty)", DataFormats.BytesToText("", "hex"));

        [Fact]
        public void Xml_WrapsAndPrettyPrints() {
            string x = DataFormats.JsonToText("{\"a\":1}", "xml");
            Assert.Contains("<root>", x);
            Assert.Contains("<a>1</a>", x);
        }
    }
}
