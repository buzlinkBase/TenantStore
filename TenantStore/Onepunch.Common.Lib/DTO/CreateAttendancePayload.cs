namespace Onepunch.Common.Lib.DTO;
public class CreateAttendancePayload
{
    public int BioId { get; set; }
    public DateTime WorkDateTime { get; set; }
    public Guid TenantId  { get; set; }
    public Guid? BranchId { get; set; }
    public Guid? ClientId { get; set; }
    public Guid? DepartmentId { get; set; }
    public string DeviceName { get; set; }
}
