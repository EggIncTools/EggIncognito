using EggIncognito.Services;

namespace EggIncognito.Models.Inspector;

public sealed record DocRailRow(string GroupTitle, DocSubject? Leaf, int Depth, bool HasChildren, bool Expanded);
