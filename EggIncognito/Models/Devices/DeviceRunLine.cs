using System.Globalization;

namespace EggIncognito.Models.Devices;

public sealed record DeviceRunLine(
    string Title,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    bool Done,
    bool Ok,
    bool Cancelled) {
    public string Elapsed {
        get {
            if (StartedAt is not { } started) return "0:00";
            var span = (FinishedAt ?? DateTimeOffset.UtcNow) - started;
            if (span < TimeSpan.Zero) span = TimeSpan.Zero;
            return span.TotalHours >= 1
                ? string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}")
                : string.Create(CultureInfo.InvariantCulture, $"{span.Minutes}:{span.Seconds:00}");
        }
    }

    public string Icon => !Done ? "play" : Cancelled ? "circle" : Ok ? "circle-check" : "circle-x";

    public string IconClass => !Done ? "text-accent" : Cancelled ? "text-muted" : Ok ? "text-ok" : "text-err";

    public string State => !Done ? "running" : Cancelled ? "stopped" : Ok ? "done" : "failed";
}
