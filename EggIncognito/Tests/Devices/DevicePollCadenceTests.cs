using EggIncognito.Data.Services;
using EggIncognito.Services.Admin;
using EggIncognito.Services.Devices;

namespace EggIncognito.Tests.Devices;

public class DevicePollCadenceTests {
    [Fact]
    public void ListenerBackoffGrowsThenCaps() {
        Assert.Equal(TimeSpan.FromSeconds(2), PgChangeListener.BackoffFor(1));
        Assert.Equal(TimeSpan.FromSeconds(4), PgChangeListener.BackoffFor(2));
        Assert.Equal(TimeSpan.FromSeconds(30), PgChangeListener.BackoffFor(9));
    }

    [Fact]
    public void ListenerSubscribesToEveryChangeChannel() {
        Assert.Contains(PgChannels.DeviceJobs, PgChangeListener.ListenSql, StringComparison.Ordinal);
        Assert.Contains(PgChannels.Apks, PgChangeListener.ListenSql, StringComparison.Ordinal);
        Assert.Contains(PgChannels.ProtoRegistry, PgChangeListener.ListenSql, StringComparison.Ordinal);
        Assert.Contains(PgChannels.StagedProtos, PgChangeListener.ListenSql, StringComparison.Ordinal);
    }

    [Fact]
    public void ListenerConnectionIsUnpooled() =>
        Assert.Contains("Pooling=False",
            PgChangeListener.ListenConnectionString("Host=h;Database=d;Username=u;Password=p"),
            StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void NotifyPayloadIsClamped() =>
        Assert.Equal(PgNotify.MaxPayload, PgNotify.Clamp(new string('x', PgNotify.MaxPayload + 100)).Length);

    [Fact]
    public void FleetWatchesContainerLifecycleActions() {
        Assert.True(DockerEventWatcher.IsWatched("die"));
        Assert.True(DockerEventWatcher.IsWatched("destroy"));
        Assert.True(DockerEventWatcher.IsWatched("health_status: unhealthy"));
        Assert.False(DockerEventWatcher.IsWatched("exec_start: id"));
    }

    [Fact]
    public void FleetReconnectBackoffGrowsThenCaps() {
        Assert.Equal(TimeSpan.FromSeconds(4), DockerEventWatcher.NextBackoff(TimeSpan.FromSeconds(2)));
        Assert.Equal(TimeSpan.FromMinutes(2), DockerEventWatcher.NextBackoff(TimeSpan.FromMinutes(90)));
    }
}
