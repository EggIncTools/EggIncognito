using EggIncognito.Core;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Data.Services;

public static class PgChannels {
    public const string DeviceJobs = "egi_device_jobs";
    public const string Apks = "egi_apks";
    public const string ProtoRegistry = "egi_proto_registry";
    public const string StagedProtos = "egi_staged_protos";
}

public static class PgNotify {
    public const int MaxPayload = 2000;

    public static string ApkPayload(ApkStoreNotice notice) =>
        Strings.Truncate($"{notice.Kind}:{notice.Platform}:{notice.Package}:{notice.AppVersion}@{notice.Build}",
            MaxPayload);

    public static async Task SendAsync(EggIncognitoDbContext db, string channel, string payload,
        CancellationToken ct) {
        try {
            await db.Database.ExecuteSqlRawAsync("SELECT pg_notify({0}, {1})",
                [channel, Strings.Truncate(payload, MaxPayload)], ct);
        } catch (Exception) {
        }
    }
}
