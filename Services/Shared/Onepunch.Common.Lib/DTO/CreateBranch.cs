using NetTopologySuite.Geometries;

namespace Onepunch.Common.Lib.DTO;

public class CreateBranch
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    //public string? ShortName { get; set; } = string.Empty;
    public string? Address { get; set; }
    //public string? Contact { get; set; }
    //public string? ManagerName { get; set; }
    //public string Email { get; set; } = string.Empty;
    public Polygon? Boundary { get; set; }
    public string Status { get; set; } = "Active";
}
public class UpdateBranch : CreateBranch
{
    public Guid Id { get; set; }
}

public partial class BranchModel : UpdateBranch
{
}