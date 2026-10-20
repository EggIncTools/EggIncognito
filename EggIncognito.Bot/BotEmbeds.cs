using Discord;
using EggIdentity.Formatting;

namespace EggIncognito.Bot;

public static class BotEmbeds {
    private const uint Accent = 0xEF7559;

    private static string Up(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes}m" : $"{t.Minutes}m {t.Seconds}s";

    public static Embed Status(StatusSnapshot s) {
        var b = new EmbedBuilder()
            .WithTitle("EggIncognito - status")
            .WithColor(new Color(Accent))
            .AddField("Mode", s.Mode, true)
            .AddField("DB layer", s.DbEnabled ? "on" : "off", true)
            .AddField("Signing", s.SigningReady ? "ready" : "disabled", true)
            .AddField("Capture", s.CaptureState, true)
            .AddField("Uptime", Up(s.Uptime), true);
        if (s.CaptureRunning) {
            b.AddField("Flows", s.FlowsCaptured.ToString(), true)
                .AddField("Devices", s.DeviceCount.ToString(), true)
                .AddField("Captured", ByteFormat.Humanize(s.BytesCaptured), true);
        }

        return b.Build();
    }

    public static Embed Endpoints(StatusSnapshot s) =>
        new EmbedBuilder()
            .WithTitle("Endpoint coverage")
            .WithColor(new Color(Accent))
            .AddField("ok", s.EndpointsOk.ToString(), true)
            .AddField("empty", s.EndpointsEmpty.ToString(), true)
            .AddField("missing", s.EndpointsMissing.ToString(), true)
            .Build();

    public static Embed Health(TimeSpan uptime) =>
        new EmbedBuilder()
            .WithTitle("pong")
            .WithDescription($"online - uptime {Up(uptime)}")
            .WithColor(new Color(Accent))
            .Build();

    public static Embed Error(string message) =>
        new EmbedBuilder().WithTitle("Something went wrong").WithDescription(message).WithColor(0xED4245).Build();
}
