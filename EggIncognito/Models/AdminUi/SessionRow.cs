using EggIncognito.Capture;
using EggIncognito.Services.Devices;

namespace EggIncognito.Models.AdminUi;

public record SessionRow(string Key, string Kind, bool Killable, bool Running, int Port, long Flows, long Connections, int Devices, long DecryptOk, int DecryptErr) {
    public static SessionRow FromCapture(string key, CaptureStats s) =>
        new(key, key == CaptureSessionManager.LocalKey ? "local" : "user", true, s.Running, s.Port,
            s.CapturedAuxbrain, s.ActiveConnections, s.DeviceCount, s.DecryptOk, s.DecryptErrors);

    public static SessionRow FromDevice(string deviceId, int port, DeviceCaptureDiag diag) =>
        new($"device:{deviceId}", "device", false, port != 0, port, diag.Flows, diag.ClientConnects, 1,
            diag.RinfoHarvests, diag.LastDecryptError is null ? 0 : 1);
}
