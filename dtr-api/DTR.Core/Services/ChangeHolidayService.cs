 

namespace DTR.Core;

public class ChangeHolidayService : ServiceBase<ChangeHoliday>
{
    public ChangeHolidayService(IDTRUnitOfWork uow) : base(uow) { }
    public void SaveChange(ChangeHolidayModel holidayModel)
    {
        var Ids = holidayModel.EmployeeIds;
        if (!Ids.Any()) return;

        var batches = FindAll()
            .Where(x => Ids.Any(xx => xx == x.EmployeeId)
                 && x.HolidayId == holidayModel.Holiday.Id)
            .Select(x => x.BatchEntryId)
            .ToList();

        var existing = FindAll()
           .Where(x => batches.Any(xx => xx == x.BatchEntryId))
           .ToList();

        RemoveRange(existing);

        _uow.Repository.SaveChanges();

        AddChangeHoliday(holidayModel);

    }

    //public List<EmployeeRecord> FindEmpForChangeHoliday(
    //    PayrollGroup? payrollGroup,
    //    Employee? employee, Client? client)
    //{
    //    return _uow.Repository.FindAll<Employee>()
    //        .Where(x =>
    //            (payrollGroup == null || x.PayrollGroupId == payrollGroup.Id)
    //            && (employee == null || x.Id == employee.Id)
    //            && (client == null || x.ClientId == client.Id)
    //            )
    //        .Select(x => new EmployeeRecord
    //        {
    //            Id = x.Id,
    //            FullName = x.FullName,
    //            Selection = false,
    //        })
    //        .AsEnumerable()
    //        .OrderBy(x=>x.FullName)
    //        .ToList();
    //}

    public List<ChangeHolidayResponse> FindList(PayrollGroup? payrollGroup,
        Employee? employee,
        Client? client,
        DateOnly fromDate, DateOnly toDate)
    {
        //flatten
        return FindAll().Where(x =>
            (payrollGroup == null || x.Employee.PayrollGroupId == payrollGroup.Id) &&
            (employee == null || x.EmployeeId == employee.Id) &&
            (client == null || x.Employee.ClientId == client.Id) &&
            x.PayrollDate >= fromDate && x.PayrollDate <= toDate)
        .ToList() 
        .GroupBy(x => new { x.BatchEntryId, x.EmployeeId })
        .Select(g =>
        {
            var ordered = g.OrderBy(x => x.PayrollDate).ToList();
            return new ChangeHolidayResponse
            {
                BatchId = g.Key.BatchEntryId,
                FullName = ordered.FirstOrDefault()?.Employee?.FullName,
                HolidayName = ordered.FirstOrDefault()?.Holiday?.Description,
                FromDate = ordered.FirstOrDefault()?.PayrollDate ?? default,
                ToDate = ordered.LastOrDefault()?.PayrollDate ?? default
            };
        })
        .OrderBy(x => x.FromDate)
        .ToList();
        }

    private void AddChangeHoliday(ChangeHolidayModel holidayModel)
    {
        var batchId = Guid.NewGuid();
        foreach (var emp in holidayModel.EmployeeIds)
        {
            var entity1 = new ChangeHoliday()
            {

                State = ChangeSchedState.OVERRIDEN,
                PayrollDate = holidayModel.PayrollDateFrom,
                EmployeeId = emp,
                HolidayId = holidayModel.Holiday.Id,
                BatchEntryId = batchId,
            };
            var entity2 = new ChangeHoliday()
            {
                State = ChangeSchedState.REPLACEMENT,
                PayrollDate = holidayModel.PayrollDateTo,
                EmployeeId = emp,
                HolidayId = holidayModel.Holiday.Id,
                BatchEntryId = batchId,
            };
            _uow.Repository.Add(entity1);
            _uow.Repository.Add(entity2);
        }
    }

}
public class ChangeHolidayModel
{
    public Holiday Holiday { get; set; }
    public DateOnly PayrollDateFrom { get; set; }
    public DateOnly PayrollDateTo { get; set; }
    public Guid[] EmployeeIds { get; set; }
}

public class ChangeHolidayResponse
{
    public Guid BatchId { get; set; }
    public string HolidayName { get; set; }
    public string ClientName { get; set; }
    public string FullName { get; set; }
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
}

public class EmployeeRecord
{
    public Guid Id { get; set; }
    public string FullName { get; set; }
    public bool Selection { get; set; }

}