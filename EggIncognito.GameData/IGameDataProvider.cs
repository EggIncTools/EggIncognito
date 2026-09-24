using System.Diagnostics.CodeAnalysis;

namespace EggIncognito.GameData;

public interface IGameDataProvider {
    IReadOnlyList<IEffectFamily> Families { get; }
    IColleggtibleCatalog Colleggtibles { get; }
    IBoostCatalog BoostCatalog { get; }
    IEggCatalog EggCatalog { get; }
    IDimensionCatalog Dimensions { get; }
    IMissionCatalog Missions { get; }
    IVehicleCatalog Vehicles { get; }
    IEffectFamily? Family(string key);
    Effect? Resolve(string family, string id);
    bool TryResolve(string family, string id, [NotNullWhen(true)] out Effect? effect);
    IReadOnlyList<Effect> All(string family);
    IReadOnlyList<Effect> ByTarget(EffectTarget target);
    double Effective(EffectTarget target, double seed, IReadOnlyDictionary<string, int> idLevels);
}
