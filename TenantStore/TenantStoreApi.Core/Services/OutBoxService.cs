
using Onepunch.Common.Lib.Services;

namespace TenantStoreApi.Core.Services;
public class OutBoxService(IUnitOfWorkService uow)
    : OutBoxServiceBase(uow.Repository)
{
}
