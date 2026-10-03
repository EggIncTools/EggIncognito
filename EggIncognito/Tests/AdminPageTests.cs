using Bunit;
using EggIdentity.Auth;
using EggIdentity.Contract;
using EggIdentity.UI;
using EggIncognito.Components.Admin;
using EggIncognito.Services;
using EggIncognito.Services.Admin;
using EggIncognito.Services.Devices;
using EggIncognito.Services.Notifications;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace EggIncognito.Tests;

public class AdminPageTests {
    public class Component : BunitContext {
        private void Wire(UserRole role) {
            JSInterop.Mode = JSRuntimeMode.Loose;
            Services.AddSingleton<ICurrentUser>(role != UserRole.Viewer
                ? new FakeUser(Guid.NewGuid(), role, DiscordId: "tester").Accessor()
                : new AnonymousUser());
            Services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor());
            Services.AddSingleton<IWebHostEnvironment>(new FakeWebHostEnvironment());
            Services.AddSingleton(new AuthState(false));
            Services.AddScoped<DeviceWorkbenchState>();
            Services.AddScoped<NotificationsWorkbenchState>();
            Services.AddScoped<AdminWorkbenchState>();
            Services.AddEggIdentityToasts();
            Services.AddSingleton<AdminNotifier>();
            Services.AddHttpClient();
        }

        [Fact]
        public void Closed_RendersNothing() {
            Wire(UserRole.Admin);
            var cut = Render<AdminWorkbenchModal>();
            Assert.Empty(cut.FindAll(".wb-body"));
        }

        [Fact]
        public void NonAdmin_CannotOpen() {
            Wire(UserRole.Viewer);
            var cut = Render<AdminWorkbenchModal>();
            cut.InvokeAsync(() => cut.Instance.Open());
            Assert.Empty(cut.FindAll(".wb-body"));
        }

        [Fact]
        public void Admin_Open_ShowsRailAndPanes() {
            Wire(UserRole.Admin);
            var cut = Render<AdminWorkbenchModal>();
            cut.InvokeAsync(() => cut.Instance.Open());
            Assert.NotNull(cut.Find("#adminWorkbench"));
            Assert.Equal(4, cut.FindAll(".wb-sec").Count);
            Assert.Equal(AdminPanes.All.Count, cut.FindAll(".wb-entry").Count);
            Assert.Contains("Users", cut.Markup);
        }

        [Fact]
        public void Admin_OpenPane_MarksItVisited() {
            Wire(UserRole.Admin);
            var cut = Render<AdminWorkbenchModal>();
            cut.InvokeAsync(() => cut.Instance.OpenPane(AdminPanes.Sessions));
            var state = Services.GetRequiredService<AdminWorkbenchState>();
            Assert.Equal(AdminPanes.Sessions, state.SelectedPane);
            Assert.Contains(AdminPanes.Sessions, state.Visited);
        }
    }

}
