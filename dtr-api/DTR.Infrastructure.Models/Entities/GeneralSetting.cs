namespace DTR.Models;

public class GeneralSetting : BaseEntity
{
    public string IdentityType { get; set; }
    public string? IdentityTypeId { get; set; } //Unique Identifier
    public string Description { get; set; }
    public string Value { get; set; }
    public string? Metadata { get; set; }//json or something for flexibility
}
