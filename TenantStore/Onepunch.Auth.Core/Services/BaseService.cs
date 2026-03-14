using EFCore.BulkExtensions;
using Onepunch.Auth.Infrastructure;

namespace OnePunch.Auth.Core.Services;

public abstract class BaseService<T> where T : class, IEntity
{
    protected readonly IUnitOfWorkService _uow;
    protected BaseService(IUnitOfWorkService service)
    {
        _uow = service;
    }
    public IUnitOfWorkService Uow => _uow;
    public IRepository Repository => _uow.Repository;
    public AuthContext Context => _uow.Context;
    protected virtual async Task<EvaluationResult> CreateValidatorAsync(T model, CancellationToken token = default) => EvaluationResult.OK;
    public async Task<bool> CommitChangesAsync(CancellationToken token = default) => await _uow.CommitChangesAsync("", token);
    public int SaveChanges() => _uow.SaveChanges();
    public Task<int> SaveChangesAsync(CancellationToken token = default) => _uow.SaveChangesAsync(token);
    public bool CommitChanges() => _uow.CommitChanges("");
    public IQueryable<T> FindBySpec(Specification<T> specification)
    {
        return Repository.Find(specification);
    }
    public async Task BulkInsertAsync(IEnumerable<T> models, bool tracked = true, Action<BulkConfig>? config = null, CancellationToken token = default) =>
        await Repository.BulkInsertAsync(models, token, tracked, config);
    public async Task BulkUpdateAsync(IEnumerable<T> models, bool tracked = true, Action<BulkConfig>? config = null, CancellationToken token = default) =>
      await Repository.BulkUpdateAsync(models, token, tracked, config);
    public async Task BulkInsertOrUpdateAsync(IEnumerable<T> models, bool tracked = true, Action<BulkConfig>? config = null, CancellationToken token = default) =>
       await Repository.BulkInsertOrUpdateAsync(models, token, tracked, config);

    public async Task BulkInsertAsync(IEnumerable<T> models, CancellationToken token, Action<BulkConfig>? config = null) =>
       await Repository.BulkInsertAsync(models, token, true, config);

    protected IQueryable<Type> GetQueryable<Type>(Specification<Type> specification, bool noTracking = true) where Type : class, IEntity
    {
        return noTracking
               ? _uow.Repository.Find(specification).AsNoTracking()
               : _uow.Repository.Find(specification)
               ;
    }
    public IQueryable<T> GetQueryable(bool noTracking = true)
    {
        return noTracking
               ? _uow.Repository.FindAll<T>().AsNoTracking()
               : _uow.Repository.FindAll<T>()
               ;
    }

    public IQueryable<T> GetQueryable(Expression<Func<T, bool>> expression, bool noTracking = true)
    {
        return GetQueryable(noTracking).Where(expression);
    }
    protected async Task<T?> GetOneAsync(Guid Id, CancellationToken token = default)
    {
        return await _uow.Repository.FindOneAsync<T>(Id, token);
    }

    protected async Task CreateRangeAsync(IEnumerable<T> models, CancellationToken token = default)
    {
        if (!models.Any()) return;
        await Guard.ModelGuardAsync<T>(CreateValidatorAsync, models, token);
        await _uow.Repository.AddRangeAsync(models, token);
    }

    protected async Task ModifyRangeAsync(IEnumerable<T> models, CancellationToken token = default)
    {
        if (!models.Any()) return;
        await Guard.ModelGuardAsync<T>(CreateValidatorAsync, models, token);
        _uow.Repository.UpdateRange(models);
    }
    protected async Task CreateAsync(T model, CancellationToken token = default)
    {
        if (model is null) return;
        await Guard.ModelGuardAsync<T>(CreateValidatorAsync, model, token);
        await _uow.Repository.AddAsync(model, token);
    }
    protected async Task ModifyAsync(T model, CancellationToken token = default)
    {
        if (model is null) return;
        await Guard.ModelGuardAsync<T>(CreateValidatorAsync, model, token);
        _uow.Repository.AddOrUpdate(model);
    }
    protected async Task CreateOrUpdateAsync(T model, CancellationToken token = default)
    {
        if (model is null) return;
        await Guard.ModelGuardAsync<T>(CreateValidatorAsync, model, token);
        await _uow.Repository.AddOrUpdateAsync(model, token);
    }
    protected async Task RemoveAllAsync()
    {
        _uow.Repository.RemoveAll<T>();
        await Task.CompletedTask;
    }
    protected async Task RemoveAsync(Guid Id, CancellationToken token = default)
    {
        await RemoveAsync(x => x.Id == Id, token);
    }
    protected async Task RemoveAsync(T model)
    {
        _uow.Repository.Remove(model);
        await Task.CompletedTask;
    }
    protected async Task RemoveAsync(Expression<Func<T, bool>> expression, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        _uow.Repository.Remove(expression);
        await Task.CompletedTask;
    }
    protected async Task RemoveRangeAsync(IEnumerable<T> models, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        _uow.Repository.RemoveRange(models);
        await Task.CompletedTask;
    }
    protected async Task ExecuteDeleteAsync(Expression<Func<T, bool>> expression, CancellationToken token = default)
    {
        var query = _uow.Repository.FindAll<T>().Where(expression);
        await query.ExecuteDeleteAsync(token);
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
