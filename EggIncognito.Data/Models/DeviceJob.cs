using System.ComponentModel.DataAnnotations.Schema;

namespace EggIncognito.Data.Models;

public static class DeviceJobKinds {
    public const string Probe = "probe";
    public const string StoreCheck = "store_check";
    public const string Harvest = "harvest";
    public const string Recert = "recert";
    public const string Cookbook = "cookbook";
}

public static class DeviceJobStates {
    public const string Running = "running";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
}

[Table("device_jobs")]
public sealed class DeviceJob {
    [Column("id")] public long Id { get; set; }
    [Column("device_id")] public string DeviceId { get; set; } = "";
    [Column("kind")] public string Kind { get; set; } = "";
    [Column("state")] public string State { get; set; } = DeviceJobStates.Running;
    [Column("trigger")] public string Trigger { get; set; } = "poll";
    [Column("started_at")] public DateTimeOffset StartedAt { get; set; }
    [Column("finished_at")] public DateTimeOffset? FinishedAt { get; set; }
    [Column("outcome")] public string? Outcome { get; set; }
    [Column("message")] public string? Message { get; set; }
    [Column("reachable")] public bool? Reachable { get; set; }
    [Column("app_version")] public string? AppVersion { get; set; }
    [Column("build")] public string? Build { get; set; }
    [Column("client_version")] public int? ClientVersion { get; set; }
    [Column("revision")] public string? Revision { get; set; }
    [Column("detail")] public string? Detail { get; set; }
}
