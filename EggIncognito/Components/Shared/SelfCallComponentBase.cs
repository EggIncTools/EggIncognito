using EggIdentity.UI.Suite;
using EggIncognito.Services;
using Microsoft.AspNetCore.Components;

namespace EggIncognito.Components.Shared;

public abstract class SelfCallComponentBase : ComponentBase {
    [Inject] protected IHttpClientFactory HttpFactory { get; set; } = null!;
    [Inject] protected IHttpContextAccessor HttpContextAccessor { get; set; } = null!;

    protected string SelfBaseAddress { get; private set; } = "";
    protected string? SelfCookie { get; private set; }

    protected override void OnInitialized() {
        var req = HttpContextAccessor.HttpContext?.Request;
        if (req is not null) {
            SelfBaseAddress = $"{req.Scheme}://{req.Host}";
            SelfCookie = req.Headers.Cookie;
        }
    }

    protected bool Speculative => Speculation.IsSpeculative(HttpContextAccessor.HttpContext);

    protected HttpClient Client() => SelfCallClient.Create(HttpFactory, SelfBaseAddress, SelfCookie);
}
