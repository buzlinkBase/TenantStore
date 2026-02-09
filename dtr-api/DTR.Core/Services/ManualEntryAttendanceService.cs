namespace DTR.Core;

public class ManualEntryAttPayload
{
    public string BatchCode { get; set; }
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    public TimeSpan? StartTime { get; set; }
    public TimeSpan? EndTime { get; set; }
    public List<Employee> Employees { get; set; }
}
public class ManualEntryAttendanceService : AttendanceService
{
    public ManualEntryAttendanceService(IDTRUnitOfWork uow, ITenantProvider tenantProvider) : base(uow,  tenantProvider )
    {
    }
    public async Task Upload(ManualEntryAttPayload payload)
    {
        var atts = new List<Attendance>();

        foreach (var emp in payload.Employees)
        {
            for (DateOnly curDate = payload.FromDate; curDate <= payload.ToDate; curDate = curDate.AddDays(1))
            {
                DateTime? startTime = null;
                DateTime? endTime = null;
                if (payload.StartTime.HasValue)
                {
                    startTime = curDate.AddDays(payload.StartTime.Value.Days).ToDateTime(TimeOnly.FromTimeSpan(payload.StartTime.Value - TimeSpan.FromDays(payload.StartTime.Value.Days)));
                }
                if (payload.EndTime.HasValue)
                {
                    endTime = curDate.AddDays(payload.EndTime.Value.Days).ToDateTime(TimeOnly.FromTimeSpan(payload.EndTime.Value - TimeSpan.FromDays(payload.EndTime.Value.Days)));
                }
                if (payload.StartTime.HasValue && startTime.HasValue)
                {
                    atts.Add(new Attendance
                    {
                        BioId = emp.BioId,
                        DepartmentId = emp.DepartmentId,
                        EmployeeId = emp.Id,
                        WorkDateTime = startTime.Value,
                        BatchCode = payload.BatchCode,
                        ClientId = emp.ClientId,
                        BranchId = emp.BranchId,
                        LogSource = LOGSOURCE.MANUAL,
                    });
                }
                if (payload.EndTime.HasValue && endTime.HasValue)
                {
                    atts.Add(new Attendance
                    {
                        BioId = emp.BioId,
                        DepartmentId = emp.DepartmentId,
                        EmployeeId = emp.Id,
                        WorkDateTime = endTime.Value,
                        BatchCode = payload.BatchCode,
                        ClientId = emp.ClientId,
                        BranchId = emp.BranchId,
                        LogSource = LOGSOURCE.MANUAL,
                    });
                }
            }
        }

        if (atts.Any())
        {
            await base.AddRangeAsync(atts); 
        }
    }

    public async Task<List<ManualBatchEntryLogEntity>> FindLogByDate(DateOnly from, DateOnly to)
    {

        var rawData = await _uow.Repository
        .Find<ManualBatchEntryLogEntity>(x => x.FromDate >= from && x.ToDate <= to)
        .ToListAsync();   

        var data = rawData
            .GroupBy(x => x.BatchCode)
            .Select(g =>
            {
                var minEntry = g.OrderBy(x => x.FromDate).First();
                var maxEntry = g.OrderByDescending(x => x.ToDate).First();

                return new ManualBatchEntryLogEntity
                {
                    BatchCode = g.Key,
                    FromDate = minEntry.FromDate,
                    ToDate = maxEntry.ToDate,
                    User = minEntry.User,
                    Time1 = minEntry.Time1,
                    Time2 = maxEntry.Time2
                };
            })
            .ToList();
        return data;
    }

    public async Task<List<Attendance>> FindLogByBatch(string batchCode)
    {
        var data = await FindAll()
            .Where(x => x.BatchCode == batchCode)
            .ToListAsync()
            ;
        return data;
    }

    public async Task DeleteAsync(string batchCode)
    {
        var records = await FindAll()
            .Where(x => x.BatchCode == batchCode)
            .ToListAsync();

        var LockedAtt = records
            .Where(x => x.RecordStatus == DTRStatus.LOCKED);

        if (LockedAtt.Any())
        {
            if (records.Count() == LockedAtt.Count())
            {
                throw new Exception("Attendance were already locked");
            }
            else
            {
                throw new Exception("some of the attendance were already locked");
            }
        }
        _uow.Repository.Remove<Attendance>(x => x.BatchCode == batchCode);
        _uow.Repository.Remove<ManualBatchEntryLogEntity>(x => x.BatchCode == batchCode);
    }

    protected override ValidationMessage AddOrUpdateValidation(Attendance model)
    {
        return base.AddOrUpdateValidation(model);
    }
}
