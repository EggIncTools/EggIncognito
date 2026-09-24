using EggIncognito.Components.Shared;
using EggIncognito.Services.Admin;
using Microsoft.AspNetCore.Components;

namespace EggIncognito.Components.Admin;

public abstract class AdminPaneBase : BrowserApiComponentBase {
    private IDisposable? _sub;

    [Inject] protected AdminNotifier Notifier { get; set; } = null!;
    [Inject] protected AdminWorkbenchState State { get; set; } = null!;
    [Inject] protected IServiceProvider Services { get; set; } = null!;

    protected abstract IReadOnlyList<string> Topics { get; }

    protected abstract Task LoadAsync();

    protected override async Task OnApiReadyAsync() {
        _sub = Notifier.Subscribe(OnTopic);
        await LoadAsync();
    }

    private void OnTopic(string topic) {
        if (!Topics.Contains(topic)) return;
        _ = InvokeAsync(async () => {
            await LoadAsync();
            StateHasChanged();
        });
    }

    public override async ValueTask DisposeAsync() {
        _sub?.Dispose();
        await base.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
