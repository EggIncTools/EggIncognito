using System.Text;
using System.Text.Json;
using EggIncognito.Core.Services;

namespace EggIncognito.Capture;

public sealed class HarWriter {
    private static readonly JsonSerializerOptions IndentedJson = JsonPresets.Indented;
    private readonly List<object> _entries = [];

    private readonly Lock _gate = new();

    public int Count {
        get {
            lock (_gate) return _entries.Count;
        }
    }

    public void Add(CapturedFlow flow) {
        object[] requestParams = flow.RequestDataB64 is null
            ? []
            : [new { name = "data", value = flow.RequestDataB64 }];

        var entry = new {
            request = new {
                method = flow.Method,
                url = flow.Url,
                headers = HarHeaders(flow.RequestHeaders),
                postData = new {
                    mimeType = "application/x-www-form-urlencoded",
                    @params = requestParams
                }
            },
            response = new {
                status = flow.Status,
                headers = HarHeaders(flow.ResponseHeaders),
                content = new {
                    text = flow.ResponseBodyB64
                }
            }
        };

        lock (_gate) _entries.Add(entry);
    }

    private static object[] HarHeaders(IReadOnlyList<HttpHeader>? headers) =>
        headers is null ? [] : [.. headers.Select(h => new { name = h.Name, value = h.Value })];

    public string ToHar() {
        object[] entries;
        lock (_gate) entries = [.. _entries];

        var har = new {
            log = new {
                version = "1.2",
                creator = new { name = "EggIncognito.Capture", version = "1.0" },
                entries
            }
        };
        return JsonSerializer.Serialize(har, IndentedJson);
    }

    public void Save(string path) {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, ToHar(), new UTF8Encoding(false));
    }
}
