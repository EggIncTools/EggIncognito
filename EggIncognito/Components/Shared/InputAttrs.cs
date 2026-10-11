namespace EggIncognito.Components.Shared;

public static class InputAttrs {
    public static readonly IReadOnlyDictionary<string, object> NoAutofill = new Dictionary<string, object> {
        ["autocomplete"] = "one-time-code",
        ["autocorrect"] = "off",
        ["autocapitalize"] = "off",
        ["spellcheck"] = "false",
        ["data-1p-ignore"] = "true",
        ["data-lpignore"] = "true",
        ["data-bwignore"] = "true",
        ["data-form-type"] = "other",
        ["data-protonpass-ignore"] = "true"
    };
}
