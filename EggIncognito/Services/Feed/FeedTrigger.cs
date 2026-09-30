using EggIncognito.Core.Services.ProtoExtract;

namespace EggIncognito.Services.Feed;

public static class FeedTrigger {
    public static bool Matches(
        string trigger, bool created, bool protoChanged, VersionDelta delta, bool flawed,
        IReadOnlyList<string> subPlatforms, string evtPlatform) {
        if (!subPlatforms.Contains(evtPlatform)) return false;
        if (delta != VersionDelta.Forward) return false;

        return trigger switch {
            FeedEventKinds.TriggerVersionUp => true,
            FeedEventKinds.TriggerProtoChanged => protoChanged,
            FeedEventKinds.TriggerSuspect => flawed,
            _ => created
        };
    }
}
