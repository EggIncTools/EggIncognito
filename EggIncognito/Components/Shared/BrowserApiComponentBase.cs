namespace EggIncognito.Components.Shared;

public abstract class BrowserApiComponentBase : SelfCallComponentBase, IAsyncDisposable {
    protected BrowserApi? Api { get; private set; }

    public virtual ValueTask DisposeAsync() {
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    protected virtual Task OnApiReadyAsync() => Task.CompletedTask;

    protected override void OnInitialized() {
        base.OnInitialized();
        Api = new BrowserApi(Client);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender) {
        if (!firstRender) return;
        await OnApiReadyAsync();
        StateHasChanged();
    }
}
