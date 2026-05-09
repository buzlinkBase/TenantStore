using Microsoft.AspNetCore.Identity;
using OnePunch.Auth.Core;
using OnePunch.Auth.Core.Services;
using OnePunch.Auth.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Onepunch.Auth.Core.Services;

public class RoleService : BaseService<Role>
{
    private readonly RoleManager<Role> _roleManager;

    public RoleService(IUnitOfWorkService uow,
        RoleManager<Role> roleManager) : base(uow)
    {
        _roleManager = roleManager;
    }

    public async Task Create(string roleName)
    {
        var role = new Role()
        {
            Name=roleName,
        };
        await _roleManager.CreateAsync(role);
        await CommitChangesAsync();
    }
}
