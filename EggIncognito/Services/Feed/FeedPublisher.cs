namespace EggIncognito.Services.Feed;

public sealed class FeedPublisher(IServiceScopeFactory scopes, IConfiguration config, ILogger<FeedPublisher> logger) {
    public string PageUrl(string path) {
        string? configured = config["Feed:PageBaseUrl"];
        string root = string.IsNullOrEmpty(configured) ? FeedDispatcher.DefaultPageBaseUrl : configured.TrimEnd('/');
        return $"{root}/{path.TrimStart('/')}";
    }

    public string ProtoPageUrl(string platform, string build) =>
        FeedDispatcher.BuildPageUrl(config["Feed:PageBaseUrl"], platform, build);

    public void Publish(INotificationEvent evt) => _ = PublishAsync(evt, CancellationToken.None);

    public async Task PublishAsync(INotificationEvent evt, CancellationToken ct) {
        try {
            using var scope = scopes.CreateScope();
            if (scope.ServiceProvider.GetService<FeedDispatcher>() is not { } dispatcher) return;
            await dispatcher.DispatchAsync(evt, ct);
        } catch (Exception ex) {
            logger.LogWarning(ex, "feed publish of {Kind} {Summary} threw", evt.Kind, evt.Summary);
        }
    }
}
