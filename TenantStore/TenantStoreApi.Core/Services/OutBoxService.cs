
using Onepunch.Common.Lib.Services;

namespace TenantStoreApi.Core.Services;
public class OutBoxService : OutBoxServiceBase
{
    public OutBoxService(IUnitOfWorkService uow) : base(uow.Repository)
    {
    }
}
