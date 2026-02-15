using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Onepunch.Auth.Infrastructure.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Onepunch.Auth.Infrastructure;

public class TenantModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime)
    {
        if (context is AuthContext tenantContext)
        {
            return (context.GetType(), tenantContext.TenantId, designTime);
        }
        return (context.GetType(), designTime);
    }
}