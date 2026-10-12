using EggIncognito.Controllers;

namespace EggIncognito.Tests.Auth;

public sealed class ApiKeyFeatureRequestTests {
    [Fact]
    public void NullNote_IsFine() => Assert.Null(ApiKeysController.NoteError(null));

    [Fact]
    public void LongNote_IsRejected() =>
        Assert.Equal("note is limited to 500 characters", ApiKeysController.NoteError(new string('a', 501)));
}
