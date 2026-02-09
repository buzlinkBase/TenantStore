namespace DTR.Core;

public class PayrollGroupService : ServiceBase<PayrollGroup>
{
    public PayrollGroupService(IDTRUnitOfWork uow) : base(uow) { }
    public override void AddOrUpdate(PayrollGroup model)
    {
        if (string.IsNullOrEmpty(model.Code))
        {
            var recordCount  = FindAll().Count() + 1;
            model.Code = recordCount.FormatCode();
        }
        base.AddOrUpdate(model);
    }
    protected override ValidationMessage AddOrUpdateValidation(PayrollGroup model)
    {
        var shift = _uow.Repository.Find<PayrollGroup>(x => (x.Name.ToLower() == model.Name.ToLower()) && x.Id != model.Id);
        if (shift.Any())
        {
            return new ValidationMessage(false, "payroll group is already exists");
        }
        else if (string.IsNullOrWhiteSpace(model.Name))
        {
            return new ValidationMessage(false, "Invalid payroll group name");
        }
        return base.AddOrUpdateValidation(model);
    }  

}