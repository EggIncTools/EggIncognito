using System.Net;
using Bunit;
using EggIncognito.Components.Shared;
using EggIncognito.Core.Services;
using EggIncognito.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace EggIncognito.Tests;

public class DocsHubTests {
    [Collection(SharedAppCollection.Name)]
    public class Integration(SharedAppFactory f) {
        private readonly WebApplicationFactory<Program> _f = f;

        [Fact]
        public async Task DocsRoute_StillResponds() {
            var c = _f.CreateClient();
            var r = await c.GetAsync("/docs");
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        }

        [Fact]
        public async Task SubjectTags_EndpointKind_Accepted() {
            var c = _f.CreateClient();
            var r = await c.GetAsync("/api/docs/subject-tags/endpoint/ei/first_contact");
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            string body = await r.Content.ReadAsStringAsync();
            Assert.Equal("[]", body.Trim());
        }

        [Fact]
        public async Task SubjectTags_RemovedKind_Rejected() {
            var c = _f.CreateClient();
            var r = await c.GetAsync("/api/docs/subject-tags/config/AppMode");
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        }
    }

    public class MarkdownComponent : BunitContext {
        [Fact]
        public void Markdown_RendersBold() {
            var cut = Render<Markdown>(p => p.Add(c => c.Body, "**hi**"));
            Assert.Contains("<strong>hi</strong>", cut.Markup);
        }
    }

    public class DocHelpComponent : BunitContext {
        private void Wire() {
            Services.AddSingleton<IProtoReflection, ProtoReflection>();
            Services.AddSingleton<IRouteCatalog>(new RouteCatalog("__no_routes_yaml__"));
            Services.AddSingleton<IDocRegistry, DocRegistry>();
            Services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor());
            Services.AddHttpClient();
        }

        [Fact]
        public void DocHelp_KnownMessage_RendersAffordance() {
            Wire();
            var cut = Render<DocHelp>(p => p
                .Add(c => c.Kind, "message")
                .Add(c => c.Key, "Contract"));
            Assert.NotNull(cut.Find(".dochelp"));
        }

        [Fact]
        public void DocHelp_UnknownSubject_RendersNothing() {
            Wire();
            var cut = Render<DocHelp>(p => p
                .Add(c => c.Kind, "message")
                .Add(c => c.Key, "NoSuchKey"));
            Assert.Empty(cut.Markup.Trim());
        }
    }
}
