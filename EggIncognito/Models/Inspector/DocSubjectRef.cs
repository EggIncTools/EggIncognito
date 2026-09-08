using EggIncognito.Services.Inspector;

namespace EggIncognito.Models.Inspector;

public sealed record DocSubjectRef(DocSubjectKind Kind, string Key) {
    public string Slug => DocSubjectKinds.Slug(Kind);

    public static bool TryParse(string? slug, string? key, out DocSubjectRef result) {
        result = null!;
        if (!DocSubjectKinds.IsKnown(slug) || string.IsNullOrEmpty(key)) return false;
        result = new DocSubjectRef(DocSubjectKinds.Parse(slug), key);
        return true;
    }
}
