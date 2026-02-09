
using Onepunch.Common.Lib.Services;
using OnePunch.Auth.Core;

namespace Onepunch.Auth.Core.Services;

public class OutBoxService : OutBoxServiceBase
{
    public OutBoxService(IUnitOfWorkService uow) : base(uow.Repository)
    {
    }
}
