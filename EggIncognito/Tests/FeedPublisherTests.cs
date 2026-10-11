using System.Net;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Services.Events;
using EggIncognito.Services.Feed;
using EggIncognito.Services.Feed.Kinds;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace EggIncognito.Tests;

public class FeedPublisherTests {
    private static readonly DateTimeOffset Start = new(2026, 10, 12, 16, 0, 0, TimeSpan.Zero);

    private static (FeedPublisher Publisher, StubHttpMessageHandler Handler) Build(string? baseUrl = null) {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var services = new ServiceCollection();
        services.AddSingleton<IFeedSubscriptionStore>(new RecordingStore(new FeedSubscription {
            Id = 1,
            EventKind = FeedEventKinds.GameEvent,
            Trigger = FeedEventKinds.TriggerAny,
            TargetUrl = "https://discord.com/api/webhooks/1/abc",
            Active = true
        }));
        services.AddSingleton<IHttpClientFactory>(new StubHttpFactory(handler));
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<FeedDispatcher>(sp => new FeedDispatcher(
            sp.GetRequiredService<IFeedSubscriptionStore>(), sp.GetRequiredService<IHttpClientFactory>(),
            NullLogger<FeedDispatcher>.Instance, TimeProvider.System));
        var provider = services.BuildServiceProvider();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Feed:PageBaseUrl"] = baseUrl })
            .Build();
        var publisher = new FeedPublisher(provider.GetRequiredService<IServiceScopeFactory>(), config,
            NullLogger<FeedPublisher>.Instance);
        return (publisher, handler);
    }

    [Fact]
    public async Task PublishAsync_ResolvesAScopedDispatcher_AndPosts() {
        var (publisher, handler) = Build();
        await publisher.PublishAsync(new GameEventAddedEvent("e", "boost-sale", "Boost", 0.7, false, Start,
            Start.AddHours(72), GameEventSources.Device, Start, "https://x/events"), CancellationToken.None);

        Assert.Single(handler.Requests);
    }

    [Fact]
    public void PageUrl_UsesTheDefaultHost_OrTheConfiguredOne() {
        Assert.Equal("https://eggincognito.egginc.tools/events", Build().Publisher.PageUrl("events"));
        Assert.Equal("https://example.test/contracts", Build("https://example.test/").Publisher.PageUrl("/contracts"));
    }

    [Fact]
    public void Ingestor_ToEvent_CarriesRowIdentityAndSeenAt() {
        var row = new GameEvent {
            EventId = "egg-boost",
            EventType = "boost-sale",
            Message = "Boost sale",
            Multiplier = 0.7,
            Ultra = true,
            StartTime = Start,
            EndTime = Start.AddHours(72),
            Source = GameEventSources.Device
        };
        var evt = GameEventIngestor.ToEvent(row, Start.AddMinutes(3), "https://x/events");

        Assert.Equal($"egg-boost:{Start.ToUnixTimeSeconds()}", evt.DedupKey);
        Assert.True(evt.Fresh);
        Assert.True(evt.FromDevice);
        Assert.True(evt.Ultra);
    }

    private sealed class RecordingStore(params FeedSubscription[] subs) : IFeedSubscriptionStore {
        private readonly List<FeedSubscription> _subs = [.. subs];
        private readonly List<FeedDelivery> _deliveries = [];

        public Task<FeedSubscription> AddAsync(FeedSubscription sub, CancellationToken ct = default) {
            _subs.Add(sub);
            return Task.FromResult(sub);
        }

        public Task<List<FeedSubscription>> ActiveAsync(CancellationToken ct = default) =>
            Task.FromResult(_subs.Where(s => s.Active).ToList());

        public Task<bool> AlreadyDeliveredAsync(int subId, string eventKind, string dedupKey, CancellationToken ct = default) =>
            Task.FromResult(_deliveries.Any(d => d.SubscriptionId == subId && d.DedupKey == dedupKey));

        public Task RecordAsync(FeedDelivery delivery, CancellationToken ct = default) {
            _deliveries.Add(delivery);
            return Task.CompletedTask;
        }

        public Task SetActiveAsync(int subId, bool active, CancellationToken ct = default) => Task.CompletedTask;
        public Task BumpFailAsync(int subId, CancellationToken ct = default) => Task.CompletedTask;
        public Task MarkDeliveredAsync(int subId, DateTimeOffset at, CancellationToken ct = default) => Task.CompletedTask;

        public Task<List<FeedSubscription>> ByOwnerAsync(Guid ownerUserId, CancellationToken ct = default) =>
            Task.FromResult(new List<FeedSubscription>());

        public Task<bool> DeleteAsync(int id, Guid ownerUserId, CancellationToken ct = default) => Task.FromResult(false);

        public Task<bool> UpdateAsync(int id, Guid ownerUserId, string[] platforms, string trigger, bool active,
            string? messageTemplate, string[] filters, string? label, CancellationToken ct = default) =>
            Task.FromResult(false);

        public Task SuppressAsync(int subId, string eventKind, string dedupKey, string reason, string? summary,
            CancellationToken ct = default) => Task.CompletedTask;

        public Task<List<FeedSubscription>> AllForAdminAsync(CancellationToken ct = default) => Task.FromResult(_subs);
        public Task<FeedSubscription?> AdminByIdAsync(int id, CancellationToken ct = default) => Task.FromResult<FeedSubscription?>(null);
        public Task<bool> AdminDeactivateAsync(int id, CancellationToken ct = default) => Task.FromResult(false);
        public Task<bool> AdminDeleteAsync(int id, CancellationToken ct = default) => Task.FromResult(false);
    }
}
