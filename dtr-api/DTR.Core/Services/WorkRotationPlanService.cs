namespace DTR.Core;

public class WorkRotationPlanService : ServiceBase<WorkSchedulePlan>
{
    public WorkRotationPlanService(IDTRUnitOfWork uow) : base(uow)
    {
    }
    public override void AddRange(List<WorkSchedulePlan> models)
    {
        var recordKeys = models
        .Select(x => new { x.PayrollDate, x.EmployeeId })
        .Distinct()
        .ToList();

        var payrollDates = recordKeys.Select(k => k.PayrollDate).Distinct().ToList();
        var employees = recordKeys.Select(k => k.EmployeeId).Distinct().ToList();

        var allExisting = FindAll()
            .Where(x =>
                payrollDates.Contains(x.PayrollDate) &&
                employees.Contains(x.EmployeeId));
        allExisting.ExecuteDelete();

        base.AddRange(models);
    }
    public void DeleteBatch(Guid guid)
    {
        var data = FindAll().Where(x => x.Batch == guid);
        RemoveRange(data);
    }

    protected override ValidationMessage DeleteValidation(WorkSchedulePlan model)
    {
        return base.DeleteValidation(model);
    }
}
public record WorkSchedulePlanRecord(Guid Id, DateOnly PayrollDate, string FullName, string ShiftName);
public class WorkSchedulePlanRecordBatchResponse
{
    public string User { get; set; }
    public DateTime CreationTime { get; set; }
    public Guid Batch { get; set; }
}
