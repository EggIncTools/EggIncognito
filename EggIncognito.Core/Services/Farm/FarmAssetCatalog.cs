using System.Runtime.CompilerServices;
using EggIncognito.Core.Services.ProtoExtract;
using Ei;
using AssetType = Ei.ShellSpec.Types.AssetType;
using FarmElement = Ei.ShellDB.Types.FarmElement;

namespace EggIncognito.Core.Services.Farm;

public sealed record FarmMeshPiece(
    AssetType AssetType,
    string Stem,
    string? BaseStem,
    string? ShellIdentifier,
    string? Url,
    string? Checksum) {
    public bool IsShell => ShellIdentifier is not null;
}

public sealed record FarmShellRef(
    string Identifier,
    string? Name,
    string? SetIdentifier,
    AssetType PrimaryAssetType,
    bool ModifiedGeometry,
    bool DefaultAppearance,
    bool IsObject);

public sealed class FarmAssetCatalog {
    private static readonly AssetType[] NoTypes = [];
    private static readonly FarmShellRef[] NoShells = [];
    private static readonly string[] NoIdentifiers = [];

    private static readonly ConditionalWeakTable<DLCCatalog, FarmAssetCatalog> Instances = [];

    private static readonly Dictionary<FarmElement, IReadOnlyList<AssetType>> TypesByElement = BuildTypesByElement();

    public static readonly FarmAssetCatalog Empty = new(new DLCCatalog());

    private readonly Dictionary<string, ShellEntry> _byIdentifier = [with(StringComparer.Ordinal)];
    private readonly Dictionary<AssetType, List<FarmShellRef>> _shellsByType = [];
    private readonly Dictionary<AssetType, string> _baseStems = [];
    private readonly Dictionary<string, AssetType> _typeByStem = [with(StringComparer.Ordinal)];
    private readonly Dictionary<AssetType, IReadOnlyList<AssetType>> _subPieceTypes = [];
    private readonly Dictionary<AssetType, IReadOnlyList<string>> _defaultShells = [];
    private readonly List<AssetType> _knownTypes = [];

    private FarmAssetCatalog(DLCCatalog catalog) {
        var votes = new Dictionary<AssetType, Dictionary<string, int>>();
        var subs = new Dictionary<AssetType, SortedSet<AssetType>>();
        var defaults = new Dictionary<AssetType, List<string>>();

        foreach (var spec in catalog.Shells) {
            string id = spec.Identifier ?? "";
            if (id.Length == 0 || _byIdentifier.ContainsKey(id)) continue;
            var primary = spec.PrimaryPiece ?? spec.Pieces.FirstOrDefault();
            if (primary is null) continue;

            string? set = NullIfEmpty(spec.SetIdentifier);
            var pieces = new List<FarmMeshPiece>();

            AddPiece(pieces, primary.AssetType, StemOf(primary.Dlc, id), id, primary.Dlc);
            AddVote(votes, primary.AssetType, StripSetSuffix(id, set));

            foreach (var piece in spec.Pieces) {
                if (ReferenceEquals(piece, primary)) continue;
                string stem = StemOf(piece.Dlc, id);
                if (!AddPiece(pieces, piece.AssetType, stem, id, piece.Dlc)) continue;
                AddVote(votes, piece.AssetType, StripSetSuffix(StripIdentifierPrefix(stem, id), set));
                if (piece.AssetType != primary.AssetType) AddSub(subs, primary.AssetType, piece.AssetType);
            }

            var shell = new FarmShellRef(id, NullIfEmpty(spec.Name), set, primary.AssetType, spec.ModifiedGeometry,
                spec.DefaultAppearance, false);
            Register(shell, pieces);
            if (spec.DefaultAppearance) AddDefault(defaults, primary.AssetType, id);
        }

        foreach (var spec in catalog.ShellObjects) {
            string id = spec.Identifier ?? "";
            if (id.Length == 0 || _byIdentifier.ContainsKey(id)) continue;
            var piece = spec.Pieces.Where(p => p.Dlc is not null).OrderBy(p => p.Lod).FirstOrDefault();
            if (piece?.Dlc is null) continue;

            var pieces = new List<FarmMeshPiece>();
            AddPiece(pieces, spec.AssetType, StemOf(piece.Dlc, id), id, piece.Dlc);
            var shell = new FarmShellRef(id, NullIfEmpty(spec.Name), null, spec.AssetType, false,
                spec.DefaultAppearance, true);
            Register(shell, pieces);
            if (spec.DefaultAppearance) AddDefault(defaults, spec.AssetType, id);
        }

        foreach (var (type, counts) in votes) {
            string? best = Majority(counts);
            if (best is { Length: > 0 }) _baseStems[type] = best;
        }

        foreach (var type in _baseStems.Keys.OrderBy(static t => (int)t)) {
            _knownTypes.Add(type);
            _typeByStem.TryAdd(_baseStems[type], type);
        }

        foreach (var (type, list) in subs) _subPieceTypes[type] = [.. list];
        foreach (var (type, list) in defaults) {
            list.Sort(StringComparer.Ordinal);
            _defaultShells[type] = list;
        }

        foreach (var (_, entry) in _byIdentifier) {
            for (int i = 0; i < entry.Pieces.Count; i++) {
                var piece = entry.Pieces[i];
                entry.Pieces[i] = piece with { BaseStem = _baseStems.GetValueOrDefault(piece.AssetType) };
            }
        }
    }

