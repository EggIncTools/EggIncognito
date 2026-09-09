namespace EggIncognito.Services.Inspector;

public sealed class RailExpansion {
    private readonly HashSet<string> _userOpen = [with(StringComparer.Ordinal)];
    private readonly HashSet<string> _userClosed = [with(StringComparer.Ordinal)];
    private readonly HashSet<string> _auto = [with(StringComparer.Ordinal)];
    private string? _followed;
    private bool _started;

    public bool IsOpen(string key) => !_userClosed.Contains(key) && (_userOpen.Contains(key) || _auto.Contains(key));

    public void Toggle(string key) {
        if (IsOpen(key)) {
            _userOpen.Remove(key);
            if (_auto.Contains(key)) _userClosed.Add(key);
            return;
        }

        _userClosed.Remove(key);
        _userOpen.Add(key);
    }

    public bool Follow(string? selection, IEnumerable<string> ancestors) {
        if (_started && selection == _followed) return false;
        _started = true;
        _followed = selection;
        _auto.Clear();
        _userClosed.Clear();
        foreach (string a in ancestors) _auto.Add(a);
        return true;
    }
}
