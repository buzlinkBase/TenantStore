
namespace DTR.Core;


public class ClientService : ServiceBase<Client>
{
    public ClientService(IDTRUnitOfWork uow) : base(uow) { }

    public override void AddOrUpdate(Client model)
    {
        if (model.Id == Guid.Empty || string.IsNullOrEmpty(model.Code))
        {
            var recordCount = FindAll().Count() + 1;
            model.Code = recordCount.FormatCode();
        }
        base.AddOrUpdate(model);
    }

    protected override ValidationMessage AddOrUpdateValidation(Client model)
    {
        if (IsExist(model))
        {
            return new ValidationMessage(false, "Client is already exists");
        }
        else if (string.IsNullOrWhiteSpace(model.Name))
        {
            return new ValidationMessage(false, "Invalid client name");
        }
        return base.AddOrUpdateValidation(model);
    }
    private bool IsExist(Client model)
    {
        var shift = _uow.Repository.Find<Client>(x => (x.Name.ToLower() == model.Name.ToLower()) && x.Id != model.Id);
        return shift.Any();
    }
}