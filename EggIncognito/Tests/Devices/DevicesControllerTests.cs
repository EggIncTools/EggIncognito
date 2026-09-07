using EggIdentity.Contract;
using EggIncognito.Controllers;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Services;
using EggIncognito.Services.Devices;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace EggIncognito.Tests.Devices;

public class DevicesControllerTests {
    private static DevicesController Make(UserRole role, IServiceProvider sp) =>
        new(new FakeUser(role), sp,
            sp.GetService<IServiceScopeFactory>() ?? new ServiceCollection().BuildServiceProvider()
                .GetRequiredService<IServiceScopeFactory>()) {
            ControllerContext = new ControllerContext {
                HttpContext = new DefaultHttpContext { RequestServices = sp }
            }
        };

    [Fact]
    public async Task Refresh_NonAdmin_403() {
        var sp = new ServiceCollection().BuildServiceProvider();
        var c = Make(UserRole.Contributor, sp);
        var r = await c.Refresh("frame-android");
        var sc = Assert.IsType<ObjectResult>(r);
        Assert.Equal(403, sc.StatusCode);
    }

    [Fact]
    public async Task Refresh_Admin_NoDb_503() {
        var sp = new ServiceCollection().BuildServiceProvider();
        var c = Make(UserRole.Admin, sp);
        var r = await c.Refresh("frame-android");
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
        var r = await c.Refresh("frame-android");
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
        var r = await c.Refresh("unknown-device");
        var nf = Assert.IsType<NotFoundObjectResult>(r);
        Assert.Contains("unknown device", nf.Value!.ToString());
    }

    [Fact]
    public async Task Refresh_Admin_AgentDisabled_FallsBackToDb_503() {
        var sp = new ServiceCollection()
            .AddSingleton<IDeviceAgentClient>(new FakeAgent(false))
            .BuildServiceProvider();
        var c = Make(UserRole.Admin, sp);
        var r = await c.Refresh("frame-android");
        var sc = Assert.IsType<ObjectResult>(r);
        Assert.Equal(503, sc.StatusCode);
    }

    [Fact]
    public async Task RefreshAll_Admin_AgentEnabled_ReturnsAgentCount() {
        var sp = new ServiceCollection()
            .AddSingleton<IDeviceAgentClient>(new FakeAgent())
            .BuildServiceProvider();
        var c = Make(UserRole.Admin, sp);
        var r = await c.RefreshAll();
        var ok = Assert.IsType<OkObjectResult>(r);
        Assert.Contains("3", ok.Value!.ToString());
    }

    [Fact]
    public async Task Status_NoDb_ReturnsEmptyArray() {
        var sp = new ServiceCollection().BuildServiceProvider();
        var c = Make(UserRole.Viewer, sp);
        var r = await c.Status();
        var ok = Assert.IsType<OkObjectResult>(r);
        Assert.NotNull(ok.Value);
    }

    [Fact]
    public async Task CheckUpdate_NonAdmin_403() {
        var sp = new ServiceCollection().BuildServiceProvider();
        var c = Make(UserRole.Contributor, sp);
        var r = await c.CheckUpdate("frame-android");
        var sc = Assert.IsType<ObjectResult>(r);
        Assert.Equal(403, sc.StatusCode);
    }

    [Fact]
    public async Task CheckUpdate_Admin_NoDb_503() {
        var sp = new ServiceCollection().BuildServiceProvider();
        var c = Make(UserRole.Admin, sp);
        var r = await c.CheckUpdate("frame-android");
        var sc = Assert.IsType<ObjectResult>(r);
        Assert.Equal(503, sc.StatusCode);
    }

    [Fact]
    public async Task JobHistory_NonAdmin_403() {
        var sp = new ServiceCollection().BuildServiceProvider();
        var c = Make(UserRole.Viewer, sp);
        var r = await c.JobHistory("frame-android");
        var sc = Assert.IsType<ObjectResult>(r);
        Assert.Equal(403, sc.StatusCode);
    }

    [Fact]
    public async Task JobHistory_Admin_NoDb_503() {
        var sp = new ServiceCollection().BuildServiceProvider();
        var c = Make(UserRole.Admin, sp);
        var r = await c.JobHistory("frame-android");
        var sc = Assert.IsType<ObjectResult>(r);
        Assert.Equal(503, sc.StatusCode);
    }

    [Fact]
    public async Task LiveJobs_NonAdmin_403() {
        var sp = new ServiceCollection().BuildServiceProvider();
        var c = Make(UserRole.Viewer, sp);
        var r = await c.LiveJobs(CancellationToken.None);
        var sc = Assert.IsType<ObjectResult>(r);
        Assert.Equal(403, sc.StatusCode);
    }

    [Fact]
    public async Task LiveJobs_Admin_NoDb_ReturnsEmptyArray() {
        var sp = new ServiceCollection().BuildServiceProvider();
        var c = Make(UserRole.Admin, sp);
        var r = await c.LiveJobs(CancellationToken.None);
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

    private sealed class FakeUser(UserRole role) : ICurrentUser {
        public bool IsAuthenticated => true;
        public Guid? UserId => null;
        public string? DiscordId => "123";
        public string? Username => "tester";
        public string? Avatar => null;
        public string? AvatarUrl => null;
        public UserRole Role => role;
        public bool IsSupporter => false;
        public bool IsAtLeast(UserRole need) => UserRoles.IsAtLeast(Role, need);
    }
}
