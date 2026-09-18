using System.Text;

namespace EggIncognito.Core.Services.ProtoExtract;

public static class StringLocator {
    public readonly record struct StringHit(ulong Va, int FileOff, string Section, string Neighbors);

    private static readonly string[] StringSections = [
        "__cstring", "__const", ".rodata", ".data.rel.ro",
    ];

    public static IReadOnlyList<StringHit> Find(byte[] bin, string needle) {
        var outp = new List<StringHit>();
        if (bin is null || bin.Length < 8 || string.IsNullOrEmpty(needle)) return outp;
        var img = BinaryImage.Load(bin);
        if (img is null) return outp;

        byte[] want = Encoding.UTF8.GetBytes(needle);
        if (want.Length == 0) return outp;

        foreach (var s in img.Sections) {
            if (s.VmSize == 0 || Array.IndexOf(StringSections, s.Name) < 0) continue;
            int start = s.FileOff;
            long endLong = (long)s.FileOff + (long)s.VmSize;
            int end = (int)Math.Min(endLong, bin.Length);
            if (start < 0 || start >= end) continue;

            int p = start;
            while (p <= end - want.Length) {
                int idx = IndexOf(bin, p, end, want);
                if (idx < 0) break;

                int lo = idx;
                while (lo > start && bin[lo - 1] != 0) lo--;
                int hi = idx + want.Length;
                while (hi < end && bin[hi] != 0) hi++;

                ulong va = s.VmAddr + (ulong)(lo - s.FileOff);
                outp.Add(new StringHit(va, lo, s.Name, Neighbors(bin, start, end, lo, hi)));
                p = hi + 1;
            }
        }

        return outp;
    }

    private static int IndexOf(byte[] bin, int from, int end, byte[] want) {
        int last = end - want.Length;
        for (int i = from; i <= last; i++) {
            int k = 0;
            while (k < want.Length && bin[i + k] == want[k]) k++;
            if (k == want.Length) return i;
        }

        return -1;
    }

    private static string Neighbors(byte[] bin, int start, int end, int lo, int hi) {
        var before = new List<string>();
        int nul = lo - 1;
        while (nul > start && before.Count < 4) {
            int b = nul;
            while (b > start && bin[b - 1] != 0) b--;
            string s = Decode(bin, b, nul);
            if (s.Length > 0) before.Add(s);
            nul = b - 1;
        }

        before.Reverse();

        var after = new List<string>();
        int ns = hi + 1;
        while (ns < end && after.Count < 4) {
            int e = ns;
            while (e < end && bin[e] != 0) e++;
            string s = Decode(bin, ns, e);
            if (s.Length > 0) after.Add(s);
            ns = e + 1;
        }

        var parts = new List<string>(before.Count + after.Count);
        parts.AddRange(before);
        parts.AddRange(after);
        return string.Join(" | ", parts);
    }

    private static string Decode(byte[] bin, int b, int e) {
        int len = Math.Min(e - b, 48);
        if (len <= 0) return "";
        var sb = new StringBuilder(len);
        for (int i = 0; i < len; i++) {
            byte ch = bin[b + i];
            if (ch is < 0x20 or > 0x7e) return "";
            sb.Append((char)ch);
        }

        return sb.ToString();
    }
}
