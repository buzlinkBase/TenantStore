using System.ComponentModel.DataAnnotations.Schema;
namespace DTR.Models;
public class Employee : BaseEntity
{
    public int BioId { get; set; } = 0;
    public Guid? DepartmentId { get; set; }
    public Guid? PayrollGroupId { get; set; }
    public Guid? ClientId { get; set; }
    //public virtual Department? Department { get; set; }
    //public virtual PayrollGroup? PayrollGroup { get; set; }
    //public virtual Client? Client { get; set; }
    //public virtual OperationArea? Area { get; set; }
    public Guid? AreaId { get; set; }
    public Guid? BranchId { get; set; }
    public Guid? SectionId { get; set; }
    public Guid? PositionId { get; set; }
    public Guid? TimeShiftId { get; set; }
    public DateTime DateRegistered { get; set; }
    public virtual ICollection<RestDay> RestDays { get; set; }

    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string MiddleName { get; set; } = string.Empty;
    public string Suffix { get; set; } = string.Empty;
    public string EmployeeNo { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty; 

    [NotMapped]
    public string FullName => FormatFullName();
    public string FormatFullName()
    {
        string full_name = $"{LastName}, {FirstName} {Suffix} {MiddleName}".Trim();
        if (full_name.Trim().StartsWith(","))
        {
            full_name = full_name.Substring(1, full_name.Length - 1);
        }
        else if (full_name.Trim().StartsWith("-, "))
        {
            full_name = full_name.Substring(3, full_name.Length - 3);
        }
        if (full_name.EndsWith(','))
        {
            full_name = full_name.TrimEnd(',');
        }
        return full_name;
    } 
}
