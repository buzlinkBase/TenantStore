using BuzlinkRepository;
using TenantStoreApi.Infrastructure;
namespace TenantStoreApi.Core;
public interface IUnitOfWorkService : IUnitOfWork<TenantContext> { }
public class UnitOfWorkService(TenantContext context) : UnitOfWork<TenantContext>(context), IUnitOfWorkService;
 