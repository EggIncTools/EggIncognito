using System.Net;
using EggIncognito.Data.Services;

namespace EggIncognito.Tests;

public class CaptureAddressStoreTests {
    private const string Prefix = "2a01:4f8:c012:e15b::/64";

    [Fact]
    public void RandomInPrefix_IsRandom_NotDeterministic() {
        var a = CaptureAddressStore.RandomInPrefix(Prefix);
        var b = CaptureAddressStore.RandomInPrefix(Prefix);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void RandomInPrefix_HonorsSubPrefixUpperHalf() {
        const string sub = "2a01:4f8:c012:e15b:8000::/65";
        byte[] prefixBytes = IPAddress.Parse("2a01:4f8:c012:e15b::").GetAddressBytes();
        for (int n = 0; n < 50; n++) {
            byte[] bytes = CaptureAddressStore.RandomInPrefix(sub).GetAddressBytes();
            for (int i = 0; i < 8; i++) Assert.Equal(prefixBytes[i], bytes[i]);
            Assert.Equal(0x80, bytes[8] & 0x80);
        }
    }
}
