using Microsoft.AspNetCore.Identity;
using OnePunch.Auth.Core;
using OnePunch.Auth.Core.Services;
using OnePunch.Auth.Domain.Entities;

namespace Onepunch.Auth.Core.Services;

public class RoleService : BaseService<Role>
{
    private readonly RoleManager<Role> _roleManager;

    public RoleService(IUnitOfWorkService uow,
        RoleManager<Role> roleManager) : base(uow)
    {
        _roleManager = roleManager;
    }

    public async Task<Role?> Create(string roleName)
    {
        var role = await _roleManager.RoleExistsAsync(roleName);
        if (!role)
        {
            var newrole = new Role() { Name = roleName };
            await _roleManager.CreateAsync(newrole);
            await CommitChangesAsync();
            return newrole;
        }
        return await _roleManager.FindByNameAsync(roleName);
    }

    public async Task<List<Role>> GetAll()
    {
        return await _roleManager.Roles.ToListAsync();
    }
}
