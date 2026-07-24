using Onepunch.Auth.Infrastructure;

namespace OnePunch.Auth.Core;

public interface IUnitOfWorkService : IUnitOfWork<AuthContext> { }
public class UnitOfWorkService(AuthContext context) : UnitOfWork<AuthContext>(context), IUnitOfWorkService;