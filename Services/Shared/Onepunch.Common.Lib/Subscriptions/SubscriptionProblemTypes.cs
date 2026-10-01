namespace Onepunch.Common.Lib.Subscriptions;

/// <summary>
/// ProblemDetails.Type values every service returns (with 403) when a subscription blocks a
/// request. The frontend's error interceptor switches on these to show an upgrade prompt instead
/// of a generic error, so they're a contract -- don't rename.
/// </summary>
public static class SubscriptionProblemTypes
{
    /// <summary>The plan doesn't include the feature. Extension "feature" names the key.</summary>
    public const string PlanRequired = "plan-required";
    /// <summary>A plan limit is reached. Extensions "limit", "max" and "used".</summary>
    public const string PlanLimitReached = "plan-limit-reached";
    /// <summary>The subscription is read-only (trial ended, suspended, or canceled) -- what the
    /// owner and billing admins get when they try to change something.</summary>
    public const string ReadOnly = "subscription-read-only";
    /// <summary>The same lapsed subscription, as every other member sees it: the company is
    /// closed to them entirely until the owner pays.</summary>
    public const string Locked = "subscription-locked";
}
