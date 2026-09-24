using EggIdentity.Contract;
using EggIncognito.Controllers;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Services.Devices;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace EggIncognito.Tests.Devices;

public class DevicesControllerTests {
    private static DevicesController Make(UserRole role, IServiceProvider sp) =>
        new(new FakeUser(role: role, discordId: "123"), sp,
            sp.GetService<IServiceScopeFactory>() ?? new ServiceCollection().BuildServiceProvider()
                .GetRequiredService<IServiceScopeFactory>(),
            NullLogger<DevicesController>.Instance) {
            ControllerContext = new ControllerContext {
                HttpContext = new DefaultHttpContext { RequestServices = sp }
            }
        };

    [Fact]
    public async Task Refresh_Admin_NoDb_503() {
        var sp = new ServiceCollection().BuildServiceProvider();
        var c = Make(UserRole.Admin, sp);
        var r = await c.Refresh("frame-android", null, null, null, null, new DevicePlatforms([]), TimeProvider.System);
        var sc = Assert.IsType<ObjectResult>(r);
        Assert.Equal(503, sc.StatusCode);
    }

    [Fact]
    public async Task Refresh_Admin_AgentEnabled_ReturnsAgentResult() {
        var sp = new ServiceCollection()
            .AddSingleton<IDeviceAgentClient>(new FakeAgent())
            .AddSingleton<IDeviceStatusStore>(new FakeDeviceStore())
            .BuildServiceProvider();
        var c = Make(UserRole.Admin, sp);
        var r = await c.Refresh("frame-android", sp.GetRequiredService<IDeviceAgentClient>(),
            sp.GetRequiredService<IDeviceStatusStore>(), null, null, new DevicePlatforms([]), TimeProvider.System);
        var ok = Assert.IsType<OkObjectResult>(r);
        Assert.Contains("no_change", ok.Value!.ToString());
    }

    [Fact]
    public async Task Refresh_Admin_AgentEnabled_UnknownDevice_404() {
        var sp = new ServiceCollection()
            .AddSingleton<IDeviceAgentClient>(new FakeAgent())
            .AddSingleton<IDeviceStatusStore>(new FakeDeviceStore())
            .BuildServiceProvider();
        var c = Make(UserRole.Admin, sp);
        var r = await c.Refresh("unknown-device", sp.GetRequiredService<IDeviceAgentClient>(),
            sp.GetRequiredService<IDeviceStatusStore>(), null, null, new DevicePlatforms([]), TimeProvider.System);
        var nf = Assert.IsType<ObjectResult>(r);
        Assert.Equal(404, nf.StatusCode);
    }

    [Fact]
    public async Task Refresh_Admin_AgentDisabled_FallsBackToDb_503() {
        var sp = new ServiceCollection()
            .AddSingleton<IDeviceAgentClient>(new FakeAgent(false))
            .BuildServiceProvider();
        var c = Make(UserRole.Admin, sp);
        var r = await c.Refresh("frame-android", sp.GetRequiredService<IDeviceAgentClient>(), null, null, null,
            new DevicePlatforms([]), TimeProvider.System);
        var sc = Assert.IsType<ObjectResult>(r);
        Assert.Equal(503, sc.StatusCode);
    }

    [Fact]
    public async Task RefreshAll_Admin_AgentEnabled_ReturnsAgentCount() {
        var sp = new ServiceCollection()
            .AddSingleton<IDeviceAgentClient>(new FakeAgent())
            .BuildServiceProvider();
        var c = Make(UserRole.Admin, sp);
        var r = await c.RefreshAll(sp.GetRequiredService<IDeviceAgentClient>(), null, null, null,
            new DevicePlatforms([]), TimeProvider.System);
        var ok = Assert.IsType<OkObjectResult>(r);
        Assert.Contains("3", ok.Value!.ToString());
    }

    [Fact]
    public async Task Status_NoDb_ReturnsEmptyArray() {
        var sp = new ServiceCollection().BuildServiceProvider();
        var c = Make(UserRole.Viewer, sp);
        var r = await c.Status(null, null, null, null, null, null, null, null);
        var ok = Assert.IsType<OkObjectResult>(r);
        Assert.NotNull(ok.Value);
    }

    [Fact]
    public async Task LiveJobs_Admin_NoDb_ReturnsEmptyArray() {
        var sp = new ServiceCollection().BuildServiceProvider();
        var c = Make(UserRole.Admin, sp);
        var r = await c.LiveJobs(null, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(r);
        Assert.NotNull(ok.Value);
    }

    private sealed class FakeAgent(bool enabled = true) : IDeviceAgentClient {
        public bool Enabled => enabled;

        public Task<DeviceProbeDto?> ProbeAsync(string id, CancellationToken ct) =>
            Task.FromResult<DeviceProbeDto?>(
                new DeviceProbeDto(id, true, "1.36", "100", "1.36", "no_change", null, DateTimeOffset.UnixEpoch));

        public Task<int> ProbeAllAsync(CancellationToken ct) => Task.FromResult(3);
        public Task<bool> PokeAsync(string? id, bool force, CancellationToken ct) =>
            throw new NotImplementedException();
    }

    private sealed class FakeDeviceStore : IDeviceStatusStore {
        public Task UpsertDeviceAsync(string id, string platform, string label, string target, string package,
            string origin = DeviceOrigins.Runtime, CancellationToken ct = default) => Task.CompletedTask;

        public Task<List<Device>> EnabledDevicesAsync(CancellationToken ct = default) =>
            Task.FromResult(new List<Device>());

        public Task<Device?> GetAsync(string id, CancellationToken ct = default) =>
            Task.FromResult<Device?>(id == "frame-android" ? new Device { Id = id, Platform = "android", Label = id } : null);

        public Task RemoveAsync(string id, CancellationToken ct = default) => Task.CompletedTask;

        public Task SetCapturePortAsync(string id, int port, CancellationToken ct = default) => Task.CompletedTask;
    }
}
