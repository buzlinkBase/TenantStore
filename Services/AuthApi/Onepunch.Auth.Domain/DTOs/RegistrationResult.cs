namespace Onepunch.Auth.Domain;

public class RegistrationResult
{
    public bool Success { get; set; }
    public string Message { get; set; }
    public string ErrorCode { get; set; }
    public string? Email { get; set; }
    public string? Name { get; set; }
}