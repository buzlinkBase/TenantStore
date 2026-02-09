namespace DTR.Core;

public class DailyRecordService : ServiceBase<DailyRecord>
{
    private CancellationToken Token;
    private readonly DataServiceResolver _dataServiceProvider;
    public DailyRecordService(IDTRUnitOfWork uow, DataServiceResolver dataServiceProvider) : base(uow)
    {
        _dataServiceProvider = dataServiceProvider;
    }

    public override void AddRange(List<DailyRecord> models)
    {
        //delete by employee and Date
        var dtrsIds = models.Select(x => x.EmployeeId).Distinct().ToList();
        var dtrsDate = models.Select(x => x.WorkDate).Distinct().ToList();
        var dtrBioId = models.Select(x => x.BioId).Distinct().ToList();
        var dtrempCode = models.Select(x => x.empCode).Distinct().ToList();

        _uow.Repository.Remove<DailyRecord>(x =>
            dtrsIds.Any(xx => xx == x.EmployeeId) &&
            dtrsDate.Any(xx => xx == x.WorkDate) &&
            dtrBioId.Any(xx => xx == x.BioId) &&
            dtrempCode.Any(xx => xx == x.empCode));

        //reset Id
        foreach (var dtr in models)
        {
            dtr.Id = Guid.Empty;
            dtr.RecordStatus = DTRStatus.LOCKED;
        }
        base.AddRange(models);
    }
    public List<DailyRecord> LoadDTRRawList(DTRRequestPayload payload, Specification<DailyRecord> specification)
    {
        return FindDTR(payload).ToList();
    }
    public List<DailyRecord> LoadDTRSummary(DTRRequestPayload payload)
    {
        return DTRSummaryQuery(payload);
    }
    private List<DailyRecord> DTRSummaryQuery(DTRRequestPayload payload)
    {
        return FindDTR(payload)
            .Where(x => x.RecordStatus == DTRStatus.LOCKED)
            .ToList()
            .GroupBy(x => x.EmployeeId)
            .Select(x => new DailyRecord()
            {
                BioId = x.FirstOrDefault().BioId,
                DepartmentId = x.FirstOrDefault().DepartmentId,
                FullName = x.FirstOrDefault().FullName,
                EmployeeId = x.Key,
                //hol Day
                LHHolidayTotalDays = x.Sum(x => x.LHHolidayTotalDays),
                SPHolidayTotalDays = x.Sum(x => x.SPHolidayTotalDays),
                //Regular Working Days
                RegularWorkingDays = x.Sum(x => x.RegularWorkingDays),
                RegularNDDays = x.Sum(x => x.RegularNDDays),
                RegularOTDays = x.Sum(x => x.RegularOTDays),
                RegularNDOTDays = x.Sum(x => x.RegularNDOTDays),
                //rest Working Days
                RestDayDays = x.Sum(x => x.RestDayDays),
                RestDayNDDays = x.Sum(x => x.RestDayNDDays),
                RestDayOTDays = x.Sum(x => x.RestDayOTDays),
                RestDayNDODays = x.Sum(x => x.RestDayNDODays),

                //minutes
                LateMinutes = x.Sum(x => x.LateMinutes),
                UTMinutes = x.Sum(x => x.UTMinutes),
                OverBreakMinutes = x.Sum(x => x.OverBreakMinutes),
                OTMinutes = x.Sum(x => x.OTMinutes),
                ND = x.Sum(x => x.ND),
                NDOT = x.Sum(x => x.NDOT),
                LH = x.Sum(x => x.LH),
                SP = x.Sum(x => x.SP),

                RegDayMinutes = x.Sum(x => x.RegDayMinutes),
                RegDayNDMinutes = x.Sum(x => x.RegDayNDMinutes),
                RegDayOTMinutes = x.Sum(x => x.RegDayOTMinutes),
                RegDayNDOMinutes = x.Sum(x => x.RegDayNDOMinutes),
                //rest
                RestDayMinutes = x.Sum(x => x.RestDayMinutes),
                RestDayNDMinutes = x.Sum(x => x.RestDayNDMinutes),
                RestDayOTMinutes = x.Sum(x => x.RestDayOTMinutes),
                RestDayNDOMinutes = x.Sum(x => x.RestDayNDOMinutes),
                LeaveMinutes = x.Sum(x => x.LeaveMinutes),
                //hours
                LateHours = x.Sum(x => x.LateHours),
                UTHours = x.Sum(x => x.UTMinutes).ToHour(),
                OverBreakHours = x.Sum(x => x.OverBreakHours),
                LateForOTHours = x.Sum(x => x.LateForOTHours),

                RegularNetHours = x.Sum(x => x.RegularNetHours),
                RegularOTHours = x.Sum(x => x.RegularOTHours),
                RegularNDHours = x.Sum(x => x.RegularNDHours),
                RegularNDOTHours = x.Sum(x => x.RegularNDOTHours),

                RestDayHours = x.Sum(x => x.RestDayHours),
                RestDayOTHours = x.Sum(x => x.RestDayOTHours),
                RestDayNDHours = x.Sum(x => x.RestDayNDHours),
                RestDayNDOTHours = x.Sum(x => x.RestDayNDOTHours),

                LegalHolHours = x.Sum(x => x.LegalHolHours),
                LegalHolOTHours = x.Sum(x => x.LegalHolOTHours),
                LegalHolNightDiffHours = x.Sum(x => x.LegalHolNightDiffHours),
                LegalHolNightDiffOTHours = x.Sum(x => x.LegalHolNightDiffOTHours),

                SpecialHolHours = x.Sum(x => x.SpecialHolHours),
                SpecialHolOTHours = x.Sum(x => x.SpecialHolOTHours),
                SpecialHolNightDiffHours = x.Sum(x => x.SpecialHolNightDiffHours),
                SpecialHolNightDiffOTHours = x.Sum(x => x.SpecialHolNightDiffOTHours),
                RawOTHours = x.Sum(x => x.RawOTHours),
                OB = x.Sum(x => x.OB),
                Absent = x.Sum(x => x.Absent),
            })
            .OrderBy(x => x.FullName)
            .ToList();
    }
    public List<DailyRecord> FindTardiness(DTRRequestPayload payload)
    {
        var data = DTRSummaryQuery(payload)
           .Where(x => x.LateHours > 0 || x.LateHours > 0
           || x.LeaveMinutes > 0 || x.OB > 0)
           .ToList();
        return data;
    }
    private IQueryable<DailyRecord> FindDTR(DTRRequestPayload payload)
    {
        return _uow.Repository
            .FindAll<DailyRecord>()
            .AsNoTracking()
            .Where(x =>
                 (x.WorkDate >= payload.FromDate && x.WorkDate <= payload.ToDate) &&
                 (payload.EmployeeId == null || x.EmployeeId == payload.EmployeeId) &&
                 (payload.DepartmentId == null || (x.DepartmentId.HasValue ? x.DepartmentId.Value == payload.DepartmentId : x.DepartmentId == payload.DepartmentId)) &&
                 (payload.PayrollGroupId == null || (x.PayrollGroupId.HasValue ? x.PayrollGroupId.Value == payload.PayrollGroupId : x.PayrollGroupId == payload.PayrollGroupId)) &&
                 (payload.ClientId == null || (x.ClientId.HasValue ? x.ClientId.Value == payload.ClientId : x.ClientId == payload.ClientId))
            );
    }
    public override void RemoveRange(IEnumerable<DailyRecord> models)
    {
        base.RemoveRange(models);
    }
}
