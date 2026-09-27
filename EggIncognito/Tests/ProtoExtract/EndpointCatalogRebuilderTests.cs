using EggIncognito.Core.Services;
using EggIncognito.Core.Services.ProtoExtract;
using EggIncognito.Services.DataApi;

namespace EggIncognito.Tests.ProtoExtract;

public class EndpointCatalogRebuilderTests {
    private static string? FindContentRoot() {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EggIncognito.slnx"))) dir = dir.Parent;
        return dir is null ? null : Path.Combine(dir.FullName, "EggIncognito");
    }

    [Fact]
    public void ExcludedPaths_ContainsOnlyKnownFalseFlags() {
        string? contentRoot = FindContentRoot();
        if (contentRoot is null) return;

        var yaml = RouteCatalog.ForRepo(contentRoot);
        Assert.Contains("ei/kb", yaml.ExcludedPaths);
        Assert.Single(yaml.ExcludedPaths);
        Assert.DoesNotContain("ei/has_voted", yaml.ExcludedPaths);
        Assert.DoesNotContain("ei/shell_showcase_view_poll", yaml.ExcludedPaths);
        Assert.DoesNotContain("ei/confirm_gift_delivery", yaml.ExcludedPaths);
        Assert.DoesNotContain("ei_srv/sub_change_hint", yaml.ExcludedPaths);
        Assert.DoesNotContain("ei_ctx/mark_evaluation_read", yaml.ExcludedPaths);
    }

    [Fact]
    public void Filter_DropsOnlyFalseFlags_KeepsNotYetMockedPaths() {
        string? contentRoot = FindContentRoot();
        if (contentRoot is null) return;

        var yaml = RouteCatalog.ForRepo(contentRoot);
        var descriptors = new[] {
            new EndpointCatalogExtractor.EndpointDescriptor("POST", "ei/kb", null, null, false, false),
            new EndpointCatalogExtractor.EndpointDescriptor("POST", "ei_afx/zoom_zoom", null, null, false, false),
            new EndpointCatalogExtractor.EndpointDescriptor("POST", "ei/has_voted", null, null, false, false),
            new EndpointCatalogExtractor.EndpointDescriptor("POST", "ei/shell_showcase_view_poll", null, null, false,
                false),
            new EndpointCatalogExtractor.EndpointDescriptor("POST", "ei/confirm_gift_delivery", null, null, false,
                false),
            new EndpointCatalogExtractor.EndpointDescriptor("POST", "ei_srv/sub_change_hint", null, null, false,
                false),
            new EndpointCatalogExtractor.EndpointDescriptor("POST", "ei_ctx/mark_evaluation_read", null, null, false,
                false)
        };

        var kept = EndpointCatalogRebuilder.Filter(descriptors, yaml.ExcludedPaths);

        Assert.DoesNotContain(kept, e => e.Path == "ei/kb");
        Assert.Contains(kept, e => e.Path == "ei_afx/zoom_zoom");
        Assert.Contains(kept, e => e.Path == "ei/has_voted");
        Assert.Contains(kept, e => e.Path == "ei/shell_showcase_view_poll");
        Assert.Contains(kept, e => e.Path == "ei/confirm_gift_delivery");
        Assert.Contains(kept, e => e.Path == "ei_srv/sub_change_hint");
        Assert.Contains(kept, e => e.Path == "ei_ctx/mark_evaluation_read");
    }

    private static EndpointCatalogExtractor.EndpointDescriptor D(string path, string? request = null,
        string? response = null, bool requestWrapped = false, bool responseWrapped = false, string method = "post") =>
        new(method, path, request, response, requestWrapped, responseWrapped);

    private static EndpointCatalogRebuilder.MergeContributor C(string platform, string version,
        params EndpointCatalogExtractor.EndpointDescriptor[] endpoints) =>
        new(platform, version, endpoints);

    [Fact]
    public void Merge_DisjointPaths_UnionsAndAttributesEachRowToItsContributor() {
        var merged = EndpointCatalogRebuilder.Merge([
            C("android", "1.37.2", D("ei/a", "AReq", "AResp")),
            C("ios", "1.37.1", D("ei/b", "BReq", "BResp"))
        ]);

        Assert.Equal(2, merged.Count);
        Assert.Equal("ei/a", merged[0].Descriptor.Path);
        Assert.Equal("AResp", merged[0].Descriptor.ResponseType);
        Assert.Equal("android", merged[0].Platform);
        Assert.Equal("1.37.2", merged[0].Version);
        Assert.Equal("ei/b", merged[1].Descriptor.Path);
        Assert.Equal("BResp", merged[1].Descriptor.ResponseType);
        Assert.Equal("ios", merged[1].Platform);
        Assert.Equal("1.37.1", merged[1].Version);
    }

