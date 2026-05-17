
using NetTopologySuite.Geometries;

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

public class DeviceCommandWrapper
{
    public List<DeviceCommandPayload> Commands = new List<DeviceCommandPayload>();
}
public class DeviceCommandPayload
{
    public string DeviceSN { get; set; } = string.Empty;
    public Guid TenantId { get; set; }
    public string CommandMessage { get; set; } = string.Empty;
}
