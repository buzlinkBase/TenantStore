using System.ComponentModel.DataAnnotations;

namespace OnePunch.Auth.Domain.DTOs;

public class CreateAccount
{
    [EmailAddress(ErrorMessage = "Invalid email address")]
    [Required(ErrorMessage = "Email is required")]
    public required string Email { get; set; }
    public string? Name { get; set; }
    [Required(ErrorMessage = "Invalid Password")]
    public required string Password { get; set; }
}
public class CreateInvitedUser
{
    public string Token { get; set; }
    public string Name { get; set; }
    public string Password { get; set; }
    public Guid TenantId { get; set; }
}

public class UpdateUser
{
    public Guid Id { get; set; }
    public string Email { get; set; }
}
public class UserModel : UpdateUser
{
}

public record ResetPassword
{
    public string Token { get; set; }
    public string NewPassword { get; set; }
}

public record ChangePassword
{
    public string Email { get; set; }
    public string OldPassword { get; set; }
    public string NewPassword { get; set; }
    public string ConfirmPassword { get; set; }
}
public record SetPassword
{
    public string Password { get; set; }
    public string ConfirmPassword { get; set; }
}

public record UpdateProfileRequest
{
    public string? FullName { get; set; }
    public string? PhoneNumber { get; set; }
}

