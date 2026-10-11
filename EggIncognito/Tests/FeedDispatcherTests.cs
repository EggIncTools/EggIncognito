using System.Net;
using EggIncognito.Core.Services.ProtoExtract;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Services.Contracts;
using EggIncognito.Services.DataApi;
using EggIncognito.Services.Events;
using EggIncognito.Services.Feed;
using EggIncognito.Services.Feed.Kinds;
using Microsoft.Extensions.Logging.Abstractions;

namespace EggIncognito.Tests;

public class FeedDispatcherTests {
    private static readonly DateTimeOffset Start = new(2026, 10, 12, 16, 0, 0, TimeSpan.Zero);

    private static FeedDispatcher Dispatcher(FakeStore store, HttpMessageHandler handler) =>
        new(store, new StubHttpFactory(handler), NullLogger<FeedDispatcher>.Instance, TimeProvider.System);

    private static StubHttpMessageHandler Ok() =>
        new(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

    private static FeedSubscription Sub(int id, string trigger, params string[] platforms) => new() {
        Id = id,
        Kind = "discord",
        EventKind = "proto_build",
        TargetUrl = "https://discord.com/api/webhooks/1/abc",
        Trigger = trigger,
        Platforms = platforms,
        Active = true
    };

    private static FeedSubscription KindSub(int id, string eventKind, string trigger, params string[] filters) => new() {
        Id = id,
        Kind = "discord",
        EventKind = eventKind,
        TargetUrl = "https://discord.com/api/webhooks/1/abc",
        Trigger = trigger,
        Platforms = [],
        Filters = filters,
        Active = true
    };

    private static FeedSubscription ConfigSub(int id, string trigger, params string[] filters) =>
        KindSub(id, FeedEventKinds.ConfigChanged, trigger, filters);

    private static ConfigChangedEvent ConfigEvt(string feed, string sha) =>
        new(feed, sha, "https://x/periodicals");

    private static ProtoBuildEvent ProtoEvt(bool protoChanged, string platform = "android", int id = 7) =>
        new(id, platform, "1.0", "111343", "72", "sha", true, protoChanged, "https://x/y", VersionDelta.Forward);

    private static GameEventAddedEvent EventEvt(string id, bool ultra, string source, TimeSpan age) =>
        new(id, "boost-sale", "Boost sale", 0.7, ultra, Start, Start.AddHours(72), source, Start + age, "https://x/events");

    private static ContractReleasedEvent ContractEvt(string id, bool leggacy, bool ultra, string source, TimeSpan age) =>
        new(id, "Hab Rush", 6, null, Start, Start.AddDays(10), 864000, leggacy, ultra, 1, 10, source, Start + age,
            "https://x/contracts");

    [Fact]
    public async Task ProtoChanged_Fires_OnChange() {
        var store = new FakeStore(Sub(1, "proto_changed", "android"));
        var handler = Ok();
        await Dispatcher(store, handler).DispatchAsync(ProtoEvt(true));

        Assert.Single(handler.Requests);
        Assert.Single(store.Deliveries);
        Assert.Equal("sent", store.Deliveries[0].Status);
    }

    [Fact]
    public async Task ProtoChanged_Skipped_WhenUnchanged() {
        var store = new FakeStore(Sub(1, "proto_changed", "android"));
        var handler = Ok();
        await Dispatcher(store, handler).DispatchAsync(ProtoEvt(false));

        Assert.Empty(handler.Requests);
        Assert.Empty(store.Deliveries);
    }

    [Fact]
    public async Task PlatformScope_Excludes() {
        var store = new FakeStore(Sub(1, "new_version", "ios"));
        var handler = Ok();
        await Dispatcher(store, handler).DispatchAsync(ProtoEvt(true));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Gone410_Deactivates() {
        var store = new FakeStore(Sub(1, "new_version", "android"));
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Gone));
        await Dispatcher(store, handler).DispatchAsync(ProtoEvt(false));

        Assert.False(store.Subs[0].Active);
        Assert.Equal("failed", store.Deliveries[0].Status);
    }

