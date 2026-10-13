using System.Security.Claims;
using BuzlinkRepository;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Onepunch.Common.Lib.Security;
using Onepunch.Common.Lib.Subscriptions;
using Onepunch.Common.Web.Subscriptions;
using Xunit;

namespace TenantStoreApi.Tests.Security;

/// <summary>
/// What gates a system's API on the account having bought it: the product -> plan feature mapping and
/// [RequireProduct] / [RequireFeature], which refuse even the Owner when the plan lacks the system.
/// </summary>
public class RequireProductTests
{
    private static readonly Guid Company = Guid.NewGuid();

    private static ActionExecutingContext Context(string method, ClaimsPrincipal user, params string[] featuresOn)
    {
        var snapshot = EntitlementSnapshot.Unrestricted(Company) with
        {
            Features = EntitlementKeys.Features.ToDictionary(k => k, k => featuresOn.Contains(k)),
        };
        var entitlements = new Mock<IEntitlementProvider>();
        entitlements.Setup(x => x.GetAsync(Company, It.IsAny<CancellationToken>())).ReturnsAsync(snapshot);
        var tenant = new Mock<ITenantProvider>();
        tenant.SetupGet(x => x.TenantId).Returns(Company);

        var http = new DefaultHttpContext
        {
            User = user,
            RequestServices = new ServiceCollection()
                .AddSingleton(entitlements.Object)
                .AddSingleton(tenant.Object)
                .BuildServiceProvider(),
        };
        http.Request.Method = method;
        return new ActionExecutingContext(
            new ActionContext(http, new RouteData(), new ActionDescriptor()),
            [],
            new Dictionary<string, object?>(),
            controller: new object());
    }

    private static ClaimsPrincipal Owner() =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Role, "Owner")], "test"));

    private static async Task<bool> RunAsync(RequireFeatureAttribute filter, ActionExecutingContext context)
    {
        var reached = false;
        await filter.OnActionExecutionAsync(context, () =>
        {
            reached = true;
            return Task.FromResult<ActionExecutedContext>(null!);
        });
        return reached;
    }

    [Theory]
    [InlineData("hrms", "hrms.core")]
    [InlineData("procurement", "procurement.core")]
    [InlineData("accounting", "accounting.core")]
    [InlineData("billing", "billing.core")]
    [InlineData("clientportal", "clientportal.core")]
    public void ForProduct_MapsEachProductToThePlanFeatureThatSwitchesItOn(string product, string feature)
    {
        EntitlementKeys.ForProduct(product).Should().Be(feature);
        EntitlementKeys.IsFeature(feature).Should().BeTrue();
    }

    [Fact]
    public void ForProduct_IsNullForAnUnknownProduct() =>
        EntitlementKeys.ForProduct("warehouse").Should().BeNull();

    [Fact]
    public void EverySeparateSystem_IsReachableFromAProduct()
    {
        var products = new[]
        {
            PermissionProducts.Procurement, PermissionProducts.Accounting,
            PermissionProducts.Billing, PermissionProducts.ClientPortal,
        };

        products.Select(EntitlementKeys.ForProduct).Should().BeEquivalentTo(EntitlementKeys.SeparateSystems);
    }

    [Fact]
    public void RequireProduct_RejectsAnUnknownProduct_SoAnApiCantBeLeftOpenByATypo()
    {
        var act = () => new RequireProductAttribute("procurment");

        act.Should().Throw<ArgumentException>().WithMessage("*not a known product*");
    }

    [Fact]
    public async Task RequireProduct_LetsThroughAnAccountThatHasTheSystem()
    {
        var filter = new RequireProductAttribute(PermissionProducts.Procurement);

        (await RunAsync(filter, Context("POST", Owner(), EntitlementKeys.ProcurementCore))).Should().BeTrue();
    }

    [Fact]
    public async Task RequireProduct_RefusesEvenTheOwner_WhenThePlanLacksTheSystem()
    {
        var filter = new RequireProductAttribute(PermissionProducts.Procurement);
        var context = Context("GET", Owner(), EntitlementKeys.PayrollCore);

        var act = () => RunAsync(filter, context);

        var error = (await act.Should().ThrowAsync<PlanRestrictionException>()).Which;
        error.ProblemType.Should().Be(SubscriptionProblemTypes.PlanRequired);
        error.Extensions["feature"].Should().Be(EntitlementKeys.ProcurementCore);
    }

    [Fact]
    public async Task RequireProduct_OwningAnotherSystem_DoesNotOpenThisOne()
    {
        var filter = new RequireProductAttribute(PermissionProducts.Accounting);
        var context = Context("GET", Owner(), EntitlementKeys.ProcurementCore, EntitlementKeys.HrmsCore);

        var act = () => RunAsync(filter, context);

        await act.Should().ThrowAsync<PlanRestrictionException>();
    }

    [Fact]
    public async Task RequireFeature_WritesOnly_LetsReadsPassWithoutThePlan()
    {
        var filter = new RequireFeatureAttribute(EntitlementKeys.PayrollCore) { WritesOnly = true };

        (await RunAsync(filter, Context("GET", Owner()))).Should().BeTrue();
        var write = () => RunAsync(filter, Context("POST", Owner()));
        await write.Should().ThrowAsync<PlanRestrictionException>();
    }
}
