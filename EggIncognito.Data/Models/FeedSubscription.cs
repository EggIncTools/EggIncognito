using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

[Table("feed_subscriptions")]
public sealed class FeedSubscription {
    [Column("id")] public int Id { get; set; }
    [Column("kind")] public string Kind { get; set; } = "discord";
    [Column("target_url")] public string TargetUrl { get; set; } = "";
    [Column("event_kind")] public string EventKind { get; set; } = "proto_build";
    [Column("platforms")] public string[] Platforms { get; set; } = ["android", "ios"];
    [Column("trigger")] public string Trigger { get; set; } = "version_up";
    [Column("filters")] public string[] Filters { get; set; } = [];
    [Column("secret")] public string? Secret { get; set; }
    [Column("label")] public string? Label { get; set; }

    [Column("message_template")] public string? MessageTemplate { get; set; }
    [Column("owner_user_id")] public Guid? OwnerUserId { get; set; }
    [Column("created_at")] public DateTimeOffset CreatedAt { get; set; }
    [Column("active")] public bool Active { get; set; } = true;
    [Column("last_delivery_at")] public DateTimeOffset? LastDeliveryAt { get; set; }
    [Column("fail_count")] public int FailCount { get; set; }
}
