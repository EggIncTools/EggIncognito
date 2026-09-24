using System.Collections;
using System.Collections.Frozen;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace EggIncognito.Core.Services;

public static class ProtoVolatileScrub {
    private static readonly FrozenSet<string> Countdowns = new[] {
        "seconds_remaining",
        "seconds_until_available",
        "shells_showcase_last_featured_time",
        "popularity"
    }.ToFrozenSet(StringComparer.Ordinal);

    public static T Scrubbed<T>(T message) where T : IMessage<T> {
        var clone = message.Clone();
        Scrub(clone);
        return clone;
    }

    public static void Scrub(IMessage message) {
        foreach (var field in message.Descriptor.Fields.InFieldNumberOrder()) {
            if (Countdowns.Contains(field.Name)) {
                field.Accessor.Clear(message);
                continue;
            }

            if (field.FieldType != FieldType.Message || field.IsMap) continue;
            if (field.IsRepeated) {
                if (field.Accessor.GetValue(message) is not IList list) continue;
                foreach (object? item in list) {
                    if (item is IMessage child) Scrub(child);
                }

                continue;
            }

            if (field.Accessor.HasValue(message) && field.Accessor.GetValue(message) is IMessage single)
                Scrub(single);
        }
    }
}
