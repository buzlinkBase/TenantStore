using System.Linq.Expressions;

namespace DTR.Core;

public class IsTrueSpec<T> : Specification<T> where T : class, IEntity, new()
{
    public IsTrueSpec()
    {
    }
    public override Expression<Func<T, bool>> Criteria => x=>true;
}
 