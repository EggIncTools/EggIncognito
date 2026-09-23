using EggIncognito.Core.Services.ProtoExtract;

namespace EggIncognito.Tests;

public class ProtoDisplayFormTests {
    [Fact]
    public void Pair_BothCanonicalPresent_PicksCanonical() {
        var (a, b, form) = ProtoDisplayForm.Pair("canonA", "rawA", "canonB", "rawB");
        Assert.Equal("canonA", a);
        Assert.Equal("canonB", b);
        Assert.Equal(ProtoDisplayForm.Canonical, form);
    }

    [Fact]
    public void Pair_CanonA_Null_PicksRawForBothSides() {
        var (a, b, form) = ProtoDisplayForm.Pair(null, "rawA", "canonB", "rawB");
        Assert.Equal("rawA", a);
        Assert.Equal("rawB", b);
        Assert.Equal(ProtoDisplayForm.Raw, form);
    }

    [Fact]
    public void Pair_CanonB_Null_PicksRawForBothSides() {
        var (a, b, form) = ProtoDisplayForm.Pair("canonA", "rawA", null, "rawB");
        Assert.Equal("rawA", a);
        Assert.Equal("rawB", b);
        Assert.Equal(ProtoDisplayForm.Raw, form);
    }

    [Fact]
    public void Pair_CanonA_Empty_PicksRawForBothSides() {
        var (a, b, form) = ProtoDisplayForm.Pair("", "rawA", "canonB", "rawB");
        Assert.Equal("rawA", a);
        Assert.Equal("rawB", b);
        Assert.Equal(ProtoDisplayForm.Raw, form);
    }

    [Fact]
    public void Pair_CanonB_Empty_PicksRawForBothSides() {
        var (a, b, form) = ProtoDisplayForm.Pair("canonA", "rawA", "", "rawB");
        Assert.Equal("rawA", a);
        Assert.Equal("rawB", b);
        Assert.Equal(ProtoDisplayForm.Raw, form);
    }
}
