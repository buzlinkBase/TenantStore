using FluentValidation.Results;

namespace OnePunch.Auth.Core.Exceptions;

public class ValidationFailureException : Exception
{
    public ValidationFailureException(string message) : base(message)
    {
    }
    public ValidationFailureException(string message, Exception exception) : base(message, exception)
    {
    }
} 

public record ValidationResponse(string Message = "", bool Success = false)
{
    public static ValidationResponse OK => new ValidationResponse("", true);
    public static ValidationResponse Fail(string message) => new ValidationResponse(message);
    public static ValidationResponse Fail(List<ValidationFailure> failures)
    {
        var message = string.Join(Environment.NewLine,
             failures.Select(f => $"{f.PropertyName}: {f.ErrorMessage}"));

        return new ValidationResponse(message);
    }
    public static ValidationResponse Check(FluentValidation.Results.ValidationResult? result)
    {
        if (result == null || result.IsValid) return OK;
        return Fail(result.Errors);
    }
} 