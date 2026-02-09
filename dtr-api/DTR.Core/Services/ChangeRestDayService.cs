namespace DTR.Core;


public class ChangeRestDayService : ServiceBase<ChangeRestDay>
{
    public ChangeRestDayService(IDTRUnitOfWork uow) : base(uow) { }

    public void SaveChange(ChangeOffModel changeOffs)
    {
        var Ids = changeOffs.EmployeeIds;
        if (!Ids.Any()) return;

        var batches = FindAll()
            .Where(x => Ids.Any(xx => xx == x.EmployeeId)
                && x.PayrollDate == changeOffs.PayrolLDateFrom)
            .Select(x => x.BatchEntryId)
            .ToList();

        var existing = FindAll()
           .Where(x => batches.Any(xx => xx == x.BatchEntryId)
            && Ids.Contains(x.EmployeeId))
           .ToList();

        RemoveRange(existing);

        _uow.Repository.SaveChanges();

        AddNewDayOff(changeOffs);
    }
    private void AddNewDayOff(ChangeOffModel changeOff)
    {
        var batchId = Guid.NewGuid();
        foreach (var emp in changeOff.EmployeeIds)
        {
            var entity1 = new ChangeRestDay()
            {
                DayName = changeOff.FromDay,
                State = ChangeSchedState.OVERRIDEN,
                PayrollDate = changeOff.PayrolLDateFrom,
                EmployeeId = emp,
                BatchEntryId = batchId,
            };
            var entity2 = new ChangeRestDay()
            {
                DayName = changeOff.ToDay,
                State = ChangeSchedState.REPLACEMENT,
                PayrollDate = changeOff.PayrolLDateTo,
                EmployeeId = emp,
                BatchEntryId = batchId,
            };
            _uow.Repository.Add(entity1);
            _uow.Repository.Add(entity2);
        }
    }
    public async Task<Dictionary<ResDaykey, CurrentRestDay>> GetAllDayOffAsync(DateOnly fromDate, DateOnly toDate, List<Employee> employees)
    {
        Dictionary<ResDaykey, List<ChangeRestDay>> allOff = await _uow.Repository
            .Find<ChangeRestDay>(x => x.PayrollDate >= fromDate && x.PayrollDate <= toDate)
            .GroupBy(x => new ResDaykey(x.EmployeeId, x.PayrollDate))
            .ToDictionaryAsync(x => x.Key, x => x.ToList())
            ;


        var allSpecDates = await _uow.Repository
           .Find<RestDayDate>(x => x.PayrollDate >= fromDate && x.PayrollDate <= toDate)
           .GroupBy(x => new ResDaykey(x.EmployeeId, x.PayrollDate))
           .ToDictionaryAsync(x => x.Key, x => x.FirstOrDefault())
           ;

        var overrideOff = new OverrideDayOffHandler(allOff);
        var specDateOff = new SpecificDateOffHandler(allSpecDates);
        var fallback = new FallBackDayOffHandler(allOff);
        overrideOff.SetNextHandler(specDateOff);
        specDateOff.SetNextHandler(fallback);

        var restDayCollection = new Dictionary<ResDaykey, CurrentRestDay>();
        for (DateOnly curdate = fromDate; curdate <= toDate; curdate = curdate.AddDays(1))
        {
            foreach (var employee in employees)
            {
                var result = overrideOff.Handle(employee, curdate);
                if (result != null)
                {
                    var key = new ResDaykey(employee.Id, curdate);
                    restDayCollection[key] = result;
                }
            }
        }
        return restDayCollection;
    }

    public List<RestDayRecordResponse> FindList(PayrollGroup? payrollGroup,
        Employee? employee,
        Client? client,
        DateOnly fromDate, DateOnly toDate)
    {
        //flatten
        return FindAll()
        .Where(x =>
            (payrollGroup == null || x.Employee.PayrollGroupId == payrollGroup.Id) &&
            (employee == null || x.EmployeeId == employee.Id) &&
            (client == null || x.Employee.ClientId == client.Id) &&
            x.PayrollDate >= fromDate && x.PayrollDate <= toDate)
        .ToList()
        .GroupBy(x => new { x.BatchEntryId, x.EmployeeId })
        .Select(g =>
        {
            var ordered = g.OrderBy(x => x.PayrollDate).ToList();
            return new RestDayRecordResponse
            {
                BatchId = g.Key.BatchEntryId,
                FullName = ordered.First().Employee.FullName,
                FromDate = ordered.First().PayrollDate,
                ToDate = ordered.Last().PayrollDate
            };
        })
        .OrderBy(x => x.FromDate)
        .ToList();

    }

    //public List<EmployeeRecord> FindAllEmpWithOff(
    //    DayName off,
    //    PayrollGroup? payrollGroup,
    //    Employee? employee, Client? client)
    //{
    //    return _uow.Repository.FindAll<Employee>()
    //        .Include(x => x.RestDays)
    //        .Where(x => x.RestDays.Any(r => r.DayName == off) &&
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
    //        .OrderBy(x => x.FullName)
    //        .ToList(); ;
    //}
}

public readonly record struct ResDaykey(Guid EmpId, DateOnly RestDay);
public class ChangeOffModel
{
    public DayName FromDay { get; set; }
    public DayName ToDay { get; set; }
    public DateOnly PayrolLDateFrom { get; set; }
    public DateOnly PayrolLDateTo { get; set; }
    public Guid[] EmployeeIds { get; set; }
}

public class RestDayRecordResponse
{
    public Guid BatchId { get; set; }
    public string FullName { get; set; }
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
}
