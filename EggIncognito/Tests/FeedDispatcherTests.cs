using System.Net;
using EggIncognito.Core.Services.ProtoExtract;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Services.DataApi;
using EggIncognito.Services.Feed;
using Microsoft.Extensions.Logging.Abstractions;

namespace EggIncognito.Tests;

public class FeedDispatcherTests {
    private static FeedDispatcher Dispatcher(FakeStore store, HttpMessageHandler handler) =>
        new(store, new StubHttpFactory(handler), NullLogger<FeedDispatcher>.Instance, TimeProvider.System);

    private static FeedSubscription Sub(int id, string trigger, params string[] platforms) => new() {
        Id = id,
        Kind = "discord",
        EventKind = "proto_build",
        TargetUrl = "https://discord.com/api/webhooks/1/abc",
        Trigger = trigger,
        Platforms = platforms,
        Active = true
    };

    private static FeedSubscription KindSub(int id, string eventKind, string trigger) => new() {
        Id = id,
        Kind = "discord",
        EventKind = eventKind,
        TargetUrl = "https://discord.com/api/webhooks/1/abc",
        Trigger = trigger,
        Platforms = [],
        Active = true
    };

    private static FeedSubscription ConfigSub(int id, string trigger) =>
        KindSub(id, FeedEventKinds.ConfigChanged, trigger);

    private static ConfigChangedEvent ConfigEvt(string feed, string sha) =>
        new(feed, sha, "https://x/periodicals");

    private static ProtoBuildEvent ProtoEvt(bool protoChanged, string platform = "android", int id = 7) =>
        new(id, platform, "1.0", "111343", "72", "sha", true, protoChanged, "https://x/y");

    [Fact]
    public async Task ProtoChanged_Fires_OnChange() {
        var store = new FakeStore(Sub(1, "proto_changed", "android"));
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        await Dispatcher(store, handler).DispatchAsync(ProtoEvt(true));

        Assert.Equal(1, handler.Posts);
        Assert.Single(store.Deliveries);
        Assert.Equal("sent", store.Deliveries[0].Status);
    }

    [Fact]
    public async Task ProtoChanged_Skipped_WhenUnchanged() {
        var store = new FakeStore(Sub(1, "proto_changed", "android"));
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        await Dispatcher(store, handler).DispatchAsync(ProtoEvt(false));

        Assert.Equal(0, handler.Posts);
        Assert.Empty(store.Deliveries);
    }

