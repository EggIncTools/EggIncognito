using System.Globalization;
using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices;

public sealed class AndroidScreenStreamSource(IDeviceConnectionFactory factory) : IScreenStreamSource {
    public string Platform => Platforms.Android;

    public async Task<string?> StreamAsync(DeviceTarget target, ScreenStreamOptions options, Stream output,
        CancellationToken ct) {
        var conn = factory.For(target);
        if (conn is null) return "no connection for device";
        if (!conn.SupportsExecOut) return "this connection cannot stream exec-out";

        string size = string.Create(CultureInfo.InvariantCulture, $"{options.Width}x{options.Height}");
        string command = ScreenVideoPump.ScreenrecordCommand(size, options.Bitrate);
        await conn.ShellAsync(ScreenVideoPump.KillStaleCommand, ct);
        var pump = new ScreenVideoPump(token => conn.ExecOutStreamAsync(command, token));
        return await pump.RunAsync(output, ct);
    }
}
