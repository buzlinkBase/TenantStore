using Adms.api.Exceptions;
using Adms.api.Validations;
using BuzlinkRepository;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace Adms.api.Services;

public abstract class BaseService<T> where T : class, IEntity
{
    protected readonly IUnitOfWorkService _uow;
    protected BaseService(IUnitOfWorkService service)
    {
        _uow = service;
    }
    protected IRepository Repository => _uow.Repository;
    protected AdmsContext Context => _uow.Context;
    protected virtual async Task<ValidationResult> CreateValidator(T model) => ValidationResult.OK;
    public async Task<bool> CommitChangesAsync()
    {
        return await _uow.CommitChangesAsync();
    }
    public bool CommitChanges()
    {
        return _uow.CommitChanges();
    }
    public IQueryable<T> FindBySpec(Specification<T> specification)
    {
        return Repository.Find(specification);
    }

    protected IQueryable<M> GetQueryable<M>(Specification<M> specification, bool noTracking = true) where M : class, IEntity
    {
        return noTracking
               ? _uow.Repository.Find(specification).AsNoTracking()
               : _uow.Repository.Find(specification)
               ;
    }
    protected IQueryable<T> GetQueryable(bool noTracking = true)
    {
        return noTracking
               ? _uow.Repository.FindAll<T>().AsNoTracking()
               : _uow.Repository.FindAll<T>()
               ;
    }
    protected IQueryable<T> GetQueryable(Expression<Func<T, bool>> expression, bool noTracking = true)
    {
        return GetQueryable(noTracking).Where(expression);
    }
    protected async Task<T?> GetOneAsync(Guid Id)
    {
        return await _uow.Repository.FindOneAsync<T>(Id);
    }
    protected async Task CreateRangeAsync(IEnumerable<T> models)
    {
        await _uow.Repository.AddRangeAsync(models);
    }
    protected async Task CreateAsync(T model)
    {
        if (model is null) return;
        await Guard.ModelGuardAsync<T>(CreateValidator, model);
        await _uow.Repository.AddAsync(model);
    }
    protected async Task ModifyAsync(T model)
    {
        if (model is null) return;
        await Guard.ModelGuardAsync<T>(CreateValidator, model);
        _uow.Repository.Update(model);
        await Task.CompletedTask;
    }
    protected async Task CreateOrUpdateAsync(T model)
    {
        if (model is null) return;
        await Guard.ModelGuardAsync<T>(CreateValidator, model);
        _uow.Repository.AddOrUpdate(model);
        await Task.CompletedTask;
    }
    protected async Task RemoveAllAsync()
    {
        _uow.Repository.RemoveAll<T>();
        await Task.CompletedTask;
    }
    protected async Task RemoveAsync(Guid Id)
    {
        await RemoveAsync(x => x.Id == Id);
    }
    protected async Task RemoveAsync(T model)
    {
        _uow.Repository.Remove(model);
        await Task.CompletedTask;
    }
    protected async Task RemoveAsync(Expression<Func<T, bool>> expression)
    {
        _uow.Repository.Remove(expression);
        await Task.CompletedTask;
    }
    protected async Task RemoveRangeAsync(List<T> models)
    {
        _uow.Repository.RemoveRange(models);
        await Task.CompletedTask;
    }
    protected async Task ExecuteDeleteAsync(Expression<Func<T, bool>> expression)
    {
        var query = _uow.Repository.FindAll<T>().Where(expression);
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
