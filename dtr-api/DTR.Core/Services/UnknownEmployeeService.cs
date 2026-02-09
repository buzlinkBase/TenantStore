namespace DTR.Core;

public class UnknownEmployeeService : ServiceBase<UnkownEmpAttendance>
{
    private readonly ITenantProvider _tenantProvider;

    public UnknownEmployeeService(IDTRUnitOfWork uow, ITenantProvider tenantProvider) : base(uow)
    {
        _tenantProvider = tenantProvider;
    }
    public async Task<List<UnkownEmpAttendance>> FindByDateAsync(DateOnly from, DateOnly to)
    {
        var data = await _uow.Repository
            .Find<UnkownEmpAttendance>(x => DateOnly.FromDateTime(x.WorkDateTime) >= from
             && DateOnly.FromDateTime(x.WorkDateTime) <= to)
            .AsNoTracking()
            .ToListAsync();
        ;
        return data;
    }
    public override async Task AddRangeAsync(List<UnkownEmpAttendance> models)
    {
        if (models == null || models.Count == 0) return;

        foreach (var att in models)
        {
            att.TenantId = _tenantProvider.TenantId;
        }

        var recordKeys = models
            .Select(x => new { x.BioId, x.WorkDateTime })
            .Distinct()
            .ToList();


        var bioIds = recordKeys.Select(k => k.BioId).Distinct().ToList();
        var workDates = recordKeys.Select(k => k.WorkDateTime).Distinct().ToList();

        var allExisting = _uow.Repository
            .Find<UnkownEmpAttendance>(x =>
                bioIds.Contains(x.BioId) &&
                workDates.Contains(x.WorkDateTime));


        await allExisting.ExecuteDeleteAsync();

        await base.AddRangeAsync(models);
    }
}