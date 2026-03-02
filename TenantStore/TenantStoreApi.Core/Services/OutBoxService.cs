//using Onepunch.Common.Lib.Services;
//using TenantStoreApi.Infrastructure;

//namespace TenantStoreApi.Core.Services;
//public class OutBoxService : OutBoxServiceBase
//{
//    private readonly IUnitOfWorkService _uow;
//    public OutBoxService(IUnitOfWorkService uow) : base(uow.Repository)
//    {
//        _uow = uow;
//    }
//    public TenantContext Context => _uow.Context;
//    public async Task<bool> CommitChangesAsync(CancellationToken token = default) => await _uow.CommitChangesAsync("", token);
//    public bool CommitChanges() => _uow.CommitChanges("");
//    public async Task SaveChangesAsync(CancellationToken token = default) => await _uow.SaveChangesAsync(token);
//    public void SaveChanges() => _uow.SaveChanges();
//}
