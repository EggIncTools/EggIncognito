using EggIncognito.Data.Services;

namespace EggIncognito.Services.Admin;

public sealed class ApkChangeNotifier(AdminNotifier notifier) : IApkStoreObserver {
    public Task OnChangedAsync(ApkStoreNotice notice, CancellationToken ct) {
        notifier.Publish(AdminTopics.Apks);
        return Task.CompletedTask;
    }
}
