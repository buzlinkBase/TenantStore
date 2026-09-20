using Microsoft.EntityFrameworkCore;

namespace TenantStoreApi.Core.Services;

public class MembershipRoleService : BaseService<MembershipRole>
{
    public MembershipRoleService(IUnitOfWorkService service) : base(service)
    {
    }
    public async Task RemoveRoles(Guid userId)
    {
        var member = await Context.Memberships
            .Include(x => x.Roles)
            .Where(x => x.UserId == userId)
            .FirstOrDefaultAsync()
            ;
        if (member != null)
        {
            Repository.RemoveRange(member.Roles);
        }
    }

    public async Task AddRoles(List<MembershipRole> roles, CancellationToken token)
    {
        await Repository.AddRangeAsync(roles, token);
    }
}
