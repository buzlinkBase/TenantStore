
using ValidationResponse = OnePunch.Auth.Core.Exceptions.ValidationResponse;

namespace OnePunch.Auth.Core.Services;
public abstract class BaseService<T>
    where T : class, IEntity
{
    protected readonly IUnitOfWorkService UoW;
    protected BaseService(IUnitOfWorkService service)
    {
        UoW = service;
    }
    protected IRepository Repository => UoW.Repository;
    protected AuthContext Context => UoW.Context;
    protected virtual async Task<ValidationResponse> CreateValidator(T model) => ValidationResponse.OK;
    public async Task<bool> CommitChangesAsync()
    {
        return await UoW.CommitChangesAsync();
    }
    public bool CommitChanges()
    {
        return UoW.CommitChanges();
    }
    public IQueryable<T> FindBySpec(Specification<T> specification)
    {
        return Repository.Find(specification);
    }

    protected IQueryable<Type> GetQueryable<Type>(Specification<Type> specification, bool noTracking = true) where Type : class, IEntity
    {
        return noTracking
               ? Repository.Find(specification).AsNoTracking()
               : Repository.Find(specification)
               ;
    }
    protected IQueryable<T> GetQueryable(bool noTracking = true)
    {
        return noTracking
               ? Repository.FindAll<T>().AsNoTracking()
               : Repository.FindAll<T>()
               ;
    }
    protected IQueryable<T> GetQueryable(Expression<Func<T, bool>> expression, bool noTracking = true)
    {
        return GetQueryable(noTracking).Where(expression);
    }
    protected async Task<T?> GetOneAsync(Guid Id)
    {
        return await Repository.FindOneAsync<T>(Id);
    }
    protected async Task CreateRangeAsync(IEnumerable<T> models)
    {
        await Repository.AddRangeAsync(models);
    }
    protected async Task CreateAsync(T model)
    {
        if (model is null) return;
        await Guard.ModelGuardAsync<T>(CreateValidator, model);
        await Repository.AddAsync(model);
    }
    protected async Task ModifyAsync(T model)
    {
        if (model is null) return;
        await Guard.ModelGuardAsync<T>(CreateValidator, model);
        Repository.Update(model);
        await Task.CompletedTask;
    }
    protected async Task CreateOrUpdateAsync(T model)
    {
        if (model is null) return;
        await Guard.ModelGuardAsync<T>(CreateValidator, model);
        Repository.AddOrUpdate(model);
        await Task.CompletedTask;
    }
    protected async Task RemoveAllAsync()
    {
        Repository.RemoveAll<T>();
        await Task.CompletedTask;
    }
    protected async Task RemoveAsync(Guid Id)
    {
        await RemoveAsync(x => x.Id == Id);
    }
    protected async Task RemoveAsync(T model)
    {
        Repository.Remove(model);
        await Task.CompletedTask;
    }
    protected async Task RemoveAsync(Expression<Func<T, bool>> expression)
    {
        Repository.Remove(expression);
        await Task.CompletedTask;
    }
    protected async Task RemoveRangeAsync(List<T> models)
    {
        Repository.RemoveRange(models);
        await Task.CompletedTask;
    }
    protected async Task ExecuteDeleteAsync(Expression<Func<T, bool>> expression)
    {
        var query = Repository.FindAll<T>().Where(expression);
        await query.ExecuteDeleteAsync();
    }
    protected IQueryable<T> PaginatedQuerable(IQueryable<T> query, int Page, int? Limit)
    {
        var limit = Limit.GetValueOrDefault();
        var page = Page > 0 ? Page : 1;
        var skip = limit > 0 ? (page - 1) * limit : 0;
        var dataQuery = query.Skip(skip);
        if (limit > 0)
        {
            dataQuery = dataQuery.Take(limit);
        }
        return dataQuery;
    }
}