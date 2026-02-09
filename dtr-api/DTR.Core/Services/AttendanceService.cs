namespace DTR.Core;

public class AttendanceService : ServiceBase<Attendance>
{
    private readonly ITenantProvider _tenantProvider;
    public AttendanceService(IDTRUnitOfWork uow,
        ITenantProvider tenantProvider) : base(uow)
    {
        _tenantProvider = tenantProvider;
    }

    public override void AddOrUpdate(Attendance model)
    {
        base.AddOrUpdate(model);
    }
    public override async Task AddRangeAsync(List<Attendance> attendances)
    {
        if (!attendances.Any()) return;

        var recordKeys = attendances
             .Select(x => new { x.BioId, x.WorkDateTime })
             .Distinct()
             .ToList();

        var bioIds = recordKeys.Select(k => k.BioId).Distinct().ToList();
        var workDates = recordKeys.Select(k => k.WorkDateTime).Distinct().ToList();

        var allExisting = _uow.Repository
            .Find<Attendance>(x =>
                bioIds.Contains(x.BioId) &&
                workDates.Contains(x.WorkDateTime));

        await allExisting.ExecuteDeleteAsync();
        foreach (var attendance in attendances)
        {
            attendance.Id = Guid.Empty;
            //attendance.UserId =   _currentUser.Id;
            attendance.UserName = "";
            attendance.TenantId = _tenantProvider.TenantId;
        }
        await base.AddRangeAsync(attendances);

    }

    //public async Task<List<Attendance>> FindRawAtt(DateOnly from, DateOnly to, Employee? employee, Department? department, Client? client, PayrollGroup? payrollGroup)
    //{
    //    var hasView = true;
    //    var spec = new UserHasViewSpec<Attendance>(hasView);

    //    var data = await _uow.Repository
    //      .Find(spec)
    //      .Where(x =>
    //          DateOnly.FromDateTime(x.WorkDateTime) >= from &&
    //          DateOnly.FromDateTime(x.WorkDateTime) <= to &&
    //          (employee == null || x.EmployeeId == employee.Id) &&
    //          (payrollGroup == null || x.Employee.PayrollGroupId == payrollGroup.Id) &&
    //          (client == null || x.Employee.ClientId == client.Id) &&
    //          (department == null || x.DepartmentId == department.Id)
    //          )
    //      .AsNoTracking()
    //      .Include(x => x.Employee)
    //          .ThenInclude(x => x.RestDays)
    //      //.Include(x => x.Employee.TimeShift)
    //      .ToListAsync()
    //      ;

    //    return data
    //        .OrderBy(x => x.Employee.FullName)
    //        .ThenBy(x => x.WorkDateTime)
    //        .ToList();
    //}

    public override void AddRange(List<Attendance> models)
    {
        base.AddRange(models);
    }

    public async Task<Dictionary<AttendanceEmpId, List<Attendance>>> LoadAttForDTRProcess(DateOnly from,
        DateOnly to,
        bool canProcess,
        Guid? EmployeeId,
        Guid? departmentId,
        Guid? clientId,
        Guid? payrollGroupId)
    {
        // Build DTR lookup based on EmployeeId and Date

        var dtrLookup = await _uow.Context.DailyRecords
            .Where(dtr => dtr.WorkDate >= from &&
                          dtr.WorkDate <= to)
            .Select(dtr => new
            {
                dtr.EmployeeId,
                dtr.WorkDate
            })
            .ToListAsync();

        var dtrSet = new HashSet<(Guid EmployeeId, DateOnly WorkDate)>(
            dtrLookup.Select(d => (d.EmployeeId, d.WorkDate))
        );

        var spec = new UserHasViewSpec<Attendance>(canProcess);

        var data = await _uow.Repository
            .Find(spec)
            .AsNoTracking()
            .AsSingleQuery()
            .Where(x =>
                DateOnly.FromDateTime(x.WorkDateTime) >= from &&
                DateOnly.FromDateTime(x.WorkDateTime) <= to &&
                (EmployeeId == null || EmployeeId == Guid.Empty || x.EmployeeId == EmployeeId) &&
                (payrollGroupId == null || payrollGroupId == Guid.Empty || x.Employee.PayrollGroupId == payrollGroupId) &&
                (clientId == null || clientId == Guid.Empty || x.ClientId == clientId) &&
                (departmentId == null || departmentId == Guid.Empty || x.Employee.DepartmentId == departmentId))
            .Include(x => x.Employee)
            //.Include(x => x.Employee.TimeShift)
            .ToListAsync();

        foreach (var attendance in data)
        {
            var key = (attendance.EmployeeId, DateOnly.FromDateTime(attendance.WorkDateTime));
            if (dtrSet.Contains(key))
                attendance.RecordStatus = DTRStatus.LOCKED;
        }

        var result = data
            .GroupBy(a => new AttendanceEmpId(a.EmployeeId))
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(x => x.WorkDateTime).ToList()
            );

        return result;
    }
    public override void Remove(Guid Id)
    {
        try
        {
            base.Remove(Id);
        }
        catch (Exception ex)
        {
        }
    }
    public override void Remove(Attendance model)
    {
        base.Remove(model);
    }
}
public readonly record struct AttendanceEmpId(Guid EmpId);
