using System.Linq.Expressions;
namespace DTR.Core;

public static class SpecificationExtensions
{
    public static Specification<T> And<T>(this Specification<T> left, Specification<T> right) where T : BaseEntity
        => new AndSpecification<T>(left, right);

    public static Specification<T> Or<T>(this Specification<T> left, Specification<T> right)  where T : BaseEntity
        => new OrSpecification<T>(left, right);

    public static Specification<T> Not<T>(this Specification<T> inner)  where T : BaseEntity
        => new NotSpecification<T>(inner);
}
public class AndSpecification<T> : Specification<T> where T : BaseEntity
{
    private readonly Specification<T> _left;
    private readonly Specification<T> _right;

    public AndSpecification(Specification<T> left, Specification<T> right)
    {
        _left = left;
        _right = right;
    } 
    public override Expression<Func<T, bool>> Criteria =>
        x => _left.Criteria.Compile().Invoke(x) && _right.Criteria.Compile().Invoke(x);
}
public class OrSpecification<T> : Specification<T> where T : BaseEntity
{
    private readonly Specification<T> _left;
    private readonly Specification<T> _right;

    public OrSpecification(Specification<T> left, Specification<T> right)
    {
        _left = left;
        _right = right;
    }

    public override Expression<Func<T, bool>> Criteria =>
        x => _left.Criteria.Compile().Invoke(x) || _right.Criteria.Compile().Invoke(x);
}
public class NotSpecification<T> : Specification<T> where T : BaseEntity
{
    private readonly Specification<T> _inner;

    public NotSpecification(Specification<T> inner)
    {
        _inner = inner;
    }

    public override Expression<Func<T, bool>> Criteria =>
        x => !_inner.Criteria.Compile().Invoke(x);
}
