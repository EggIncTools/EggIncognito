using System.Threading.Channels;

namespace EggIncognito.Services.EggSharpSender;

public sealed class EggSharpDataPusher(IServiceScopeFactory scopes, ILogger<EggSharpDataPusher> logger) : BackgroundService {
    private readonly Channel<bool> _requests = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    public DataPushOutcome? Last { get; private set; }

    public void Request() => _requests.Writer.TryWrite(true);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        await foreach (bool _ in _requests.Reader.ReadAllAsync(stoppingToken)) {
            try {
                await using var scope = scopes.CreateAsyncScope();
                var sender = scope.ServiceProvider.GetRequiredService<EggSharpDataSender>();
                if (!sender.Enabled) continue;
                Last = await sender.PushAsync(stoppingToken);
                logger.LogInformation("EggSharp data push {Status} {Sha}: {Detail}", Last.Status, Last.Sha, Last.Detail);
            } catch (Exception ex) when (ex is not OperationCanceledException) {
                logger.LogWarning(ex, "EggSharp data push failed");
            }
        }
    }
}
