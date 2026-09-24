using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using EggIncognito.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Data.Services;

public sealed class CaptureAddressStore(EggIncognitoDbContext db, TimeProvider time) {
    public static IPAddress RandomInPrefix(string prefixCidr) {
        string[] parts = prefixCidr.Split('/');
        byte[] prefix = IPAddress.Parse(parts[0]).GetAddressBytes();
        int prefixLen = parts.Length > 1 ? int.Parse(parts[1], CultureInfo.InvariantCulture) : 64;
        byte[] rnd = RandomNumberGenerator.GetBytes(16);

        byte[] bytes = new byte[16];
        for (int i = 0; i < 16; i++) {
            int firstBit = i * 8;
            if (firstBit + 8 <= prefixLen) {
                bytes[i] = prefix[i];
                continue;
            }

            if (firstBit >= prefixLen) {
                bytes[i] = rnd[i];
                continue;
            }

            int prefixBits = prefixLen - firstBit;
            byte mask = (byte)(0xFF << (8 - prefixBits));
            bytes[i] = (byte)((prefix[i] & mask) | (rnd[i] & ~mask));
        }

        bool hostAllZero = true;
        for (int i = 8; i < 15; i++) {
            if (bytes[i] != 0)
                hostAllZero = false;
        }

        if (hostAllZero && bytes[15] <= 1) bytes[15] = 2;
        return new IPAddress(bytes);
    }

    public async Task<IPAddress> AddrForUserAsync(string prefixCidr, Guid userId, CancellationToken ct = default) {
        var row = await db.CaptureProxyAddrs.AsNoTracking().FirstOrDefaultAsync(a => a.UserId == userId, ct);
        return row is not null ? IPAddress.Parse(row.Addr) : await MintAsync(prefixCidr, userId, ct);
    }

    public async Task<IPAddress> RotateAsync(string prefixCidr, Guid userId, CancellationToken ct = default)
        => await MintAsync(prefixCidr, userId, ct);

    private async Task<IPAddress> MintAsync(string prefixCidr, Guid userId, CancellationToken ct) {
        for (int attempt = 0; attempt < 5; attempt++) {
            var addr = RandomInPrefix(prefixCidr);
            string canonical = addr.ToString();
            var row = await db.CaptureProxyAddrs.FirstOrDefaultAsync(a => a.UserId == userId, ct);
            if (row is null)
                db.CaptureProxyAddrs.Add(new CaptureProxyAddr { UserId = userId, Addr = canonical, CreatedAt = time.GetUtcNow() });
            else
                row.Addr = canonical;
            try {
                await db.SaveChangesAsync(ct);
                return addr;
            } catch (DbUpdateException) {
                db.ChangeTracker.Clear();
            }
        }

        throw new InvalidOperationException("could not mint a unique capture address after retries");
    }

    public async Task<Guid?> UserForAddrAsync(IPAddress addr, CancellationToken ct = default) {
        string canonical = addr.ToString();
        var row = await db.CaptureProxyAddrs.AsNoTracking().FirstOrDefaultAsync(a => a.Addr == canonical, ct);
        return row?.UserId;
    }
}
