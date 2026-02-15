
using Onepunch.Common.Lib.Services;
using OnePunch.Auth.Core;

namespace Onepunch.Auth.Core.Services;

public class OutBoxService : OutBoxServiceBase
{
    private readonly IUnitOfWorkService _uow;
    public OutBoxService(IUnitOfWorkService uow) : base(uow.Repository)
    {
        _uow = uow;
    }
    public AuthContext Context => _uow.Context;

}
