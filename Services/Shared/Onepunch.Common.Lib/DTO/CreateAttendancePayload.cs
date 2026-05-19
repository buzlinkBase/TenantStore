
using NetTopologySuite.Geometries;
using System.Net.NetworkInformation;
using System.Runtime;

namespace Onepunch.Common.Lib.DTO;

public class AttendancePayloadWrapper
{
    public List<CreateAttendancePayload> AttLogs { get; set; }
}

public class CreateAttendancePayload
{
    public Guid BatchId { get; set; }
    public int BioId { get; set; }
    public DateTime WorkDateTime { get; set; }
    public Guid TenantId { get; set; }
    public Guid? BranchId { get; set; }
    public Guid? ClientId { get; set; }
    public Guid? DepartmentId { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public string IPAddress { get; set; } = string.Empty;
    public Polygon? Coordinates { get; set; }
}

public record BatchAttConfirmation
{
    public Guid BatchId { get; set; }
}

public class DeviceCommandWrapper<T>
{
    public Guid TenantId { get; set; }
    public string DeviceSN { get; set; } = string.Empty;
    public List<T> Commands = new List<T>();
}

public class SetEmployeePayload
{
    public int BioId  { get; set; }
    public string Name { get; set; }
    public int Privilege { get; set; }
    public string Password { get; set; }
    public string Card { get; set; }

    //public string group_code { get; set; }
    //public string timezone_code { get; set; }
    //public string verification_mode { get; set; }
    //public string vice_card_no { get; set; }
    //public string valid_from { get; set; }
    //public string valid_until { get; set; }
}
 