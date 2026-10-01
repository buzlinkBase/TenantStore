using Onepunch.Common.Lib.Exceptions;

namespace Onepunch.Common.Lib.Subscriptions;

/// <summary>
/// A subscription refused the request -- a feature the plan lacks, a limit reached, or a
/// read-only subscription. Global exception handlers turn it into a 403 ProblemDetails whose Type
/// is <see cref="ProblemType"/> (one of <see cref="SubscriptionProblemTypes"/>) with
/// <see cref="Extensions"/> alongside, which the frontend uses to show an upgrade prompt.
/// </summary>
public class PlanRestrictionException(
    string problemType,
    string message,
    IReadOnlyDictionary<string, object?>? extensions = null) : ForbiddenException(message)
{
    public string ProblemType { get; } = problemType;
    public IReadOnlyDictionary<string, object?> Extensions { get; } = extensions ?? new Dictionary<string, object?>();

    public static PlanRestrictionException FeatureRequired(string featureKey, string? message = null) =>
        new(SubscriptionProblemTypes.PlanRequired,
            message ?? "Your plan doesn't include this feature.",
            new Dictionary<string, object?> { ["feature"] = featureKey });

    public static PlanRestrictionException LimitReached(string limitKey, int? max, int used, string? message = null) =>
        new(SubscriptionProblemTypes.PlanLimitReached,
            message ?? $"Your plan's limit is reached ({used} of {max}).",
            new Dictionary<string, object?> { ["limit"] = limitKey, ["max"] = max, ["used"] = used });

    public static PlanRestrictionException ReadOnly(string? message = null) =>
        new(SubscriptionProblemTypes.ReadOnly,
            message ?? "Your subscription is read-only. Choose a plan to make changes.");

    public static PlanRestrictionException Locked(string? message = null) =>
        new(SubscriptionProblemTypes.Locked,
            message ?? "This company's subscription has ended. Contact the company owner to restore access.");
}