    [Fact]
    public async Task Idempotent_SecondEvent_NoSecondDelivery() {
        var store = new FakeStore(Sub(1, "new_version", "android"));
        var handler = Ok();
        var d = Dispatcher(store, handler);
        await d.DispatchAsync(ProtoEvt(false));
        await d.DispatchAsync(ProtoEvt(false));

        Assert.Single(handler.Requests);
        Assert.Single(store.Deliveries);
    }

    [Fact]
    public async Task CustomTemplate_SendsRenderedContent_NotEmbed() {
        var sub = Sub(1, "proto_changed", "android");
        sub.MessageTemplate = "New build {{appVersion}} ({{build}}) on {{platform}}: {{protoChanged}}";
        var store = new FakeStore(sub);
        string? sentBody = null;
        var handler = new StubHttpMessageHandler(req => {
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
        var handler = Ok();
        await Dispatcher(store, handler).DispatchAsync(ProtoEvt(true));

        Assert.Empty(handler.Requests);
        Assert.Empty(store.Deliveries);
    }

    [Fact]
    public async Task Config_Any_Fires_ProtoSubIgnored() {
        var store = new FakeStore(
            ConfigSub(1, FeedEventKinds.TriggerAnyFeed), Sub(2, "proto_changed", "android"));
        var handler = Ok();
        await Dispatcher(store, handler).DispatchAsync(ConfigEvt(ConfigFeeds.Periodicals, "abc123"));

        Assert.Single(handler.Requests);
        Assert.Single(store.Deliveries);
        Assert.Equal(FeedEventKinds.ConfigChanged, store.Deliveries[0].EventKind);
        Assert.Equal("periodicals:abc123", store.Deliveries[0].DedupKey);
    }

    [Fact]
    public async Task LegacyPeriodicalsKind_StillMatchesConfigEvents() {
        var store = new FakeStore(KindSub(1, FeedEventKinds.LegacyPeriodicalsChanged, ConfigFeeds.Periodicals));
        var handler = Ok();
        await Dispatcher(store, handler).DispatchAsync(ConfigEvt(ConfigFeeds.Periodicals, "legacy"));

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Config_FeedTrigger_MatchesOnlyThatFeed() {
        var store = new FakeStore(ConfigSub(1, ConfigFeeds.Afx));
        var handler = Ok();
        var d = Dispatcher(store, handler);
        await d.DispatchAsync(ConfigEvt(ConfigFeeds.Periodicals, "h1"));
        Assert.Empty(handler.Requests);
        await d.DispatchAsync(ConfigEvt(ConfigFeeds.Afx, "h2"));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Config_DedupsOnAspects_NotFixtureHash() {
        var store = new FakeStore(ConfigSub(1, FeedEventKinds.TriggerAnyFeed));
        var handler = Ok();
        var d = Dispatcher(store, handler);
        var change = new ConfigChangeSummary(["events"], [], []);
        await d.DispatchAsync(new ConfigChangedEvent(
            ConfigFeeds.Periodicals, "fixture1", "https://x/periodicals", change, "aspects"));
        await d.DispatchAsync(new ConfigChangedEvent(
            ConfigFeeds.Periodicals, "fixture2", "https://x/periodicals", change, "aspects"));

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Config_RequireAspects_BlocksUncharacterisedChange() {
        var store = new FakeStore(
            ConfigSub(1, FeedEventKinds.TriggerAnyFeed, FeedEventKinds.FilterRequireAspects));
        var handler = Ok();
        await Dispatcher(store, handler).DispatchAsync(ConfigEvt(ConfigFeeds.Periodicals, "bare"));

        Assert.Empty(handler.Requests);
        var blocked = Assert.Single(store.Suppressions);
        Assert.Contains(FeedEventKinds.FilterRequireAspects, blocked.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GameData_AnyRebuild_Fires_BinaryUp_Filters() {
        var store = new FakeStore(
            KindSub(1, FeedEventKinds.GameDataRebuilt, FeedEventKinds.TriggerBinaryUp),
            KindSub(2, FeedEventKinds.GameDataRebuilt, FeedEventKinds.TriggerAnyRebuild));
        var handler = Ok();
        await Dispatcher(store, handler).DispatchAsync(new GameDataRebuiltEvent(
            "1.37.0", "1.37.0", "android", "sha-same", ["eggs"], "https://x/data"));

        Assert.Single(handler.Requests);
        Assert.Single(store.Deliveries);
        Assert.Equal(2, store.Deliveries[0].SubscriptionId);
    }

    [Fact]
    public async Task GameData_NoChangedDocs_NeverFires() {
        var store = new FakeStore(KindSub(1, FeedEventKinds.GameDataRebuilt, FeedEventKinds.TriggerAnyRebuild));
        var handler = Ok();
        await Dispatcher(store, handler).DispatchAsync(new GameDataRebuiltEvent(
            "1.37.0", "1.36.4", "android", "sha", [], "https://x/data"));

        Assert.Empty(handler.Requests);
        Assert.Empty(store.Deliveries);
    }

    [Fact]
    public async Task GameEvent_FreshDevice_Fires_OldImport_Suppressed() {
        var store = new FakeStore(KindSub(1, FeedEventKinds.GameEvent, FeedEventKinds.TriggerAny,
            FeedEventKinds.FilterFreshOnly, FeedEventKinds.FilterDeviceOnly));
        var handler = Ok();
        var d = Dispatcher(store, handler);

        await d.DispatchAsync(EventEvt("live", false, GameEventSources.Device, TimeSpan.FromMinutes(5)));
        Assert.Single(handler.Requests);

        await d.DispatchAsync(EventEvt("old", false, GameEventSources.Carpet, TimeSpan.FromDays(400)));
        Assert.Single(handler.Requests);
        var blocked = Assert.Single(store.Suppressions);
        Assert.Contains(FeedEventKinds.FilterFreshOnly, blocked.Reason, StringComparison.Ordinal);
        Assert.Contains(FeedEventKinds.FilterDeviceOnly, blocked.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GameEvent_UltraOnly_SkipsStandard() {
        var store = new FakeStore(KindSub(1, FeedEventKinds.GameEvent, FeedEventKinds.TriggerUltraOnly));
        var handler = Ok();
        var d = Dispatcher(store, handler);

        await d.DispatchAsync(EventEvt("std", false, GameEventSources.Device, TimeSpan.Zero));
        Assert.Empty(handler.Requests);
        await d.DispatchAsync(EventEvt("ultra", true, GameEventSources.Device, TimeSpan.Zero));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GameEvent_DedupsOnIdAndStart() {
        var store = new FakeStore(KindSub(1, FeedEventKinds.GameEvent, FeedEventKinds.TriggerAny));
        var handler = Ok();
        var d = Dispatcher(store, handler);

        await d.DispatchAsync(EventEvt("same", false, GameEventSources.Device, TimeSpan.Zero));
        await d.DispatchAsync(EventEvt("same", false, GameEventSources.Device, TimeSpan.FromHours(1)));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Contract_Triggers_SplitByReleaseKind() {
        var store = new FakeStore(
            KindSub(1, FeedEventKinds.ContractRelease, FeedEventKinds.TriggerNewOnly),
            KindSub(2, FeedEventKinds.ContractRelease, FeedEventKinds.TriggerLeggacyOnly),
            KindSub(3, FeedEventKinds.ContractRelease, FeedEventKinds.TriggerUltraOnly));
        var handler = Ok();
        var d = Dispatcher(store, handler);

        await d.DispatchAsync(ContractEvt("fresh", false, false, ContractSources.Device, TimeSpan.Zero));
        Assert.Equal([1], store.Deliveries.Select(x => x.SubscriptionId));

        await d.DispatchAsync(ContractEvt("leg", true, true, ContractSources.Device, TimeSpan.Zero));
        Assert.Equal([1, 2, 3], store.Deliveries.Select(x => x.SubscriptionId));
    }

    [Fact]
    public async Task Contract_DeviceOnly_BlocksCarpetRows() {
        var store = new FakeStore(KindSub(1, FeedEventKinds.ContractRelease, FeedEventKinds.TriggerAny,
            FeedEventKinds.FilterDeviceOnly));
        var handler = Ok();
        await Dispatcher(store, handler).DispatchAsync(
            ContractEvt("carpet", false, false, ContractSources.Carpet, TimeSpan.Zero));

        Assert.Empty(handler.Requests);
        Assert.Equal(FeedEventKinds.FilterDeviceOnly, Assert.Single(store.Suppressions).Reason);
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
        return new ProtoBuildEvent(id, "ios", "1.37.1", "111340", null, "", true, true, "https://x/y",
            VersionDelta.Forward, "1.37.0", "1.37.0.1", flaws);
    }

    [Fact]
    public async Task Backfill_NeverReachesAnyProtoSubscription() {
        var store = new FakeStore(
            Sub(1, FeedEventKinds.TriggerVersionUp, "android"),
            Sub(2, FeedEventKinds.TriggerProtoChanged, "android"),
            Sub(3, FeedEventKinds.TriggerNewVersion, "android"),
            Sub(4, FeedEventKinds.TriggerSuspect, "android"));
        var handler = Ok();
        var d = Dispatcher(store, handler);

        await d.DispatchAsync(ProtoEvt(true) with { Delta = VersionDelta.Backfill, ProtoVersionId = 1 });
        await d.DispatchAsync(ProtoEvt(true) with { Delta = VersionDelta.Unknown, ProtoVersionId = 2 });
        await d.DispatchAsync(BrokenIosEvt(3) with { Delta = VersionDelta.Backfill });

        Assert.Empty(handler.Requests);
        Assert.Empty(store.Deliveries);
        Assert.Empty(store.Suppressions);
    }

    [Fact]
    public async Task VersionUp_Fires_OnForward_Only() {
        var store = new FakeStore(Sub(1, FeedEventKinds.TriggerVersionUp, "android"));
        var handler = Ok();
        var d = Dispatcher(store, handler);

        await d.DispatchAsync(ProtoEvt(true) with { Delta = VersionDelta.Backfill, ProtoVersionId = 1 });
        Assert.Empty(handler.Requests);

        await d.DispatchAsync(ProtoEvt(true) with { Delta = VersionDelta.Forward, ProtoVersionId = 2 });
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Filters_Block_Flawed_Event_And_Record_Reason() {
        var store = new FakeStore(Guarded(1, FeedEventKinds.TriggerNewVersion, "ios"));
        var handler = Ok();
        await Dispatcher(store, handler).DispatchAsync(BrokenIosEvt());

        Assert.Empty(handler.Requests);
        Assert.Empty(store.Deliveries);
        var blocked = Assert.Single(store.Suppressions);
        Assert.Contains(FeedEventKinds.FilterRequireClientVersion, blocked.Reason, StringComparison.Ordinal);
        Assert.Contains(FeedEventKinds.FilterSaneBuild, blocked.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain(FeedEventKinds.FilterKnownDelta, blocked.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Suspect_Fires_OnFlawed_NotOnClean_AndBypassesFilters() {
        var store = new FakeStore(Guarded(1, FeedEventKinds.TriggerSuspect, "android", "ios"));
        var handler = Ok();
        var d = Dispatcher(store, handler);

        await d.DispatchAsync(ProtoEvt(true, id: 5) with { Delta = VersionDelta.Forward, Flaws = [] });
        Assert.Empty(handler.Requests);

        await d.DispatchAsync(BrokenIosEvt());
        Assert.Single(handler.Requests);
        Assert.Empty(store.Suppressions);
    }

    [Fact]
    public async Task Clean_Forward_Event_Passes_Filters() {
        var store = new FakeStore(Guarded(1, FeedEventKinds.TriggerVersionUp, "android"));
        var handler = Ok();
        await Dispatcher(store, handler).DispatchAsync(
            ProtoEvt(true) with { Delta = VersionDelta.Forward, Flaws = [] });

        Assert.Single(handler.Requests);
        Assert.Empty(store.Suppressions);
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
            bool active, string? messageTemplate, string[] filters, string? label, CancellationToken ct = default) {
            var s = Subs.FirstOrDefault(x => x.Id == id && x.OwnerUserId == ownerUserId);
            if (s is null) return Task.FromResult(false);
            s.Platforms = platforms;
            s.Trigger = trigger;
            s.Active = active;
            s.MessageTemplate = messageTemplate;
            s.Filters = filters;
            s.Label = label;
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
