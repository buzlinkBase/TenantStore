using System.Linq.Expressions;
namespace DTR.Core;

public class UserHasViewSpec<T> : Specification<T> where T : class, IEntity, new()
{
    private readonly bool _hasView = false;
    public UserHasViewSpec(bool hasView)
    {
        _hasView = hasView;
    }
    public override Expression<Func<T, bool>> Criteria =>
        x =>  _hasView;
}
