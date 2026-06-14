namespace Onepunch.Auth.Domain.DTOs;

public class ErrorResponse
{
    public IEnumerable<string> Errors { get; set; } = [];
}

public class MessageErrorResponse
{
    public string Message { get; set; } = string.Empty;
}

public class UnauthorizedResponse
{
    public string ErrorMessage { get; set; } = string.Empty;
}
