namespace TenantStoreApi.Domain.Entities;

/// <summary>
/// A line of business a company can be classified under (e.g. Retail). Platform reference data: not
/// tenant-owned and not [Audited]. A company stores the stable <see cref="Code"/>, never the Id, so
/// a rename here never touches company rows. Retired entries are made inactive, not deleted.
/// </summary>
public class Industry : BaseEntity
{
    /// <summary>Stable identifier, e.g. "retail".</summary>
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    public virtual ICollection<SubIndustry> SubIndustries { get; set; } = new List<SubIndustry>();
}

/// <summary>A narrower specialty under one <see cref="Industry"/> (e.g. Retail &gt; Grocery).</summary>
public class SubIndustry : BaseEntity
{
    public Guid IndustryId { get; set; }
    public virtual Industry Industry { get; set; } = null!;
    /// <summary>Stable identifier, unique across all sub-industries, e.g. "retail-grocery".</summary>
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}
