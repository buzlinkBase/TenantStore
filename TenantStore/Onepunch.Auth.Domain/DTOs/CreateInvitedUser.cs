namespace OnePunch.Auth.Domain.DTOs;

public class CreateAccount
{
    public string Email { get; set; }
    public string Password { get; set; }
    public string CompanyName { get; set; }
}
public class CreateInvitedUser
{
    public string Token { get; set; }
    public string Name { get; set; } 
    public string Password  { get; set; }
    public Guid TenantId  { get; set; }
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
    public string Token  { get; set; }
    public string Password   { get; set; }
    public string ConfirmPassword  { get; set; }
}

public record ChangePassword
{
    public string Email   { get; set; }
    public string OldPassword  { get; set; }
    public string NewPassword { get; set; }
    public string ConfirmPassword { get; set; }
} 