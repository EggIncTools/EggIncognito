using System.Text.RegularExpressions;

namespace EggIncognito.Core.Services.ProtoExtract;

public static partial class MissionCatalogExtractor {
    public const string InitSymbol = "__GLOBAL__sub_I_missiondata";
    public const string SignatureString = "Hatch 200 chickens";

    [GeneratedRegex("^[a-z][a-z0-9_]{3,}$")]
    private static partial Regex IdPattern();

    [GeneratedRegex("^[A-Z][A-Z0-9 .!']+$")]
    private static partial Regex DisplayPattern();

    public static Result Extract(byte[] bin) {
        var img = BinaryImage.Load(bin);
        return ExtractWith(bin, img?.Symbols ?? []);
    }

    public static Result ExtractWith(byte[] bin, IReadOnlyList<MachoSymbols.Symbol> syms) {
        var scan = Arm64DataTableReader.ScanWith(bin, syms, [InitSymbol]);
        return !scan.Ok ? new Result(false, [], scan.Diagnostics) : Classify(bin, scan.Addresses);
    }

    public static Result ExtractAuto(byte[] bin) {
        var img = BinaryImage.Load(bin);
        if (img is ElfImage) {
            var loc = InitArrayLocator.Create(bin);
            if (loc is null || !loc.TryLocateByString(SignatureString, out var s, out var e))
                return new Result(false, [], "missiondata init not located via signature on ELF");
            return ExtractRange(bin, s, e);
        }

        return ExtractWith(bin, img?.Symbols ?? []);
    }

    public static Result ExtractRange(byte[] bin, ulong startVa, ulong endVa) {
        var scan = Arm64DataTableReader.ScanRange(bin, startVa, endVa);
        return !scan.Ok ? new Result(false, [], scan.Diagnostics) : Classify(bin, scan.Addresses);
    }

    private static Result Classify(byte[] bin, IReadOnlyList<Arm64DataTableReader.AddressRef> refs) {
        var img = BinaryImage.Load(bin);
        var entries = new List<MissionEntry>();
        string? pendingDisplay = null;
        int open = -1;
        string? prevStr = null;
        ulong prevVa = 0;

        foreach (var r in refs) {
            if (!BinaryStrings.IsStringSection(r.Section)) continue;
            string raw = BinaryStrings.ReadCstr(bin, img, r.Va, 128);
            if (string.IsNullOrEmpty(raw)) continue;
            string str = StripDescPrefix(raw);
            if (!IsPrintable(str)) continue;

            if (prevStr is not null && prevStr.EndsWith(str, StringComparison.Ordinal)
                                    && r.Va > prevVa && r.Va <= prevVa + (ulong)prevStr.Length + 1) {
                continue;
            }

            prevStr = str;
            prevVa = r.Va;

            if (IdPattern().IsMatch(str)) {
                entries.Add(new MissionEntry(str, pendingDisplay, null));
                open = entries.Count - 1;
                pendingDisplay = null;
            } else if (DisplayPattern().IsMatch(str)) {
                pendingDisplay = str;
                open = -1;
            } else if (open >= 0 && entries[open].Goal is null) {
                entries[open] = entries[open] with { Goal = str };
            }
        }

        return new Result(true, entries, $"{entries.Count} missions");
    }

    private static bool IsPrintable(string s) {
        if (s.Length < 2) return false;
        foreach (char c in s) {
            if (c is '\t' or '\n' or '\r' or (char)0x1b) continue;
            if (c is < (char)0x20 or > (char)0x7e) return false;
        }

        return true;
    }

    private static string StripDescPrefix(string s) {
        string t = s;
        if (t.Length > 0 && t[0] == (char)0x1b) t = t[1..];
        return t;
    }

    public readonly record struct MissionEntry(string Id, string? DisplayName, string? Goal);

    public readonly record struct Result(bool Ok, IReadOnlyList<MissionEntry> Entries, string Diagnostics);
}
