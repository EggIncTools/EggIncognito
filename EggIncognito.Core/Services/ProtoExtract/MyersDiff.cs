namespace EggIncognito.Core.Services.ProtoExtract;

public enum DiffOpKind {
    Equal,
    Delete,
    Insert
}

public readonly record struct DiffOp(DiffOpKind Kind, int AStart, int ALength, int BStart, int BLength);

public static class MyersDiff {
    public const int GuardBudget = 400_000;

    public static IReadOnlyList<DiffOp> Compute<T>(
        IReadOnlyList<T> a, IReadOnlyList<T> b, IEqualityComparer<T>? comparer = null) {
        if (a is null || a.Count == 0) {
            if (b is null || b.Count == 0) return [];
            return [new DiffOp(DiffOpKind.Insert, 0, 0, 0, b.Count)];
        }

        if (b is null || b.Count == 0) return [new DiffOp(DiffOpKind.Delete, 0, a.Count, 0, 0)];

        var eq = comparer ?? EqualityComparer<T>.Default;
        int an = a.Count;
        int bn = b.Count;

        int prefix = 0;
        while (prefix < an && prefix < bn && eq.Equals(a[prefix], b[prefix])) prefix++;

        int suffix = 0;
        while (suffix < an - prefix && suffix < bn - prefix &&
            eq.Equals(a[an - 1 - suffix], b[bn - 1 - suffix])) {
            suffix++;
        }

        int n = an - prefix - suffix;
        int m = bn - prefix - suffix;

        var ops = new List<DiffOp>();
        if (prefix > 0) ops.Add(new DiffOp(DiffOpKind.Equal, 0, prefix, 0, prefix));

        if (n + m > GuardBudget) {
            if (n > 0) ops.Add(new DiffOp(DiffOpKind.Delete, prefix, n, prefix, 0));
            if (m > 0) ops.Add(new DiffOp(DiffOpKind.Insert, prefix + n, 0, prefix, m));
        } else if (n > 0 || m > 0) {
            AppendMiddle(a, b, eq, prefix, n, m, ops);
        }

        if (suffix > 0) ops.Add(new DiffOp(DiffOpKind.Equal, an - suffix, suffix, bn - suffix, suffix));
        return ops;
    }

    private static void AppendMiddle<T>(
        IReadOnlyList<T> a, IReadOnlyList<T> b, IEqualityComparer<T> eq,
        int prefix, int n, int m, List<DiffOp> ops) {
        var script = Backtrack(a, b, eq, prefix, n, m);
        int ai = prefix;
        int bi = prefix;
        int i = script.Count - 1;
        while (i >= 0) {
            if (script[i] == DiffOpKind.Equal) {
                int run = 0;
                while (i >= 0 && script[i] == DiffOpKind.Equal) {
                    run++;
                    i--;
                }

                ops.Add(new DiffOp(DiffOpKind.Equal, ai, run, bi, run));
                ai += run;
                bi += run;
            } else {
                int deletes = 0;
                int inserts = 0;
                while (i >= 0 && script[i] != DiffOpKind.Equal) {
                    if (script[i] == DiffOpKind.Delete) deletes++;
                    else inserts++;
                    i--;
                }

                if (deletes > 0) {
                    ops.Add(new DiffOp(DiffOpKind.Delete, ai, deletes, bi, 0));
                    ai += deletes;
                }

                if (inserts > 0) {
                    ops.Add(new DiffOp(DiffOpKind.Insert, ai, 0, bi, inserts));
                    bi += inserts;
                }
            }
        }
    }

    private static List<DiffOpKind> Backtrack<T>(
        IReadOnlyList<T> a, IReadOnlyList<T> b, IEqualityComparer<T> eq,
        int prefix, int n, int m) {
        int max = n + m;
        int offset = max;
        var v = new int[(2 * max) + 1];
        var trace = new List<int[]>();

    search: for (int d = 0; d <= max; d++) {
            var snapshot = new int[(2 * d) + 1];
            Array.Copy(v, offset - d, snapshot, 0, snapshot.Length);
            trace.Add(snapshot);

            for (int k = -d; k <= d; k += 2) {
                int x = k == -d || (k != d && v[offset + k - 1] < v[offset + k + 1])
                    ? v[offset + k + 1]
                    : v[offset + k - 1] + 1;
                int y = x - k;
                while (x < n && y < m && eq.Equals(a[prefix + x], b[prefix + y])) {
                    x++;
                    y++;
                }

                v[offset + k] = x;
                if (x >= n && y >= m) break search;
            }
        }

        var script = new List<DiffOpKind>();
        int px = n;
        int py = m;
        for (int d = trace.Count - 1; d > 0; d--) {
            int[] vd = trace[d];
            int k = px - py;
            int prevK = k == -d || (k != d && vd[k - 1 + d] < vd[k + 1 + d]) ? k + 1 : k - 1;
            int prevX = vd[prevK + d];
            int prevY = prevX - prevK;
            while (px > prevX && py > prevY) {
                px--;
                py--;
                script.Add(DiffOpKind.Equal);
            }

            script.Add(px == prevX ? DiffOpKind.Insert : DiffOpKind.Delete);
            px = prevX;
            py = prevY;
        }

        while (px > 0 && py > 0) {
            px--;
            py--;
            script.Add(DiffOpKind.Equal);
        }

        return script;
    }
}
