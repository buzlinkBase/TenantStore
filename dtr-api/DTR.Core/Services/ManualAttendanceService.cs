namespace DTR.Core;

public record ManualAttPayload(Attendance att, DateOnly payrollDate, DateTime WorkTime, Guid EmployeeId, string Remarks);

public class ManualAttendanceService : AttendanceService
{
    private readonly EmployeeService _employeeService;

    public ManualAttendanceService(IDTRUnitOfWork uow,
        EmployeeService employeeService, 
        ITenantProvider tenantProvider) : base(uow, tenantProvider)
    {
        _employeeService = employeeService;
    }
    public async Task AddOrUpdate(ManualAttPayload payload)
    {
        var emp = await _employeeService.GetOne(payload.EmployeeId);
        if (emp == null)
        {
            throw new Exception("Employee not loaded");
        }
        var model = FindOne(payload.att.Id);
        if (model == null)
        {
            model = new Attendance();
            model.BatchCode = Guid.NewGuid().ToString();
            model.LogSource = LOGSOURCE.MANUAL;
        }

        if (model.WorkDateTime == payload.WorkTime)
        {
            throw new Exception("Work Time is already exists");
        }

        model.BioId = emp.BioId;
        model.EmployeeId = emp.Id;
        model.WorkDateTime = payload.WorkTime;
        model.LogRemarks = payload.Remarks;
        model.ClientId = emp.ClientId;
        model.DepartmentId = emp.DepartmentId;

        if (payload.att.Id == Guid.Empty)
        {
            await AddPunchCounter(payload, emp);
        }

        base.AddOrUpdate(model);
    }
    public override void Remove(Attendance model)
    {
        //remove punch counter
        if (model.LogSource == LOGSOURCE.MANUAL)
        {
            var lpc = _uow.Repository.Find<DailyRecordPunchCounter>(x => x.EmployeeId == model.EmployeeId
             && x.WorkTime == model.WorkDateTime)
                .FirstOrDefault()
                ;
            if (lpc != null && lpc.ManualPunchCount > 0)
            {
                lpc.ManualPunchCount -= 1;
                _uow.Repository.AddOrUpdate(lpc);
            }
        }
        base.Remove(model);
    }
    private async Task AddPunchCounter(ManualAttPayload payload, Employee employee)
    {
        var punchCounterModel = _uow.Repository.FindAll<DailyRecordPunchCounter>()
           .Where(x => x.EmployeeId == payload.EmployeeId
                    && x.PayrollDate == payload.payrollDate)
           .FirstOrDefault()
           ;


        if (punchCounterModel == null)
        {
            punchCounterModel = new DailyRecordPunchCounter();
        }

        if (!await AddPunchValidation(employee, punchCounterModel)) return;

        punchCounterModel.ManualPunchCount += 1;
        punchCounterModel.EmployeeId = payload.EmployeeId;
        punchCounterModel.PayrollDate = payload.payrollDate;
        punchCounterModel.WorkTime = payload.WorkTime;
        _uow.Repository.AddOrUpdate(punchCounterModel);
    }
    private async Task<bool> AddPunchValidation(Employee employee, DailyRecordPunchCounter model)
    {
        var setting = await new GeneralSettingService(_uow).GetSettingsAsync("Company");
        if (!setting.TryGetValue(SettingKey.AttFillLimit.ToString(), out var policyModel))
        {
            return true;//no limit
        }

        var policy = GeneralSettingsUtil.ParseEnum(policyModel.Value, ManualEntryLimitEnum.NOLIMIT);
        switch (policy)
        {
            case ManualEntryLimitEnum.NOLIMIT:
                return true;
            case ManualEntryLimitEnum.DONTALLOW:
                throw new Exception("Manual entry is not allowed");
            case ManualEntryLimitEnum.ONE:
                if (model.ManualPunchCount >= 1)
                {
                    throw new Exception("The manual punch entry limit is reached.");
                }
                return true;
            case ManualEntryLimitEnum.TWO:
                if (model.ManualPunchCount >= 2)
                {
                    throw new Exception("The manual punch entry limit is reached.");
                }
                return true;
            case ManualEntryLimitEnum.THREE:
                if (model.ManualPunchCount >= 3)
                {
                    throw new Exception("The manual punch entry limit is reached.");
                }
                return true;
            case ManualEntryLimitEnum.FOUR:
                if (model.ManualPunchCount >= 4)
                {
                    throw new Exception("The manual punch entry limit is reached.");
                }
                return true;
            default:
                return false;
        }
    }
    protected override ValidationMessage AddOrUpdateValidation(Attendance model)
    {
        if (model.WorkDateTime == DateTime.MinValue)
        {
            return new ValidationMessage(false, "Invalid work time");
        }

        //var tenant = BzServiceProvider.Instance.GetService<ITenantProvider<GuidId>>();
        //var cs = new CompanyRuleService(_uow).FindOne(tenant.TenantId.Value);
        //var emp = new EmployeeService(_uow).FindOne(model.Id);
        //var employees = new List<Employee> { emp };
        //var CurrentShifts = new WorkScheduleService(_uow).GetShifts(DateOnly.FromDateTime(model.WorkDateTime.AddDays(-1)), DateOnly.FromDateTime(model.WorkDateTime.AddDays(1)), employees.AsEnumerable());


        //var csk = new CurrentTimeShiftKey(model.EmployeeId, DateOnly.FromDateTime(model.WorkDateTime));
        //CurrentShifts.TryGetValue(csk, out var shift);

        //var cspP = new CurrentShiftProviderPayload(CurrentShifts, model.Employee, DateOnly.FromDateTime(model.WorkDateTime));
        //var csp = new CurrentShiftProvider(cspP);

        //var priorShift = csp.GetPreviousShift();
        //var nextShift = csp.GetNextShift();

        //if (model.WorkDateTime < priorShift.EndTime || model.WorkDateTime > nextShift.StartTime)
        //{
        //    return new ValidationMessage(false, "The specified time conflicts with the timetable of the other shifts.");
        //}

        //var lpc = _uow.Repository.Find<DailyRecordPunchCounter>(x => x.EmployeeId == model.EmployeeId
        //    && x.WorkTime == model.WorkDateTime)
        //       .FirstOrDefault();

        //var context = new ManualAddAttContext { CurrentCount = lpc?.ManualPunchCount ?? 0 };
        //var manualPunchRule = new PunchCountPipeLine(cs).Apply(context);
        //if (!manualPunchRule)
        //{
        //    return new ValidationMessage(false, "Reach the limit on the number of modifications.");
        //}

        return base.AddOrUpdateValidation(model);
    }
    protected override ValidationMessage DeleteValidation(Attendance model)
    {
        if (model.LogSource != LOGSOURCE.MANUAL)
        {
            return new ValidationMessage(false, "We can only remove manualy added log");
        }
        return base.DeleteValidation(model);
    }
}
