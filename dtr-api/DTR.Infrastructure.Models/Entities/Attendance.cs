using System.ComponentModel.DataAnnotations.Schema;
namespace DTR.Models;

public class Attendance : BaseEntity,IUserField
{
    public int BioId { get; set; }
    public DateTime WorkDateTime { get; set; }
    public string IP { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public Guid  EmployeeId { get; set; }
    public virtual Employee Employee  { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid? ClientId { get; set; }
    public Guid? BranchId  { get; set; }
    public Guid  UserId  { get; set; }=Guid.Empty;
    public string UserName { get; set; } = "Admin";
    public virtual int Workstate { get; set; }
    public virtual string Verifycode { get; set; } = string.Empty;
    public string LogRemarks { get; set; }= string.Empty;
    public string BatchCode { get; set; } = string.Empty;
    public string EditRemarks { get; set; } = string.Empty;
    public LOGSOURCE LogSource { get; set; } =  LOGSOURCE.UPLOADED;
    [NotMapped]
    public string PunchType { get; set; }=string.Empty;

    [NotMapped]
    public DTRStatus RecordStatus { get; set; } = DTRStatus.OPEN;
    public override string EntityType { get => this.GetType().Name; set => base.EntityType = value; }
}

public class UnkownEmpAttendance : BaseEntity
{
    public int BioId { get; set; } 
    public DateTime WorkDateTime { get; set; }
    public string IP { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public Guid UserId { get; set; } 
    public virtual int Workstate { get; set; }
    public virtual string Verifycode { get; set; } = string.Empty;
    public string LogRemarks { get; set; } = string.Empty;
    public string BatchCode { get; set; } = string.Empty;
    public string EditRemarks { get; set; } = string.Empty;
    public LOGSOURCE LogSource { get; set; }=LOGSOURCE.UPLOADED;
    public override string EntityType { get => this.GetType().Name; set => base.EntityType = value; }
}

public class LogLimit :BaseEntity
{
    public Guid Employee { get; set; }
    public DateOnly WorkDate { get; set; }
    public int LogOunt { get; set; }

}