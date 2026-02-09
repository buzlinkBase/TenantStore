namespace DTR.Core;

public class AreaService : ServiceBase<OperationArea>
{
    public AreaService(IDTRUnitOfWork uow) : base(uow) { }
    protected override ValidationMessage AddOrUpdateValidation(OperationArea model)
    {
        if (IsExist(model))
        {
            return new ValidationMessage(false, "Area is already exists");
        }
        else if (string.IsNullOrWhiteSpace(model.Name))
        {
            return new ValidationMessage(false, "Invalid Area name");
        }
        return base.AddOrUpdateValidation(model);
    }
    private bool IsExist(OperationArea model)
    {
        var entities = _uow.Repository.Find<OperationArea>(x => (x.Name.ToLower() == model.Name.ToLower()) && x.Id != model.Id);
        return entities.Any();
    }
}
