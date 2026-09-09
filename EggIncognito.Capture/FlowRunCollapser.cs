namespace EggIncognito.Capture;

public sealed record FlowRun(int Start, int Count, DashboardFlow First, DashboardFlow Last) {
    public long Id => First.Id;
    public bool Folded => Count > 1;
}

public static class FlowRunCollapser {
    public const int MinRun = 3;

    public static bool Folds(DashboardFlow head, DashboardFlow flow) =>
        string.Equals(head.Path, flow.Path, StringComparison.Ordinal)
        && head.Status == flow.Status
        && string.Equals(head.RequestType, flow.RequestType, StringComparison.Ordinal)
        && string.Equals(head.ResponseType, flow.ResponseType, StringComparison.Ordinal);

    public static List<FlowRun> Fold(IReadOnlyList<DashboardFlow> flows, int minRun = MinRun) {
        var runs = new List<FlowRun>();
        int start = 0;
        while (start < flows.Count) {
            int end = start + 1;
            while (end < flows.Count && Folds(flows[start], flows[end])) end++;
            int count = end - start;
            if (count >= minRun) {
                runs.Add(new FlowRun(start, count, flows[start], flows[end - 1]));
            } else {
                for (int i = start; i < end; i++) runs.Add(new FlowRun(i, 1, flows[i], flows[i]));
            }

            start = end;
        }

        return runs;
    }
}
