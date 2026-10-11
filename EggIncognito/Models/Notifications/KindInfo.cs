using EggIncognito.Services.Feed;

namespace EggIncognito.Models.Notifications;

public record KindInfo(
    string Key,
    string Label,
    string Description,
    List<TriggerOpt> Triggers,
    List<FeedVarInfo> Vars,
    string DefaultTrigger,
    bool PlatformScoped,
    List<FilterOpt> Filters);
