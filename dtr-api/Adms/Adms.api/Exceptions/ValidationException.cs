using FluentValidation.Results;

namespace Adms.api.Exceptions;
public record ValidationResult(string Message = "", bool Success = false)
{
    public static ValidationResult OK => new ValidationResult("", true);
    public static ValidationResult Fail(string message) => new ValidationResult(message);
    public static ValidationResult Fail(List<ValidationFailure> failures)
    {
        var message = string.Join(Environment.NewLine,
             failures.Select(f => $"{f.PropertyName}: {f.ErrorMessage}"));

        return new ValidationResult(message);
    }
}

public class ValidationException : Exception
{
    public ValidationException(string message) : base(message)
    {
    }
    public ValidationException(string message, Exception exception) : base(message, exception)
    {
    }
}