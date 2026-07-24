using Microsoft.AspNetCore.Authorization;

namespace TenantStoreApi.Middlewares;

public class SubscriptionRequirementHandler : AuthorizationHandler<SubscriptionRequirement>
{
    private readonly ITenantProvider _tenantContext;
    private readonly ISubscriptionService _subscriptionService;
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, SubscriptionRequirement requirement)
    {
        // Verify if the active tenant has the required module (e.g., "HRIS")
        var hasAccess = await _subscriptionService.HasModuleAccessAsync(_tenantContext.TenantId, requirement.ModuleCode);
        if (hasAccess)
        {
            context.Succeed(requirement);
        }
    }
}

public class SubscriptionRequirement : IAuthorizationRequirement
{
    public string ModuleCode { get; set; }
}

public interface ISubscriptionService
{
    Task<bool> HasModuleAccessAsync(Guid tenantId, string module);
}