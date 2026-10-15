using System.Globalization;
using EggIncognito.Services.Events;

namespace EggIncognito.Services.Predictions;

public static class HistoryCsv {
    public static List<ContractReleaseSample> Contracts(Stream stream) {
        using var reader = new StreamReader(stream);
        var header = ReadRow(reader);
        if (header is null) return [];
        var ix = IndexOf(header, "contract_id", "name", "egg", "custom_egg_id", "start_time", "length_seconds",
            "leggacy", "ultra_only", "prophecy_eggs");

        var rows = new List<ContractReleaseSample>();
        while (ReadRow(reader) is { } row) {
            if (row.Count < header.Count) continue;
            string raw = Field(row, ix[3]);
            string? customEggId = string.IsNullOrWhiteSpace(raw) || string.Equals(raw, "NULL", StringComparison.OrdinalIgnoreCase)
                ? null
                : raw;
            rows.Add(new ContractReleaseSample(
                Field(row, ix[0]),
                Field(row, ix[1]),
                ParseTimestamp(Field(row, ix[4])),
                ParseDouble(Field(row, ix[5])),
                ParseBool(Field(row, ix[6])),
                (int)ParseDouble(Field(row, ix[8])),
                ParseBool(Field(row, ix[7])),
                ParseInt(Field(row, ix[2])),
                customEggId));
        }

        return rows;
    }

    public static List<EventRow> GameEvents(Stream stream) {
        using var reader = new StreamReader(stream);
        var header = ReadRow(reader);
        if (header is null) return [];
        var ix = IndexOf(header, "event_type", "ultra", "start_time", "end_time", "multiplier");

        var rows = new List<EventRow>();
        while (ReadRow(reader) is { } row) {
            if (row.Count < header.Count) continue;
            rows.Add(new EventRow(
                Field(row, ix[0]),
                ParseBool(Field(row, ix[1])),
                ParseTimestamp(Field(row, ix[2])),
                ParseTimestamp(Field(row, ix[3])),
                ParseDouble(Field(row, ix[4]))));
        }

        return rows;
    }

    private static string Field(List<string> row, int index) => index >= 0 && index < row.Count ? row[index] : "";

    private static int[] IndexOf(List<string> header, params string[] names) {
        var map = header.Select((h, i) => (h, i)).ToDictionary(x => x.h, x => x.i, StringComparer.OrdinalIgnoreCase);
        return [.. names.Select(n => map.GetValueOrDefault(n, -1))];
    }

    private static double ParseTimestamp(string? value) {
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "NULL", StringComparison.OrdinalIgnoreCase)) return 0;
        var parsed = DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
        return UnixSeconds.FromTime(parsed);
    }

    private static double ParseDouble(string? value) {
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "NULL", StringComparison.OrdinalIgnoreCase)) return 0;
        return double.Parse(value, CultureInfo.InvariantCulture);
    }

    private static int ParseInt(string? value) {
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "NULL", StringComparison.OrdinalIgnoreCase)) return 0;
        return (int)double.Parse(value, CultureInfo.InvariantCulture);
    }

    private static bool ParseBool(string? value) =>
        string.Equals(value, "True", StringComparison.OrdinalIgnoreCase) || value == "1" || value == "t";

    private static List<string>? ReadRow(StreamReader reader) {
        var line = reader.ReadLine();
        if (line is null) return null;
        var fields = new List<string>();
        var current = new System.Text.StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++) {
            char c = line[i];
            if (quoted) {
                if (c == '"') {
                    if (i + 1 < line.Length && line[i + 1] == '"') {
                        current.Append('"');
                        i++;
                    } else {
                        quoted = false;
                    }
                } else {
                    current.Append(c);
                }
            } else if (c == '"') {
                quoted = true;
            } else if (c == ',') {
                fields.Add(current.ToString());
                current.Clear();
            } else {
                current.Append(c);
            }
        }

        fields.Add(current.ToString());
        return fields;
    }
}
