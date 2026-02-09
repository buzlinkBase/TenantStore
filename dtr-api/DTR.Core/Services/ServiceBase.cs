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
        try
        {
            var validationResult = AddOrUpdateValidation(model);
            if (!validationResult.Success)
            {
                throw new Exception(validationResult.Message);
            }
            _uow.Repository.AddOrUpdate(model);
        }
        catch (DbException ex)
        {
            Debug.WriteLine($"[DB ERROR] {ex}");
        }
    }
    public virtual async Task AddRangeAsync(List<T> models)
    {
        try
        {
            await _uow.Repository.AddRangeAsync(models);
        }
        catch (DbException ex)
        {
            Debug.WriteLine($"[DB ERROR] {ex}");
        }
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
        try
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
        catch (DbException ex)
        {
            Debug.WriteLine($"[DB ERROR] {ex}");
        }
    }
    public virtual IQueryable<T> FindAll()
    {
        try
        {
            return _uow.Repository.FindAll<T>();
        }
        catch (DbException ex)
        {
            Debug.WriteLine($"[DB ERROR] {ex}");
            return Enumerable.Empty<T>().AsQueryable();
        }
    }
    public virtual List<T> FindActive()
    {
        try
        {
            var spec = new ActiveRecord<T>();
            var data = _uow.Repository.Find<T>(spec).ToList();
            return data;
        }
        catch (DbException ex)
        {
            Debug.WriteLine($"[DB ERROR] {ex}");
            return new List<T>();
        }
    }
    public virtual T? FindOne(Guid Id)
    {
        try
        {
            return _uow.Repository.FindOne<T>(Id);
        }
        catch (DbException ex)
        {
            Debug.WriteLine($"[DB ERROR] {ex}");
            return null;
        }
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
        try
        {
            _uow.Repository.Remove<T>(Id);
        }
        catch (DbException ex)
        {
            Debug.WriteLine($"[DB ERROR] {ex}");
        }
    }
    public virtual void Remove(T model)
    {
        try
        {

            var validationResult = DeleteValidation(model);
            if (!validationResult.Success && !string.IsNullOrWhiteSpace(validationResult.Message))
            {
                throw new Exception(validationResult.Message);
            }
            _uow.Repository.Remove(model);
        }
        catch (DbException ex)
        {
            Debug.WriteLine($"[DB ERROR] {ex}");
        }
    }
    public virtual void RemoveRange(IEnumerable<T> models)
    {
        try
        {
            _uow.Repository.RemoveRange(models);
        }
        catch (DbException ex)
        {
            Debug.WriteLine($"[DB ERROR] {ex}");
        }
    }
    public virtual void RemoveRangeWithValidation(IEnumerable<T> models)
    {
        try
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
        catch (DbException ex)
        {
            Debug.WriteLine($"[DB ERROR] {ex}");
        }
    }
    public bool CommitChanges() => _uow.CommitChanges();
    public async Task<bool> CommitChangesAsync() => await _uow.CommitChangesAsync();
    public void SaveChanges() => _uow.SaveChanges();
    public async Task SaveChangesAsync() => await _uow.SaveChangesAsync();
}
public record ValidationMessage(bool Success, string Message) { };
public class ValidationException : Exception
{
    public string Message { get; set; }
    public Exception Exception { get; set; }
}

