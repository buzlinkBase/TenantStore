//namespace DTR.Core;

//public class SetRestDayService : ServiceBase<RestDayDate>
//{
//    public SetRestDayService(IDTRUnitOfWork uow) : base(uow) { }
//    public void SaveChange(RestDayDate model)
//    {
//        var useModel = _uow.Context.RestDayDates.FirstOrDefault(x => x.EmployeeId == model.EmployeeId && x.PayrollDate == model.PayrollDate);
//        if (useModel == null)
//        {
//            useModel = model;
//        }
//        AddOrUpdate(useModel);
//    }
//    public RestDayDate? GetOne(DateOnly date,Guid empId)=> _uow.Context.RestDayDates.FirstOrDefault(x=>x.PayrollDate==date && x.EmployeeId==empId);
//    public void Delete(RestDayDate  model) => _uow.Repository.Remove(model);
//    public List<RestDayDate> GetAll(DateOnly fromDate, DateOnly toDate)
//    {
//        return _uow.Context.RestDayDates
//            .Where(x => x.PayrollDate >= fromDate && x.PayrollDate <= toDate)
//            .ToList();
//    }
//}