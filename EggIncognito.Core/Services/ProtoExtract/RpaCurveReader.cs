namespace EggIncognito.Core.Services.ProtoExtract;

public static class RpaCurveReader {
    public static Curve Read(byte[] bin) {
        if (bin is null || bin.Length < 0x18) return new Curve(false, 0, 0, [], "too short");
        if (!(bin[0] == 'R' && bin[1] == 'P' && bin[2] == 'A' && bin[3] == '1'))
            return new Curve(false, 0, 0, [], "not RPA1");

        int tracks = I32(bin, 0x04);
        int nKeys = I32(bin, 0x10);
        int nComp = I32(bin, 0x14);
        if (nKeys is < 0 or > 1_000_000) return new Curve(false, tracks, nComp, [], $"implausible nKeys {nKeys}");

        long need = 0x18L + (long)nKeys * 16;
        if (need > bin.Length) return new Curve(false, tracks, nComp, [], $"truncated: need {need}, have {bin.Length}");

        var keys = new Key[nKeys];
        int o = 0x18;
        for (int i = 0; i < nKeys; i++, o += 16)
            keys[i] = new Key(F32(bin, o), F32(bin, o + 4), F32(bin, o + 8), F32(bin, o + 12));

        return new Curve(true, tracks, nComp, keys, "ok");
    }

    private static int I32(byte[] b, int o) => b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24);
    private static float F32(byte[] b, int o) => BitConverter.ToSingle(b.AsSpan(o));

    public readonly record struct Key(float Time, float C0, float C1, float C2);

    public readonly record struct Curve(
        bool Ok,
        int Tracks,
        int Components,
        IReadOnlyList<Key> Keys,
        string Diagnostics) {
        public float Duration => Keys.Count == 0 ? 0 : Keys[^1].Time;

        public float Sample(float t, int comp = 0) {
            if (Keys.Count == 0) return 0;
            if (t <= Keys[0].Time) return Comp(Keys[0], comp);
            if (t >= Keys[^1].Time) return Comp(Keys[^1], comp);
            for (int i = 1; i < Keys.Count; i++) {
                if (t <= Keys[i].Time) {
                    var a = Keys[i - 1];
                    var b = Keys[i];
                    float span = b.Time - a.Time;
                    float u = span <= 0 ? 0 : (t - a.Time) / span;
                    return Comp(a, comp) + (Comp(b, comp) - Comp(a, comp)) * u;
                }
            }

            return Comp(Keys[^1], comp);
        }

        private static float Comp(Key k, int comp) => comp switch { 0 => k.C0, 1 => k.C1, 2 => k.C2, _ => 0 };
    }
}
