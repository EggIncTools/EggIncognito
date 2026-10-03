using Bunit;
using EggIdentity.Auth;
using EggIdentity.Contract;
using EggIdentity.UI;
using EggIncognito.Components.Protos;
using EggIncognito.Services.Notifications;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace EggIncognito.Tests;

public class NotificationsAdminScopeTests : BunitContext {
    private const string CreateForm = "input[aria-label=\"Discord webhook URL\"]";

    private void Wire() {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<ICurrentUser>(new FakeUser(Guid.NewGuid(), UserRole.Admin, DiscordId: "tester").Accessor());
        Services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor());
        Services.AddSingleton<IWebHostEnvironment>(new FakeWebHostEnvironment());
        Services.AddScoped<NotificationsWorkbenchState>();
        Services.AddEggIdentityToasts();
        Services.AddHttpClient();
    }

    [Fact]
    public async Task AdminScope_HidesCreateUi() {
        Wire();
        var cut = Render<NotificationsWorkbenchModal>(p => p.Add(c => c.AdminScope, true));
        await cut.InvokeAsync(() => cut.Instance.Open());
        cut.WaitForElement(".wb-body");
        Assert.Empty(cut.FindAll(CreateForm));
        Assert.DoesNotContain("New notification", cut.Markup);
    }

    [Fact]
    public async Task SelfScope_KeepsCreateUi() {
        Wire();
        var cut = Render<NotificationsWorkbenchModal>(p => p.Add(c => c.AdminScope, false));
        await cut.InvokeAsync(() => cut.Instance.Open());
        cut.WaitForElement(".wb-body");
        Assert.NotEmpty(cut.FindAll(CreateForm));
        Assert.Contains("New notification", cut.Markup);
    }

}