    [Fact]
    public async Task Gone410_Deactivates() {
        var store = new FakeStore(Sub(1, "new_version", "android"));
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Gone));
        await Dispatcher(store, handler).DispatchAsync(ProtoEvt(false));

        Assert.False(store.Subs[0].Active);
        Assert.Equal("failed", store.Deliveries[0].Status);
    }

    [Fact]
    public async Task Idempotent_SecondEvent_NoSecondDelivery() {
        var store = new FakeStore(Sub(1, "new_version", "android"));
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var d = Dispatcher(store, handler);
        await d.DispatchAsync(ProtoEvt(false));
        await d.DispatchAsync(ProtoEvt(false));

        Assert.Equal(1, handler.Posts);
        Assert.Single(store.Deliveries);
    }

    [Fact]
    public async Task CustomTemplate_SendsRenderedContent_NotEmbed() {
        var sub = Sub(1, "proto_changed", "android");
        sub.MessageTemplate = "New build {{appVersion}} ({{build}}) on {{platform}}: {{protoChanged}}";
        var store = new FakeStore(sub);
        string? sentBody = null;
        var handler = new StubHandler(req => {
            sentBody = req.Content!.ReadAsStringAsync().Result;
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        await Dispatcher(store, handler).DispatchAsync(ProtoEvt(true));

        Assert.Contains("New build 1.0 (111343) on android: changed", sentBody);
        Assert.DoesNotContain("embeds", sentBody);
    }

    [Fact]
    public async Task WrongKind_Subscription_NotFired() {
        var store = new FakeStore(ConfigSub(1, FeedEventKinds.TriggerAnyFeed));
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        await Dispatcher(store, handler).DispatchAsync(ProtoEvt(true));

        Assert.Equal(0, handler.Posts);
        Assert.Empty(store.Deliveries);
    }

    [Fact]
    public async Task Config_Any_Fires_ProtoSubIgnored() {
        var store = new FakeStore(
            ConfigSub(1, FeedEventKinds.TriggerAnyFeed), Sub(2, "proto_changed", "android"));
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        await Dispatcher(store, handler).DispatchAsync(ConfigEvt(ConfigFeeds.Periodicals, "abc123"));

        Assert.Equal(1, handler.Posts);
        Assert.Single(store.Deliveries);
        Assert.Equal(FeedEventKinds.ConfigChanged, store.Deliveries[0].EventKind);
        Assert.Equal("periodicals:abc123", store.Deliveries[0].DedupKey);
    }

    [Fact]
    public async Task LegacyPeriodicalsKind_StillMatchesConfigEvents() {
        var store = new FakeStore(KindSub(1, FeedEventKinds.LegacyPeriodicalsChanged, ConfigFeeds.Periodicals));
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        await Dispatcher(store, handler).DispatchAsync(ConfigEvt(ConfigFeeds.Periodicals, "legacy"));

        Assert.Equal(1, handler.Posts);
    }

    [Fact]
    public async Task Config_FeedTrigger_MatchesOnlyThatFeed() {
        var store = new FakeStore(ConfigSub(1, ConfigFeeds.Afx));
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var d = Dispatcher(store, handler);
        await d.DispatchAsync(ConfigEvt(ConfigFeeds.Periodicals, "h1"));
        Assert.Equal(0, handler.Posts);
        await d.DispatchAsync(ConfigEvt(ConfigFeeds.Afx, "h2"));
        Assert.Equal(1, handler.Posts);
    }

    [Fact]
    public async Task Config_SameContentHash_Deduped() {
        var store = new FakeStore(ConfigSub(1, FeedEventKinds.TriggerAnyFeed));
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var d = Dispatcher(store, handler);
        await d.DispatchAsync(ConfigEvt(ConfigFeeds.Periodicals, "samehash"));
        await d.DispatchAsync(ConfigEvt(ConfigFeeds.Periodicals, "samehash"));

        Assert.Equal(1, handler.Posts);
        await d.DispatchAsync(ConfigEvt(ConfigFeeds.Periodicals, "newhash"));
        Assert.Equal(2, handler.Posts);
    }

    [Fact]
    public async Task Config_DedupsOnAspects_NotFixtureHash() {
        var store = new FakeStore(ConfigSub(1, FeedEventKinds.TriggerAnyFeed));
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var d = Dispatcher(store, handler);
        var change = new ConfigChangeSummary(["events"], [], []);
        await d.DispatchAsync(new ConfigChangedEvent(
            ConfigFeeds.Periodicals, "fixture1", "https://x/periodicals", change, "aspects"));
        await d.DispatchAsync(new ConfigChangedEvent(
            ConfigFeeds.Periodicals, "fixture2", "https://x/periodicals", change, "aspects"));

        Assert.Equal(1, handler.Posts);
    }

    [Fact]
    public async Task Config_RequireAspects_BlocksUncharacterisedChange() {
        var sub = ConfigSub(1, FeedEventKinds.TriggerAnyFeed);
        sub.Filters = [FeedEventKinds.FilterRequireAspects];
        var store = new FakeStore(sub);
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        await Dispatcher(store, handler).DispatchAsync(ConfigEvt(ConfigFeeds.Periodicals, "bare"));

        Assert.Equal(0, handler.Posts);
        var blocked = Assert.Single(store.Suppressions);
        Assert.Contains(FeedEventKinds.FilterRequireAspects, blocked.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GameData_AnyRebuild_Fires_BinaryUp_Filters() {
        var store = new FakeStore(
            KindSub(1, FeedEventKinds.GameDataRebuilt, FeedEventKinds.TriggerBinaryUp),
            KindSub(2, FeedEventKinds.GameDataRebuilt, FeedEventKinds.TriggerAnyRebuild));
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        await Dispatcher(store, handler).DispatchAsync(new GameDataRebuiltEvent(
            "1.37.0", "1.37.0", "android", "sha-same", ["eggs"], "https://x/data"));

        Assert.Equal(1, handler.Posts);
        Assert.Single(store.Deliveries);
        Assert.Equal(2, store.Deliveries[0].SubscriptionId);
    }

    [Fact]
    public async Task GameData_NoChangedDocs_NeverFires() {
        var store = new FakeStore(KindSub(1, FeedEventKinds.GameDataRebuilt, FeedEventKinds.TriggerAnyRebuild));
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        await Dispatcher(store, handler).DispatchAsync(new GameDataRebuiltEvent(
            "1.37.0", "1.36.4", "android", "sha", [], "https://x/data"));

        Assert.Equal(0, handler.Posts);
        Assert.Empty(store.Deliveries);
    }

    private static FeedSubscription Guarded(int id, string trigger, params string[] platforms) {
        var sub = Sub(id, trigger, platforms);
        sub.Filters = [
            FeedEventKinds.FilterRequireClientVersion, FeedEventKinds.FilterRequireProto,
            FeedEventKinds.FilterSaneBuild, FeedEventKinds.FilterKnownDelta
        ];
        return sub;
    }

    private static ProtoBuildEvent BrokenIosEvt(int id = 42) {
        var flaws = ProtoVersionQuality.Flaws("ios", "111340", null, "", false);
        return new ProtoBuildEvent(id, "ios", "1.36.4", "111340", null, "", true, true, "https://x/y",
            VersionDelta.Unknown, "1.37.0", "1.37.0.1", flaws);
    }

    [Fact]
    public async Task VersionUp_Fires_OnForward_Only() {
        var store = new FakeStore(Sub(1, FeedEventKinds.TriggerVersionUp, "android"));
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var d = Dispatcher(store, handler);

        await d.DispatchAsync(ProtoEvt(true) with { Delta = VersionDelta.Backfill, ProtoVersionId = 1 });
        Assert.Equal(0, handler.Posts);

        await d.DispatchAsync(ProtoEvt(true) with { Delta = VersionDelta.Forward, ProtoVersionId = 2 });
        Assert.Equal(1, handler.Posts);
    }

    [Fact]
    public async Task Filters_Block_Flawed_Event_And_Record_Reason() {
        var store = new FakeStore(Guarded(1, FeedEventKinds.TriggerNewVersion, "ios"));
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        await Dispatcher(store, handler).DispatchAsync(BrokenIosEvt());

        Assert.Equal(0, handler.Posts);
        Assert.Empty(store.Deliveries);
        var blocked = Assert.Single(store.Suppressions);
        Assert.Contains(FeedEventKinds.FilterRequireClientVersion, blocked.Reason, StringComparison.Ordinal);
        Assert.Contains(FeedEventKinds.FilterSaneBuild, blocked.Reason, StringComparison.Ordinal);
        Assert.Contains(FeedEventKinds.FilterKnownDelta, blocked.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Suspect_Fires_OnFlawed_NotOnClean() {
        var store = new FakeStore(Guarded(1, FeedEventKinds.TriggerSuspect, "android", "ios"));
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var d = Dispatcher(store, handler);

        await d.DispatchAsync(ProtoEvt(true, id: 5) with { Delta = VersionDelta.Forward, Flaws = [] });
        Assert.Equal(0, handler.Posts);

        await d.DispatchAsync(BrokenIosEvt());
        Assert.Equal(1, handler.Posts);
        Assert.Empty(store.Suppressions);
    }

    [Fact]
    public async Task Clean_Forward_Event_Passes_Filters() {
        var store = new FakeStore(Guarded(1, FeedEventKinds.TriggerVersionUp, "android"));
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        await Dispatcher(store, handler).DispatchAsync(
            ProtoEvt(true) with { Delta = VersionDelta.Forward, Flaws = [] });

        Assert.Equal(1, handler.Posts);
        Assert.Empty(store.Suppressions);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler {
        public int Posts { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) {
            Posts++;
            return Task.FromResult(respond(request));
        }
    }

    private sealed class FakeStore(params FeedSubscription[] subs) : IFeedSubscriptionStore {
        public List<FeedSubscription> Subs { get; } = [.. subs];
        public List<FeedDelivery> Deliveries { get; } = [];
        public List<FeedSuppression> Suppressions { get; } = [];

        public Task<FeedSubscription> AddAsync(FeedSubscription sub, CancellationToken ct = default) {
            Subs.Add(sub);
            return Task.FromResult(sub);
        }

        public Task<List<FeedSubscription>> ActiveAsync(CancellationToken ct = default) =>
            Task.FromResult(Subs.Where(s => s.Active).ToList());

        public Task<bool> AlreadyDeliveredAsync(int subId, string eventKind, string dedupKey,
            CancellationToken ct = default) =>
            Task.FromResult(Deliveries.Any(d =>
                d.SubscriptionId == subId && d.EventKind == eventKind && d.DedupKey == dedupKey));

        public Task RecordAsync(FeedDelivery delivery, CancellationToken ct = default) {
            Deliveries.Add(delivery);
            return Task.CompletedTask;
        }

        public Task SetActiveAsync(int subId, bool active, CancellationToken ct = default) {
            var s = Subs.FirstOrDefault(x => x.Id == subId);
            s?.Active = active;
            return Task.CompletedTask;
        }

        public Task BumpFailAsync(int subId, CancellationToken ct = default) {
            var s = Subs.FirstOrDefault(x => x.Id == subId);
            if (s is not null) s.FailCount++;
            return Task.CompletedTask;
        }

        public Task MarkDeliveredAsync(int subId, DateTimeOffset at, CancellationToken ct = default) {
            var s = Subs.FirstOrDefault(x => x.Id == subId);
            if (s is not null) {
                s.LastDeliveryAt = at;
                s.FailCount = 0;
            }

            return Task.CompletedTask;
        }

        public Task<List<FeedSubscription>> ByOwnerAsync(Guid ownerUserId, CancellationToken ct = default) =>
            Task.FromResult(Subs.Where(s => s.OwnerUserId == ownerUserId)
                .OrderByDescending(s => s.CreatedAt).ToList());

        public Task<List<FeedSubscription>> AllForAdminAsync(CancellationToken ct = default) =>
            Task.FromResult(Subs.OrderByDescending(s => s.CreatedAt).ToList());

        public Task<FeedSubscription?> AdminByIdAsync(int id, CancellationToken ct = default) =>
            Task.FromResult(Subs.FirstOrDefault(x => x.Id == id));

        public Task<bool> AdminDeactivateAsync(int id, CancellationToken ct = default) {
            var s = Subs.FirstOrDefault(x => x.Id == id);
            if (s is null) return Task.FromResult(false);
            s.Active = false;
            return Task.FromResult(true);
        }

        public Task<bool> AdminDeleteAsync(int id, CancellationToken ct = default) {
            var s = Subs.FirstOrDefault(x => x.Id == id);
            if (s is null) return Task.FromResult(false);
            Subs.Remove(s);
            return Task.FromResult(true);
        }

        public Task<bool> DeleteAsync(int id, Guid ownerUserId, CancellationToken ct = default) {
            var s = Subs.FirstOrDefault(x => x.Id == id && x.OwnerUserId == ownerUserId);
            if (s is null) return Task.FromResult(false);
            Subs.Remove(s);
            return Task.FromResult(true);
        }

        public Task<bool> UpdateAsync(int id, Guid ownerUserId, string[] platforms, string trigger,
            bool active, string? messageTemplate, string[] filters, CancellationToken ct = default) {
            var s = Subs.FirstOrDefault(x => x.Id == id && x.OwnerUserId == ownerUserId);
            if (s is null) return Task.FromResult(false);
            s.Platforms = platforms;
            s.Trigger = trigger;
            s.Active = active;
            s.MessageTemplate = messageTemplate;
            s.Filters = filters;
            return Task.FromResult(true);
        }

        public Task SuppressAsync(int subId, string eventKind, string dedupKey, string reason, string? summary,
            CancellationToken ct = default) {
            Suppressions.Add(new FeedSuppression {
                SubscriptionId = subId,
                EventKind = eventKind,
                DedupKey = dedupKey,
                Reason = reason,
                Summary = summary
            });
            return Task.CompletedTask;
        }
    }
}
