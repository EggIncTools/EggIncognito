using System.Text.Json;
using EggIncognito.Services.Feed;

namespace EggIncognito.Models.Notifications;

public record KindInfo(
    string Key,
    string Label,
    string Description,
    JsonElement Catalog,
    List<FeedVarInfo> Vars);
