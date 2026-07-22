
using NetTopologySuite.Geometries;

namespace Onepunch.Common.Lib.DTO;

public class AttendancePayloadWrapper
{
    public List<CreateAttendancePayload> AttLogs { get; set; }
}

public class CreateAttendancePayload
{
    public string BatchId { get; set; } = string.Empty;
    public int BioId { get; set; }
    public DateTime WorkDateTime { get; set; }
    public Guid TenantId { get; set; }
    public Guid? BranchId { get; set; }
    public Guid? ClientId { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid? OperationAreaId { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public string IPAddress { get; set; } = string.Empty;
    public Polygon? Coordinates { get; set; }
}

public record BatchAttConfirmation
{
    public string BatchId { get; set; } = string.Empty;
}

public class DeviceCommandWrapper<T> where T : class, new()
{
    public string DeviceSN { get; set; } = string.Empty;
    public T Commands = new();
}

public class SetEmployeeCommandPayload
{
    public int BioId { get; set; }
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

public class SyncBioPayload
{
    public int BioId { get; set; }
    public int Index { get; set; }
    public string Template { get; set; }
    public bool Dures { get; set; }
}

public class DeleteEmployeePayload
{
    public int BioId { get; set; }
}

public class DeleteFingerprintPayload
{
    public int BioId { get; set; }

    // Specific ZKTeco finger index (0-9). Null deletes all fingerprints for the user.
    public int? FingerIndex { get; set; }
}