    public static FarmAssetCatalog From(DLCCatalog? catalog) =>
        catalog is null ? Empty : Instances.GetValue(catalog, static c => new FarmAssetCatalog(c));

    public static FarmAssetCatalog From(ConfigResponse? config) => From(config?.DlcCatalog);

    public IReadOnlyDictionary<AssetType, string> BaseStems => _baseStems;

    public IReadOnlyList<AssetType> KnownAssetTypes => _knownTypes;

    public IReadOnlyCollection<string> KnownStems => _typeByStem.Keys;

    public IReadOnlyDictionary<AssetType, IReadOnlyList<string>> DefaultShells => _defaultShells;

    public string? BaseStem(AssetType type) => _baseStems.GetValueOrDefault(type);

    public bool IsKnownStem(string? stem) => stem is not null && _typeByStem.ContainsKey(stem);

    public AssetType? AssetTypeForStem(string? stem) =>
        stem is not null && _typeByStem.TryGetValue(stem, out var type) ? type : null;

    public IReadOnlyList<AssetType> SubPieceTypes(AssetType type) =>
        _subPieceTypes.TryGetValue(type, out var list) ? list : NoTypes;

    public IReadOnlyList<FarmShellRef> ShellsFor(AssetType type) =>
        _shellsByType.TryGetValue(type, out var list) ? list : NoShells;

    public FarmShellRef? ShellById(string? identifier) =>
        identifier is not null && _byIdentifier.TryGetValue(identifier, out var entry) ? entry.Shell : null;

    public string? DefaultShellFor(AssetType type) =>
        _defaultShells.TryGetValue(type, out var list) && list.Count == 1 ? list[0] : null;

    public IReadOnlyList<string> DefaultShellsFor(AssetType type) =>
        _defaultShells.TryGetValue(type, out var list) ? list : NoIdentifiers;

    public IReadOnlyList<FarmMeshPiece> Resolve(AssetType type, string? shellIdentifier = null) {
        var result = new List<FarmMeshPiece>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        Append(result, seen, type, shellIdentifier);
        foreach (var sub in SubPieceTypes(type)) Append(result, seen, sub, shellIdentifier);
        return result;
    }

    public IReadOnlyList<FarmMeshPiece> ResolveSlot(AssetType type, string? shellIdentifier = null) {
        var result = new List<FarmMeshPiece>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        Append(result, seen, type, shellIdentifier);
        return result;
    }

    public IReadOnlyList<string> ResolveStems(AssetType type, string? shellIdentifier = null) =>
        [.. Resolve(type, shellIdentifier).Select(static p => p.Stem)];

    public static IReadOnlyList<AssetType> AssetTypesForElement(FarmElement element) =>
        TypesByElement.TryGetValue(element, out var list) ? list : NoTypes;

    public static FarmElement ElementOf(AssetType type) => (int)type switch {
        >= 1 and <= 19 => FarmElement.HenHouse,
        >= 50 and <= 59 => FarmElement.Silo,
        70 or 600 => FarmElement.Mailbox,
        71 => FarmElement.TrophyCase,
        72 => FarmElement.Ground,
        73 => FarmElement.Hardscape,
        74 or 570 => FarmElement.Hyperloop,
        >= 100 and <= 106 => FarmElement.Depot,
        >= 110 and <= 115 => FarmElement.Lab,
        >= 120 and <= 164 or >= 500 and <= 554 => FarmElement.Hatchery,
        >= 170 and <= 172 => FarmElement.Hoa,
        >= 180 and <= 182 => FarmElement.MissionControl,
        >= 200 and <= 203 => FarmElement.FuelTank,
        1000 => FarmElement.Chicken,
        1010 => FarmElement.Hat,
        _ => FarmElement.Unknown
    };

