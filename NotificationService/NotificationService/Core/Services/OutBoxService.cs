using Onepunch.Common.Lib.Services;

namespace OnePunch.Notification.Core.Services;

public class OutBoxService : OutBoxServiceBase
{
    private readonly IUnitOfWorkService _uow;
    public OutBoxService(IUnitOfWorkService uow) : base(uow.Repository)
    {
        _uow = uow;
    } 
}
