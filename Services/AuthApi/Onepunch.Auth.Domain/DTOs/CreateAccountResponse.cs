namespace Onepunch.Auth.Domain.DTOs;

public class CreateAccountResponse
{
    public string? Email { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class CreateAccountErrorResponse
{
    public string ErrorCode { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
