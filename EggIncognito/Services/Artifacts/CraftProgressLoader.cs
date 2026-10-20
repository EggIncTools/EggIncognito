using EggIdentity.Contract;
using EggIncognito.Core.Services;
using Ei;
using Google.Protobuf;

namespace EggIncognito.Services.Artifacts;

public sealed record CraftProgress(double CraftingXp, IReadOnlyDictionary<(int AfxId, int AfxLevel), uint> Counts, EidSnapshot Snapshot);

public sealed class CraftProgressLoader(IHttpClientFactory httpFactory, ILogger<CraftProgressLoader> logger) {
    private const string Url = AuxbrainHosts.Origin + "/ei/bot_first_contact";

    public async Task<CraftProgress?> LoadAsync(string eiUserId, CancellationToken ct) {
        var request = new EggIncFirstContactRequest {
            Rinfo = new BasicRequestInfo { EiUserId = eiUserId },
            EiUserId = eiUserId
        };
        try {
            var client = httpFactory.CreateClient("inspector");
            var content = new FormUrlEncodedContent([new("data", Convert.ToBase64String(request.ToByteArray()))]);
            using var resp = await client.PostAsync(Url, content, ct);
            if (!resp.IsSuccessStatusCode) return null;
            string body = (await resp.Content.ReadAsStringAsync(ct)).Trim();
            byte[] raw = ProtoFraming.FromBase64Loose(body);
            var response = EggIncFirstContactResponse.Parser.ParseFrom(ProtoFraming.TryUnwrap(raw) ?? raw);
            if (response.Backup is not { } backup) return null;

            var counts = new Dictionary<(int, int), uint>();
            foreach (var status in backup.ArtifactsDb?.ArtifactStatus ?? []) {
                if (status.Spec is not { } spec) continue;
                counts[((int)spec.Name, (int)spec.Level)] = status.Count;
            }

            var snapshot = new EidSnapshot {
                GameName = string.IsNullOrWhiteSpace(backup.UserName) ? null : backup.UserName,
                PeCount = (int)(backup.Game?.EggsOfProphecy ?? 0),
                TeCount = (int)(backup.Virtue?.EovEarned.Sum(n => (long)n) ?? 0),
                BackupAt = backup.ApproxTime > 0 ? (long)backup.ApproxTime : null
            };
            return new CraftProgress(backup.Artifacts?.CraftingXp ?? 0, counts, snapshot);
        } catch (Exception ex) when (ex is HttpRequestException or InvalidProtocolBufferException or FormatException or TaskCanceledException) {
            logger.LogDebug(ex, "craft progress could not be loaded");
            return null;
        }
    }
}
