using System.Security.Claims;

namespace Onepunch.Common.Lib.Subscriptions;

public enum SubscriptionAccessDecision
{
    Allow,
    /// <summary>Refuse with <see cref="SubscriptionProblemTypes.ReadOnly"/>.</summary>
    ReadOnly,
    /// <summary>Refuse with <see cref="SubscriptionProblemTypes.Locked"/>.</summary>
    Locked,
}

/// <summary>
/// What a lapsed subscription (Expired, Suspended, Canceled) allows, the one rule every service
/// applies: the owner and billing admins keep read-only access -- to look, export, and pay --
/// while every other member is locked out of the company entirely, reads included.
/// </summary>
public static class SubscriptionAccessPolicy
{
    public static SubscriptionAccessDecision Decide(EntitlementSnapshot snapshot, string httpMethod, bool isBillingUser)
    {
        // An operator's suspension or legal hold stops everyone, the owner and billing admins too.
        if (snapshot.IsBlocked) return SubscriptionAccessDecision.Locked;
        if (!snapshot.IsReadOnly) return SubscriptionAccessDecision.Allow;
        if (!isBillingUser) return SubscriptionAccessDecision.Locked;
        return IsRead(httpMethod) ? SubscriptionAccessDecision.Allow : SubscriptionAccessDecision.ReadOnly;
    }

    /// <summary>The exception for a refusal, or null when allowed.</summary>
    public static PlanRestrictionException? Refusal(SubscriptionAccessDecision decision) => decision switch
    {
        SubscriptionAccessDecision.ReadOnly => PlanRestrictionException.ReadOnly(
            "Your subscription is read-only, so changes can't be saved. Choose a plan in Billing to continue."),
        SubscriptionAccessDecision.Locked => PlanRestrictionException.Locked(),
        _ => null,
    };

    private static bool IsRead(string method) =>
        string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase)
        || string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase)
        || string.Equals(method, "OPTIONS", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Who may see and manage the account's billing in the current company: the Owner, or anyone
/// granted "Billing:View" (a billing admin). Read straight from the token's claims -- the usual
/// permission helpers let Admin through everything, and billing is deliberately not an Admin right.
/// </summary>
public static class BillingAccess
{
    public const string ViewPermission = "Billing:View";

    public static bool IsBillingUser(ClaimsPrincipal user) =>
        user.FindAll(ClaimTypes.Role).Any(c => c.Value == "Owner")
        || user.FindAll("role").Any(c => c.Value == "Owner")
        || user.FindAll("permission").Any(c => c.Value == ViewPermission);
}