    [Fact]
    public void Merge_SamePathInBoth_OwnerWinsWhollyWhenNothingIsNull() {
        var merged = EndpointCatalogRebuilder.Merge([
            C("android", "1.37.2", D("ei/x", "OwnerReq", "OwnerResp", true, true, "ownerMethod")),
            C("ios", "1.37.1", D("ei/x", "DonorReq", "DonorResp", false, false, "donorMethod"))
        ]);

        var row = Assert.Single(merged);
        Assert.Equal("ownerMethod", row.Descriptor.Method);
        Assert.Equal("OwnerReq", row.Descriptor.RequestType);
        Assert.Equal("OwnerResp", row.Descriptor.ResponseType);
        Assert.True(row.Descriptor.RequestWrapped);
        Assert.True(row.Descriptor.ResponseWrapped);
        Assert.Equal("android", row.Platform);
        Assert.Equal("1.37.2", row.Version);
    }

    [Fact]
    public void Merge_OwnerMissingResponse_TakesDonorResponseAndItsWrappedFlagOnly() {
        var merged = EndpointCatalogRebuilder.Merge([
            C("android", "1.37.2", D("ei/x", "OwnerReq", null, true, false, "ownerMethod")),
            C("ios", "1.37.1", D("ei/x", "DonorReq", "DonorResp", false, true, "donorMethod"))
        ]);

        var row = Assert.Single(merged);
        Assert.Equal("DonorResp", row.Descriptor.ResponseType);
        Assert.True(row.Descriptor.ResponseWrapped);
        Assert.Equal("OwnerReq", row.Descriptor.RequestType);
        Assert.True(row.Descriptor.RequestWrapped);
        Assert.Equal("ownerMethod", row.Descriptor.Method);
        Assert.Equal("android", row.Platform);
        Assert.Equal("1.37.2", row.Version);
    }

    [Fact]
    public void Merge_OwnerMissingRequest_TakesDonorRequestAndItsWrappedFlagOnly() {
        var merged = EndpointCatalogRebuilder.Merge([
            C("android", "1.37.2", D("ei/x", null, "OwnerResp", false, true)),
            C("ios", "1.37.1", D("ei/x", "DonorReq", "DonorResp", true))
        ]);

        var row = Assert.Single(merged);
        Assert.Equal("DonorReq", row.Descriptor.RequestType);
        Assert.True(row.Descriptor.RequestWrapped);
        Assert.Equal("OwnerResp", row.Descriptor.ResponseType);
        Assert.True(row.Descriptor.ResponseWrapped);
    }

    [Fact]
    public void Merge_BothContributorsNullOnASide_StaysNull() {
        var merged = EndpointCatalogRebuilder.Merge([
            C("android", "1.37.2", D("ei/x", "OwnerReq")),
            C("ios", "1.37.1", D("ei/x", "DonorReq"))
        ]);

        var row = Assert.Single(merged);
        Assert.Equal("OwnerReq", row.Descriptor.RequestType);
        Assert.Null(row.Descriptor.ResponseType);
        Assert.False(row.Descriptor.ResponseWrapped);
    }

    [Fact]
    public void Merge_PreservesFirstAppearanceOrderAcrossContributors() {
        var merged = EndpointCatalogRebuilder.Merge([
            C("android", "1.37.2", D("ei/a"), D("ei/b")),
            C("ios", "1.37.1", D("ei/b"), D("ei/c"))
        ]);

        Assert.Equal(3, merged.Count);
        Assert.Equal("ei/a", merged[0].Descriptor.Path);
        Assert.Equal("ei/b", merged[1].Descriptor.Path);
        Assert.Equal("android", merged[1].Platform);
        Assert.Equal("ei/c", merged[2].Descriptor.Path);
        Assert.Equal("ios", merged[2].Platform);
    }

    [Fact]
    public void BuildNote_AllContributorsUsed_NoNotUsedSuffix() {
        string note = EndpointCatalogRebuilder.BuildNote([
            C("android", "1.37.2", D("ei/a"), D("ei/b")),
            C("ios", "1.37.1", D("ei/c"))
        ], 3, []);

        Assert.Equal("android 1.37.2 (2) + ios 1.37.1 (1), merged 3", note);
    }

    [Fact]
    public void BuildNote_WithNotUsed_AppendsSemicolonJoinedSuffix() {
        string note = EndpointCatalogRebuilder.BuildNote([C("android", "1.37.2", D("ei/a"), D("ei/b"))], 2,
            ["ios: no binary", "ios 1.37.1: 0 endpoints from 12 symbols"]);

        Assert.Equal(
            "android 1.37.2 (2), merged 2; not used: ios: no binary; ios 1.37.1: 0 endpoints from 12 symbols",
            note);
    }
}
