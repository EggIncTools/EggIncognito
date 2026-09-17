namespace EggIncognito.Models.Devices;

public sealed record UiStateInfo(bool Awake, bool Locked, int? NavMode = null);
