namespace OnePunch.Auth.Domain.DTOs;

//public record RegisterTenantPayload
//{
//    public string Email { get; set; }
//    public string? Name  { get; set; }
//    public string Password { get; set; }
//    public Guid TenantId { get; set; }
//}

public record InvitationPayload
{
    public string? Name { get; set; }
    public string Email { get; set; }

}
public class CreateInvitedUser
{
    public string Token { get; set; }
    public string Password { get; set; }
}
public class UpdateUser  
{
    public Guid Id { get; set; }
    public string Email { get; set; }
}
public class UserModel : UpdateUser
{
}
