using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Tests.Devices;

public class DeviceShellTempPathTests {
    [Fact]
    public void IsTempPath_NewTempPath_IsTrue() =>
        Assert.True(DeviceShell.IsTempPath(DeviceShell.NewTempPath(".apk")));

    [Fact]
    public void IsTempPath_TempFileWithoutThePrefix_IsFalse() =>
        Assert.False(DeviceShell.IsTempPath(Path.Combine(Path.GetTempPath(), "other.apk")));

    [Fact]
    public void IsTempPath_RepoPath_IsFalse() =>
        Assert.False(DeviceShell.IsTempPath(Path.Combine(AppContext.BaseDirectory, "egi-thing.apk")));

    [Theory]
    [InlineData("")]
    [InlineData("/sdcard/egi-thing.apk")]
    [InlineData("shell")]
    public void IsTempPath_ForeignArgs_AreFalse(string arg) => Assert.False(DeviceShell.IsTempPath(arg));
}
