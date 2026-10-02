namespace EggIncognito.Models.Registry;

public sealed record MergeSuggestion(string ClientVersion, string ProtoSha, List<MergeMember> Members);
