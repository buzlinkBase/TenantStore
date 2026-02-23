using System.Data.Common;
using System.Diagnostics;

namespace DTR.Core;

public abstract class ServiceBase<T> where T : class, IEntity, new()
{
    protected readonly IDTRUnitOfWork _uow;
    protected ServiceBase(IDTRUnitOfWork uow)
    {
        _uow = uow;
    }
    public IDTRUnitOfWork UnitOfWork => _uow;
    protected virtual ValidationMessage AddOrUpdateValidation(T model) => new ValidationMessage(true, "");
    protected virtual ValidationMessage DeleteValidation(T model) => new ValidationMessage(true, "");
    public virtual void AddOrUpdate(T model)
    {
        var validationResult = AddOrUpdateValidation(model);
        if (!validationResult.Success)
        {
            throw new Exception(validationResult.Message);
        }
        _uow.Repository.AddOrUpdate(model);
    }
    public virtual async Task AddRangeAsync(List<T> models, CancellationToken token)
    {
        await _uow.Repository.AddRangeAsync(models, token);
    }
    public virtual void AddRange(List<T> models)
    {
        try
        {
            _uow.Repository.AddRange(models);
        }
        catch (DbException ex)
        {
            Debug.WriteLine($"[DB ERROR] {ex}");
        }
    }
    public virtual void AddRangeWithValidation(List<T> models)
    {
        foreach (var item in models)
        {
            var validationResult = AddOrUpdateValidation(item);
            if (!validationResult.Success)
            {
                throw new Exception(validationResult.Message);
            }
        }
        _uow.Repository.AddRange(models);
    }
    public virtual IQueryable<T> FindAll()
    {
        return _uow.Repository.FindAll<T>();
    }
    public virtual List<T> FindActive()
    {
        var spec = new ActiveRecord<T>();
        var data = _uow.Repository.Find<T>(spec).ToList();
        return data;
    }
    public virtual T? FindOne(Guid Id)
    {
        return _uow.Repository.FindOne<T>(Id);
    }

    public virtual void UpdateStatatus(T model, string status)
    {
        model.Status = status;
        AddOrUpdate(model);
    }
    public virtual void Activate(T model)
    {
        UpdateStatatus(model, "Active");
    }
    public virtual void Cancel(T model)
    {
        UpdateStatatus(model, "Cancelled");
    }
    public virtual void Remove(Guid Id)
    {
        _uow.Repository.Remove<T>(Id);
    }
    public virtual void Remove(T model)
    {
        var validationResult = DeleteValidation(model);
        if (!validationResult.Success && !string.IsNullOrWhiteSpace(validationResult.Message))
        {
            throw new Exception(validationResult.Message);
        }
        _uow.Repository.Remove(model);
    }
    public virtual void RemoveRange(IEnumerable<T> models)
    {
        _uow.Repository.RemoveRange(models);
    }
    public virtual void RemoveRangeWithValidation(IEnumerable<T> models)
    {
        foreach (var item in models)
        {
            var validationResult = DeleteValidation(item);
            if (!validationResult.Success)
            {
                throw new Exception(validationResult.Message);
            }
        }
        _uow.Repository.RemoveRange(models);
    }
    public bool CommitChanges() => _uow.CommitChanges();
    public async Task<bool> CommitChangesAsync(CancellationToken token) => await _uow.CommitChangesAsync(token);
    public void SaveChanges() => _uow.SaveChanges();
    public async Task SaveChangesAsync(CancellationToken token) => await _uow.SaveChangesAsync(token);
}
public record ValidationMessage(bool Success, string Message) { };
public class ValidationException : Exception
{
    public string Message { get; set; }
    public Exception Exception { get; set; }
}

