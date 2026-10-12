namespace EggIncognito.Data.Models;

public static class ApiKeyFeatureStates {
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Denied = "denied";
    public const string Revoked = "revoked";

    public static string? ForDecision(string verb) => verb switch {
        "approve" => Approved,
        "deny" => Denied,
        "revoke" => Revoked,
        _ => null
    };

    public static bool CanDecide(string current, string target) => (current, target) switch {
        (Pending, Approved) or (Pending, Denied) or (Approved, Revoked) => true,
        _ => false
    };

    public static bool Requestable(string? current) => current is not Approved;
}
