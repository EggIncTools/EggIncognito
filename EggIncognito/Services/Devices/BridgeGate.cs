using System.Net;
using System.Security.Cryptography;
using System.Text;
using EggIdentity.Contract;
using EggIncognito.Core.Services.Devices;
using Microsoft.AspNetCore.Mvc;

namespace EggIncognito.Services.Devices;

public static class BridgeGate {
    public static IActionResult? Check(HttpContext http, DeviceTransportConfig? cfg, ICurrentUser user,
        ILogger? logger) {
        if (cfg is null || !cfg.BridgeEnabled) return new NotFoundResult();
        if (cfg.Mode == DeviceTransportMode.Remote) return new NotFoundResult();

        var denied = new ObjectResult(new { error = "forbidden" }) { StatusCode = 403 };
        if (!CallerInAllowedRange(http, cfg)) {
            Log(http, logger,
                $"caller {http.Connection.RemoteIpAddress} is outside DeviceTransport:AllowedCidrs "
                + $"[{string.Join(", ", cfg.AllowedCidrs)}]");
            return denied;
        }

        if (SecretPresented(http, cfg) || user.IsAtLeast(UserRole.Admin)) return null;

        Log(http, logger, string.IsNullOrEmpty(cfg.ApiKey)
            ? "DeviceTransport:ApiKey is not set on this host, so the bridge authorizes nobody by key"
            : http.Request.Headers.ContainsKey(BridgeRoutes.SecretHeader)
                ? $"the {BridgeRoutes.SecretHeader} presented does not match DeviceTransport:ApiKey on this host"
                : $"no {BridgeRoutes.SecretHeader} header was presented and the caller is not an admin session");
        return denied;
    }

    private static void Log(HttpContext http, ILogger? logger, string reason) =>
        logger?.LogWarning("device bridge refused {Path}: {Reason}", http.Request.Path.Value, reason);

    private static bool CallerInAllowedRange(HttpContext http, DeviceTransportConfig cfg) {
        if (cfg.AllowedCidrs.Length == 0) return true;

        var ip = http.Connection.RemoteIpAddress;
        if (ip is null) return false;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();

        foreach (string cidr in cfg.AllowedCidrs) {
            try {
                if (IPNetwork.Parse(cidr).Contains(ip)) return true;
            } catch (FormatException) {
                continue;
            }
        }

        return false;
    }

    private static bool SecretPresented(HttpContext http, DeviceTransportConfig cfg) {
        if (string.IsNullOrEmpty(cfg.ApiKey)) return false;
        if (!http.Request.Headers.TryGetValue(BridgeRoutes.SecretHeader, out var presented)) return false;
        string? offered = presented.ToString();
        if (string.IsNullOrEmpty(offered)) return false;

        byte[] expected = SHA256.HashData(Encoding.UTF8.GetBytes(cfg.ApiKey));
        byte[] actual = SHA256.HashData(Encoding.UTF8.GetBytes(offered));
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}
