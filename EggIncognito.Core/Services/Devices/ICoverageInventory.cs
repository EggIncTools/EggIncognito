namespace EggIncognito.Core.Services.Devices;

public interface ICoverageInventory {
    double? Held(string? deviceId, string family, string level, string rarity);
}