    private void Append(List<FarmMeshPiece> into, HashSet<string> seen, AssetType type, string? shellIdentifier) {
        if (AppendFrom(into, seen, type, shellIdentifier)) return;
        if (AppendFrom(into, seen, type, DefaultShellFor(type))) return;
        if (_baseStems.TryGetValue(type, out string? stem) && seen.Add(stem)) into.Add(new FarmMeshPiece(type, stem, stem, null, null, null));
    }

    private bool AppendFrom(List<FarmMeshPiece> into, HashSet<string> seen, AssetType type, string? shellIdentifier) {
        if (shellIdentifier is null || !_byIdentifier.TryGetValue(shellIdentifier, out var entry)) return false;

        bool matched = false;
        foreach (var piece in entry.Pieces) {
            if (piece.AssetType != type) continue;
            matched = true;
            if (seen.Add(piece.Stem)) into.Add(piece);
        }

        return matched;
    }

    private void Register(FarmShellRef shell, List<FarmMeshPiece> pieces) {
        _byIdentifier[shell.Identifier] = new ShellEntry(shell, pieces);
        foreach (var type in pieces.Select(static p => p.AssetType).Distinct()) {
            if (!_shellsByType.TryGetValue(type, out var list)) _shellsByType[type] = list = [];
            list.Add(shell);
        }
    }

    private static bool AddPiece(List<FarmMeshPiece> pieces, AssetType type, string stem, string shellIdentifier,
        DLCItem? dlc) {
        if (stem.Length == 0) return false;
        foreach (var existing in pieces) {
            if (existing.AssetType == type && string.Equals(existing.Stem, stem, StringComparison.Ordinal))
                return false;
        }

        pieces.Add(new FarmMeshPiece(type, stem, null, shellIdentifier, ShellCatalog.AssetUrl(dlc),
            NullIfEmpty(dlc?.Checksum)));
        return true;
    }

    private static void AddVote(Dictionary<AssetType, Dictionary<string, int>> votes, AssetType type, string stem) {
        if (stem.Length == 0) return;
        if (!votes.TryGetValue(type, out var counts)) votes[type] = counts = [with(StringComparer.Ordinal)];

        counts[stem] = counts.GetValueOrDefault(stem) + 1;
    }

    private static void AddSub(Dictionary<AssetType, SortedSet<AssetType>> subs, AssetType parent, AssetType child) {
        if (!subs.TryGetValue(parent, out var set)) subs[parent] = set = [];
        set.Add(child);
    }

    private static void AddDefault(Dictionary<AssetType, List<string>> defaults, AssetType type, string identifier) {
        if (!defaults.TryGetValue(type, out var list)) defaults[type] = list = [];
        list.Add(identifier);
    }

    private static string? Majority(Dictionary<string, int> counts) {
        string? best = null;
        int bestCount = -1;
        foreach (var (stem, count) in counts) {
            if (count < bestCount) continue;
            if (count == bestCount && string.CompareOrdinal(stem, best) >= 0) continue;
            best = stem;
            bestCount = count;
        }

        return best;
    }

    private static string StemOf(DLCItem? dlc, string fallback) =>
        dlc is not null && !string.IsNullOrEmpty(dlc.Name) ? dlc.Name : fallback;

    private static string StripIdentifierPrefix(string stem, string identifier) =>
        stem.Length > identifier.Length + 1
        && stem.StartsWith(identifier, StringComparison.Ordinal)
        && stem[identifier.Length] == '_'
            ? stem[(identifier.Length + 1)..]
            : stem;

    private static string StripSetSuffix(string stem, string? set) =>
        set is { Length: > 0 }
        && stem.Length > set.Length + 1
        && stem.EndsWith(set, StringComparison.Ordinal)
        && stem[stem.Length - set.Length - 1] == '_'
            ? stem[..^(set.Length + 1)]
            : stem;

    private static Dictionary<FarmElement, IReadOnlyList<AssetType>> BuildTypesByElement() {
        var grouped = new Dictionary<FarmElement, List<AssetType>>();
        foreach (var type in Enum.GetValues<AssetType>()) {
            var element = ElementOf(type);
            if (element == FarmElement.Unknown) continue;
            if (!grouped.TryGetValue(element, out var list)) grouped[element] = list = [];
            list.Add(type);
        }

        var result = new Dictionary<FarmElement, IReadOnlyList<AssetType>>();
        foreach (var (element, list) in grouped) {
            list.Sort(static (a, b) => ((int)a).CompareTo((int)b));
            result[element] = list;
        }

        return result;
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;

    private sealed record ShellEntry(FarmShellRef Shell, List<FarmMeshPiece> Pieces);
}
