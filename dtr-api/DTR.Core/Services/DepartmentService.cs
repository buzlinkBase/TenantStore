namespace DTR.Core;

public class DepartmentService : ServiceBase<Department>
{
    public DepartmentService(IDTRUnitOfWork uow) : base(uow) { }
    protected override ValidationMessage AddOrUpdateValidation(Department model)
    {
        if (IsExist(model))
        {
            return new ValidationMessage(false, "Department is already exists");
        }
        else if (string.IsNullOrWhiteSpace(model.Name))
        {
            return new ValidationMessage(false, "Invalid department name");
        }
        return base.AddOrUpdateValidation(model);
    }
    private bool IsExist(Department model)
    {
        var entities  = _uow.Repository.Find<Department>(x => (x.Name.ToLower() == model.Name.ToLower()) && x.Id != model.Id);
        return entities.Any();
    }
}