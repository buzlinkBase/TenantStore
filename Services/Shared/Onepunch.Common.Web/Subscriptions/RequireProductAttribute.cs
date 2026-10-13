using Onepunch.Common.Lib.Security;
using Onepunch.Common.Lib.Subscriptions;

namespace Onepunch.Common.Web.Subscriptions;

/// <summary>
/// Gates a whole system's API on the account having bought that system: [RequireProduct("procurement")]
/// on a controller, or <c>options.Filters.Add(new RequireProductAttribute(PermissionProducts.Procurement))</c>
/// to gate every action of a product API at once. It is <see cref="RequireFeatureAttribute"/> on the
/// feature that switches the product on (<see cref="EntitlementKeys.ForProduct"/>), so it refuses with
/// the same 403 "plan-required" -- even for the Owner, who passes every permission check but cannot use
/// a system the plan doesn't include. An unknown product fails at startup rather than leaving the API open.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireProductAttribute(string product)
    : RequireFeatureAttribute(EntitlementKeys.ForProduct(product)
        ?? throw new ArgumentException($"'{product}' is not a known product, so it has no plan feature to gate on.", nameof(product)))
{
    public string Product => product;
}
