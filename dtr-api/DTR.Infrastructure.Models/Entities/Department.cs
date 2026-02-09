
namespace DTR.Models;

public class Department : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    public Guid? BranchId { get; set; }
    public Guid? HeadId { get; set; }
    public string? HeadName { get; set; } = string.Empty;
}