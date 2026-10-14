namespace EggIncognito.Core.Services.Devices;

public static class VersionCompare {
    public static readonly Comparer<string?> DottedComparer = Comparer<string?>.Create(Dotted);
    public static readonly Comparer<string?> BuildComparer = Comparer<string?>.Create(Build);

    public static bool IsValidDotted(string? v) => TryParseDotted(v, out _);

    public static int Dotted(string? a, string? b) {
        var aOk = TryParseDotted(a, out var ac);
        var bOk = TryParseDotted(b, out var bc);
        if (!aOk || !bOk) return aOk.CompareTo(bOk);
        var n = Math.Max(ac.Length, bc.Length);
        for (var i = 0; i < n; i++) {
            var x = i < ac.Length ? ac[i] : 0L;
            var y = i < bc.Length ? bc[i] : 0L;
            var c = x.CompareTo(y);
            if (c != 0) return c;
        }
        return 0;
    }

    public static int Build(string? a, string? b) {
        var aOk = !string.IsNullOrWhiteSpace(a);
        var bOk = !string.IsNullOrWhiteSpace(b);
        if (!aOk || !bOk) return aOk.CompareTo(bOk);
        var aNum = long.TryParse(a!.Trim(), out var an);
        var bNum = long.TryParse(b!.Trim(), out var bn);
        if (aNum && bNum) return an.CompareTo(bn);
        if (IsValidDotted(a) && IsValidDotted(b)) return Dotted(a, b);
        if (aNum != bNum) return aNum.CompareTo(bNum);
        return string.CompareOrdinal(a, b);
    }

    public static long DottedKey(string? v) {
        if (!TryParseDotted(v, out var parts)) return long.MinValue;
        long key = 0;
        for (var i = 0; i < 4; i++) {
            var p = i < parts.Length ? parts[i] : 0L;
            key = (key << 16) | (p < 0 ? 0 : p > 0xFFFF ? 0xFFFF : p);
        }
        return key;
    }

    private static bool TryParseDotted(string? v, out long[] parts) {
        parts = [];
        if (string.IsNullOrWhiteSpace(v)) return false;
        var segs = v.Trim().Split('.');
        var result = new long[segs.Length];
        for (var i = 0; i < segs.Length; i++) {
            if (!long.TryParse(segs[i], out result[i]) || result[i] < 0) return false;
        }
        parts = result;
        return true;
    }
}
