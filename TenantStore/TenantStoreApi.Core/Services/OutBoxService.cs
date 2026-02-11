
using Onepunch.Common.Lib.Services;

namespace TenantStoreApi.Core.Services;

public class OutBoxService(IUnitOfWorkService uow)
    : OutBoxServiceBase(uow.Repository)
{
    // You can still add members here if needed
}
