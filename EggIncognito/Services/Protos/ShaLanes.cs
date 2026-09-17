using EggIncognito.Core.Services.ProtoExtract;

namespace EggIncognito.Services.Protos;

public enum ShaLaneMark { None, Start, Node, End, Pass }

public sealed record ShaLaneSegment(int Lane, ShaLaneMark Mark, int GroupId);

public sealed record ShaLaneRow(IReadOnlyList<ShaLaneSegment> Segments, int GroupSize, int VisibleGroupSize, int? GroupId);

public sealed record ShaLaneLayout(IReadOnlyDictionary<long, ShaLaneRow> Rows, IReadOnlyDictionary<int, string> GroupShas);

public static class ShaLanes {
    public static ShaLaneLayout Build(
        IReadOnlyList<long> orderedKeys, IReadOnlyList<VersionKey> orderedVisible,
        IReadOnlyList<VersionKey> all, int maxLanes = 4) {
        var rows = new Dictionary<long, ShaLaneRow>();
        var groupShas = new Dictionary<int, string>();
        if (orderedKeys.Count != orderedVisible.Count) return new ShaLaneLayout(rows, groupShas);

        List<List<int>> groups = GroupIndexes(orderedVisible);
        var segments = new List<ShaLaneSegment>[orderedVisible.Count];
        for (var ix = 0; ix < segments.Length; ix++) segments[ix] = [];

        var rowGroupId = new int?[orderedVisible.Count];
        var laneEnd = new List<int>();
        var nextGroupId = 0;
        foreach (List<int> group in groups.Where(g => g.Count > 1).OrderBy(g => g[0])) {
            int first = group[0];
            int last = group[^1];
            int lane = FreeLane(laneEnd, first, last, maxLanes);
            if (lane < 0) continue;

            int groupId = nextGroupId++;
            groupShas[groupId] = orderedVisible[first].ProtoSha ?? "";

            var members = new HashSet<int>(group);
            for (int ix = first; ix <= last; ix++) {
                bool isMember = members.Contains(ix);
                ShaLaneMark mark = ix == first ? ShaLaneMark.Start
                    : ix == last ? ShaLaneMark.End
                    : isMember ? ShaLaneMark.Node
                    : ShaLaneMark.Pass;
                segments[ix].Add(new ShaLaneSegment(lane, mark, groupId));
                if (isMember) rowGroupId[ix] = groupId;
            }
        }

        var visibleSize = new int[orderedVisible.Count];
        foreach (List<int> group in groups) {
            foreach (int ix in group) visibleSize[ix] = group.Count;
        }

        int[] totalSize = TotalSizes(orderedVisible, all);
        for (var ix = 0; ix < orderedKeys.Count; ix++) {
            rows[orderedKeys[ix]] = new ShaLaneRow(segments[ix], totalSize[ix], visibleSize[ix], rowGroupId[ix]);
        }

        return new ShaLaneLayout(rows, groupShas);
    }

    public static int LaneCount(ShaLaneLayout layout) => LaneCount(layout.Rows);

    public static int LaneCount(IReadOnlyDictionary<long, ShaLaneRow> rows) {
        var top = -1;
        foreach (ShaLaneRow row in rows.Values) {
            foreach (ShaLaneSegment segment in row.Segments) {
                if (segment.Lane > top) top = segment.Lane;
            }
        }

        return top + 1;
    }

    private static int FreeLane(List<int> laneEnd, int first, int last, int maxLanes) {
        for (var lane = 0; lane < laneEnd.Count; lane++) {
            if (laneEnd[lane] >= first) continue;
            laneEnd[lane] = last;
            return lane;
        }

        if (laneEnd.Count >= maxLanes) return -1;

        laneEnd.Add(last);
        return laneEnd.Count - 1;
    }

    private static List<List<int>> GroupIndexes(IReadOnlyList<VersionKey> keys) {
        var groups = new List<List<int>>();
        var leaders = new List<VersionKey>();
    scan: for (var ix = 0; ix < keys.Count; ix++) {
            VersionKey key = keys[ix];
            if (string.IsNullOrWhiteSpace(key.ProtoSha)) continue;

            for (var g = 0; g < leaders.Count; g++) {
                if (!ProtoVersionTranslator.SharesProtoSha(leaders[g], key)) continue;

                groups[g].Add(ix);
                continue scan;
            }

            leaders.Add(key);
            groups.Add([ix]);
        }

        return groups;
    }

    private static int[] TotalSizes(IReadOnlyList<VersionKey> visible, IReadOnlyList<VersionKey> all) {
        List<List<int>> groups = GroupIndexes(all);
        var sizes = new int[visible.Count];
        for (var ix = 0; ix < visible.Count; ix++) {
            VersionKey key = visible[ix];
            if (string.IsNullOrWhiteSpace(key.ProtoSha)) continue;

            foreach (List<int> group in groups) {
                if (!ProtoVersionTranslator.SharesProtoSha(all[group[0]], key)) continue;

                sizes[ix] = group.Count;
                break;
            }
        }

        return sizes;
    }
}
