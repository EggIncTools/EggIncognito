using System.Net;
using System.Net.Http.Headers;

namespace EggIncognito.Tests;

[Collection(HostedAppCollection.Name)]
public class DocsGateTests(HostedAppFactory f) {
    [Fact]
    public async Task UpsertDoc_Anonymous_Is403() {
        var c = f.CreateClient();
        var r = await c.PostAsJsonAsync("/api/docs/doc",
            new { subjectKind = "message", subjectKey = "Contract", bodyMd = "# hi" });
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task SetSubjectTags_Anonymous_Is403() {
        var c = f.CreateClient();
        var r = await c.PostAsJsonAsync("/api/docs/subject-tags",
            new { subjectKind = "message", subjectKey = "Contract", tagIds = new long[] { 1 } });
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task AddTag_Anonymous_Is403() {
        var c = f.CreateClient();
        var r = await c.PostAsJsonAsync("/api/admin/tag", new { slug = "x", label = "X", color = (string?)null });
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task GetDoc_Reachable_EmptyWhenNoDb() {
        var c = f.CreateClient();
        var r = await c.GetAsync("/api/docs/doc/message/Contract");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("bodyMd", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task GetTags_Reachable_EmptyWhenNoDb() {
        var c = f.CreateClient();
        var r = await c.GetAsync("/api/docs/tags");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task GetDoc_InvalidKind_Is400() {
        var c = f.CreateClient();
        var r = await c.GetAsync("/api/docs/doc/bogus/Contract");
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task TagsMap_Reachable_EmptyWhenNoDb() {
        var c = f.CreateClient();
        var r = await c.GetAsync("/api/docs/tags-map");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task UploadImage_Anonymous_Is403() {
        var c = f.CreateClient();
        using var content = new MultipartFormDataContent();
        byte[] bytes = [0x89, 0x50, 0x4E, 0x47];
        var part = new ByteArrayContent(bytes);
        part.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(part, "file", "x.png");
        var r = await c.PostAsync("/api/docs/image", content);
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task GetImage_NoDb_Is404() {
        var c = f.CreateClient();
        var r = await c.GetAsync("/api/docs/image/1");
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }
}
