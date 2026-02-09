//namespace DTR.Core;

//public class LeaveService : ServiceBase<LeaveEntity>
//{
//    public LeaveService(IDTRUnitOfWork uow) : base(uow) { }
//    protected override ValidationMessage AddOrUpdateValidation(LeaveEntity model)
//    {
//        var exists = _uow.Repository.Find<LeaveEntity>(x => (x.Description.ToLower() == model.Description.ToLower()) && x.Id != model.Id);
//        if (exists.Any())
//        {
//            return new ValidationMessage(false, "Description is already exists");
//        }
//        else if (string.IsNullOrWhiteSpace(model.Description))
//        {
//            return new ValidationMessage(false, "Invalid description name");
//        }
//        return base.AddOrUpdateValidation(model);
//    } 
//}
