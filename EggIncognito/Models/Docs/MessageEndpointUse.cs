using EggIncognito.Core.Services;

namespace EggIncognito.Models.Docs;

public sealed record MessageEndpointUse(string Path, MessageUseRole Role, RouteInfo? Route, bool Locked);
