using System.ComponentModel.DataAnnotations.Schema;

namespace DTR.Core;

public class UserModel
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;

    public Guid? EmployeeId { get; set; }
    public string? Position { get; set; }
    public string Salt { get; set; } = string.Empty;
    public bool IsHidden { get; set; }
    public bool ResetOnLogon { get; set; }
    [NotMapped]
    public static bool IsOverride { get; set; }
    public string UserType { get; set; } = string.Empty;

    public bool Verified { get; set; }
    public bool IsLocked { get; set; }

    public bool RequestDeletion { get; set; }
    public bool Disabled { get; set; }
    public string Remarks { get; set; } = string.Empty;
    public DateTime last_login { get; set; }
    public string app_version { get; set; } = string.Empty;

    //public List<Claims> Claims { get; set; }
}