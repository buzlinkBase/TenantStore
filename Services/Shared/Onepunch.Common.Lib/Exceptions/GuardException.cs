
using FluentValidation.Results;

namespace Onepunch.Common.Lib.Exceptions;

public class GuardException : Exception
{
    public GuardException(string message) : base(message)
    {
    }
    public GuardException(string message, Exception exception) : base(message, exception)
    {
    }
}

public record EvaluationResult(string Message = "", bool Success = false)
{
    public static EvaluationResult OK => new EvaluationResult("", true);
    public static EvaluationResult Fail(string message) => new EvaluationResult(message);
    public static EvaluationResult Fail(List<ValidationFailure> failures)
    {
        var message = string.Join(Environment.NewLine,
             failures.Select(f => $"{f.PropertyName}: {f.ErrorMessage}"));

        return new EvaluationResult(message);
    }
    public static EvaluationResult Check(ValidationResult? result)
    {
        if (result == null || result.IsValid) return OK;
        return Fail(result.Errors);
    }
}
