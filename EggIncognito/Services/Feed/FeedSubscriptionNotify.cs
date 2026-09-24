using EggIncognito.Services.Admin;

namespace EggIncognito.Services.Feed;

public static class FeedSubscriptionNotify {
    public static void Changed(AdminNotifier? notifier) => notifier?.Publish(AdminTopics.Notifications);
}
