using Microsoft.EntityFrameworkCore.Query;
using System.Linq.Expressions;

namespace Onepunch.Auth.Api.Tests.TestSupport;

/// <summary>
/// Lets an in-memory List{T}.AsQueryable() (e.g. from a mocked UserManager/RoleManager.Users
/// property) survive EF Core's ToListAsync/FirstOrDefaultAsync/etc., which require an
/// IAsyncQueryProvider that plain LINQ-to-Objects queryables don't implement.
/// </summary>
public static class TestAsyncQueryableExtensions
{
    public static IQueryable<T> AsAsyncQueryable<T>(this IEnumerable<T> source) =>
        new TestAsyncEnumerable<T>(source);
}

internal class TestAsyncEnumerable<T> : EnumerableQuery<T>, IAsyncEnumerable<T>, IQueryable<T>
{
    public TestAsyncEnumerable(IEnumerable<T> enumerable) : base(enumerable) { }
    public TestAsyncEnumerable(Expression expression) : base(expression) { }

    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
        new TestAsyncEnumerator<T>(this.AsEnumerable().GetEnumerator());

    IQueryProvider IQueryable.Provider => new TestAsyncQueryProvider<T>(this);
}

internal class TestAsyncEnumerator<T>(IEnumerator<T> inner) : IAsyncEnumerator<T>
{
    public T Current => inner.Current;
    public ValueTask<bool> MoveNextAsync() => ValueTask.FromResult(inner.MoveNext());
    public ValueTask DisposeAsync()
    {
        inner.Dispose();
        return ValueTask.CompletedTask;
    }
}

internal class TestAsyncQueryProvider<TEntity>(IQueryProvider inner) : IQueryProvider, IAsyncQueryProvider
{
    public IQueryable CreateQuery(Expression expression) => new TestAsyncEnumerable<TEntity>(expression);
    public IQueryable<TElement> CreateQuery<TElement>(Expression expression) => new TestAsyncEnumerable<TElement>(expression);
    public object? Execute(Expression expression) => inner.Execute(expression);
    public TResult Execute<TResult>(Expression expression) => inner.Execute<TResult>(expression);

    public TResult ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken = default)
    {
        var resultType = typeof(TResult).GetGenericArguments().FirstOrDefault();
        if (resultType == null)
            return Execute<TResult>(expression);

        var executionResult = typeof(IQueryProvider)
            .GetMethod(name: nameof(IQueryProvider.Execute), genericParameterCount: 1, types: [typeof(Expression)])!
            .MakeGenericMethod(resultType)
            .Invoke(this, [expression]);

        return (TResult)typeof(Task).GetMethod(nameof(Task.FromResult))!
            .MakeGenericMethod(resultType)
            .Invoke(null, [executionResult])!;
    }
}
