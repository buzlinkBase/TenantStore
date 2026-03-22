using MessagePack;
using System.Drawing;

namespace Onepunch.Common.Lib.DTO;

public class CreateBranch
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? ShortName { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? Contact { get; set; }
    public string? ManagerName { get; set; }
    public Point? Corrdinates { get; set; }
}
public class UpdateBranch : CreateBranch
{
    public Guid Id { get; set; }
    public string Status { get; set; }
}

[MessagePackObject]
public partial class BranchModel
{
    [Key(0)]
    public Guid Id { get; set; }
    [Key(1)]
    public string Code { get; set; }
    [Key(2)]
    public required Guid TenantId { get; set; }
    [Key(3)]
    public required string Name { get; set; }
    [Key(4)]
    public string? ShortName { get; set; } = string.Empty;
    [Key(5)]
    public string? Address { get; set; }
    [Key(6)]
    public string? Contact { get; set; }
    [Key(7)]
    public string? ManagerName { get; set; }
    [Key(8)]
    public string Status { get; set; }
    [Key(9)]
    public DateTime? DeletedAt { get; set; }
}