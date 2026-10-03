using EggIncognito.Core.Services.Syntax;

namespace EggIncognito.Components.Shared.Code;

public sealed class CodeRevealState {
#pragma warning disable IDE0028
    private readonly HashSet<string> _revealed = new(StringComparer.Ordinal);
#pragma warning restore IDE0028

    public string? Scope { get; private set; }

    public bool IsRevealed(string? key) => key is not null && _revealed.Contains(key);

    public void Toggle(string? key) {
        if (key is null) return;
        if (!_revealed.Remove(key)) _revealed.Add(key);
    }

    public void Clear() => _revealed.Clear();

    public bool ResetFor(string? scope) {
        if (string.Equals(Scope, scope, StringComparison.Ordinal)) return false;
        Scope = scope;
        _revealed.Clear();
        return true;
    }

    public string Class(string? key) => IsRevealed(key) ? TokenClasses.Blur + " revealed" : TokenClasses.Blur;
}
