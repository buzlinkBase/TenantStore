using MessagePack;
namespace DTR.Models;

[MessagePackObject]
public class Employee
{
    [Key(0)] public Guid Id { get; set; }
    [Key(1)] public int BioId { get; set; } = 0;
    [Key(2)] public Guid? DepartmentId { get; set; }
    [Key(3)] public Guid? PayrollGroupId { get; set; }
    [Key(4)] public Guid? ClientId { get; set; }
    [Key(5)] public Guid? AreaId { get; set; }
    [Key(6)] public Guid? BranchId { get; set; }
    [Key(7)] public Guid? SectionId { get; set; }
    [Key(8)] public Guid? PositionId { get; set; }
    [Key(9)] public Guid? TimeShiftId { get; set; }
    [Key(10)] public string FirstName { get; set; } = string.Empty;
    [Key(11)] public string LastName { get; set; } = string.Empty;
    [Key(12)] public string MiddleName { get; set; } = string.Empty;
    [Key(13)] public string Suffix { get; set; } = string.Empty;
    [Key(14)] public virtual List<RestDayModel> RestDays { get; set; }
    [Key(15)] public string? DepartmentName { get; set; }
    [Key(16)] public string? FullName { get; set; }
}

[MessagePackObject]
public class RestDayModel
{
    [Key(0)] public Guid Id { get; set; }
    [Key(1)] public DayName DayName { get; set; }
}