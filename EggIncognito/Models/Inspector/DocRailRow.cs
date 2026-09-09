using EggIncognito.Services;

namespace EggIncognito.Models.Inspector;

public sealed record DocRailRow(
    string Key,
    DocSubject? Leaf,
    string Label,
    int Depth,
    bool HasChildren,
    bool Expanded,
    bool Locked);
