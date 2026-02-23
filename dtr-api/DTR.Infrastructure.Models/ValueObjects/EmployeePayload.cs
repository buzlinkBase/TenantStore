using System.ComponentModel.DataAnnotations.Schema;

namespace DTR.Models.ValueObjects;

public class EmployeePayload : BasePayload
{
    public int BioId { get; set; } = 0;
    public Guid? DepartmentId { get; set; }
    public Guid? PayrollGroupId { get; set; }
    public Guid? ClientId { get; set; }
    public Guid? AreaId { get; set; }
    public Guid? BranchId { get; set; }
    public Guid? SectionId { get; set; }
    public Guid? PositionId { get; set; }
    public Guid? TimeShiftId { get; set; }
    public DateTime DateRegistered { get; set; }
    public virtual List<RestDay> RestDays { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string MiddleName { get; set; } = string.Empty;
    public string Suffix { get; set; } = string.Empty;
    public string EmployeeNo { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
}
