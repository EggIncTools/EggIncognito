using System.Text.Json;
using System.Text.Json.Nodes;

namespace EggIncognito.Services.Feed;

public static class DiscordFeedPayload {
    public const string TestNotice = "EggIncognito feed test. Sample data, not a real release.";

    public static string Build(INotificationEvent evt, string? messageTemplate = null) {
        if (!string.IsNullOrWhiteSpace(messageTemplate))
            return JsonSerializer.Serialize(new { content = FeedTemplate.Render(messageTemplate, evt.Vars()) });

        var embed = evt.Embed();
        return JsonSerializer.Serialize(new {
            embeds = new[] {
                new {
                    title = embed.Title,
                    url = embed.Url,
                    color = embed.Color,
                    fields = embed.Fields.Select(f => new { name = f.Name, value = f.Value, inline = f.Inline }).ToArray()
                }
            }
        });
    }

    public static string MarkAsTest(string body) {
        JsonNode? node;
        try {
            node = JsonNode.Parse(body);
        } catch (JsonException) {
            return body;
        }

        if (node is not JsonObject obj) return body;

        string existing = obj["content"] is JsonValue value && value.TryGetValue<string>(out var text) ? text : "";
        obj["content"] = existing.Length > 0 ? $"{TestNotice}\n{existing}" : TestNotice;

        if (obj["embeds"] is JsonArray embeds) {
            foreach (var entry in embeds) {
                if (entry is JsonObject embed) embed["footer"] = new JsonObject { ["text"] = TestNotice };
            }
        }

        return obj.ToJsonString();
    }
}
