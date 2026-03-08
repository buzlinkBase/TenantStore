using MassTransit;
using Microsoft.EntityFrameworkCore;
using Serilog;
using TenantStoreApi.Core.Services;

namespace TenantStoreApi.Core;

public class UserConfirmedWorker : IConsumer<TenantUserPayload>
{
    private readonly TenantService _tenantService;
    private readonly BranchService _branchService;
    private readonly UserMembershipService _userMembershipService;

    public UserConfirmedWorker(TenantService tenantService,
        BranchService branchService,
        UserMembershipService userMembershipService

        )
    {
        _tenantService = tenantService;
        _branchService = branchService;
        _userMembershipService = userMembershipService;
    }
    public async Task Consume(ConsumeContext<TenantUserPayload> context)
    {
        var model = context.Message;
        var tenant = await _tenantService.FindTenantAsync(model.TenantId, context.CancellationToken);
        if (tenant == null)
        {
            Log.Warning("Tenant {TenantId} not found. Nothing to activate.", model.TenantId);
            return;
        }

        //branch
        var branch = await _branchService.GetQueryable(x => x.TenantId == model.TenantId).FirstOrDefaultAsync(context.CancellationToken);
        if (branch != null)
        {
            branch.Status = "Active";
            _branchService.Repository.Update(branch);
        }
        tenant.Status = "Active";
        //membership
        await _userMembershipService.AddAsync(new UserMembership
        {
            Role = "Admin",
            TenantId = tenant.Id,
            UserId = model.UserId
        },context.CancellationToken);

        await _tenantService.CommitChangesAsync(context.CancellationToken);
    }
}
