using System.ComponentModel.DataAnnotations;

namespace DTR.Models;

public class WorkSchedulePlan : BaseEntity
{
    [Required]
    public DateOnly PayrollDate { get; set; }
    public Guid EmployeeId { get; set; }
    public virtual Employee Employee { get; set; }
    public Guid TimeShiftId { get; set; }
    public virtual TimeShift TimeShift { get; set; }
    public Guid Batch { get; set; }
    public string User { get; set; } = string.Empty;

}
