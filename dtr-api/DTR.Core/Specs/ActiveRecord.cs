using System.Linq.Expressions;
namespace DTR.Core;


public class ActiveRecord<T> : Specification<T> where T : class, IEntity, new()
{
    public override Expression<Func<T, bool>> Criteria
    {
        get
        {
            return x => x.Status == "Active";
        }
    }
}

