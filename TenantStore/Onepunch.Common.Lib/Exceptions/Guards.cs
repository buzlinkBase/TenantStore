using BuzlinkRepository;

namespace Onepunch.Common.Lib.Exceptions;

public static class Guard
{
    public static void ThrowIfNull<T>(T? value, string paramName) where T : class
    {
        if (value is null)
            throw new ArgumentNullException(paramName ?? nameof(value));
    }
    public static void ThrowIfEmpty(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentNullException(paramName ?? nameof(value));
    }


    public static void EnsureTrue(bool condition, string message)
    {
        if (!condition)
            throw new GuardException(message);
    }

    public static void EnsureFalse(bool condition, string message)
    {
        if (condition)
            throw new GuardException(message);
    }

    public static void ThrowIfError(EvaluationResult? result)
    {
        if (result is null)
            throw new GuardException("Validation result cannot be null.");

        if (!result.Success)
            throw new GuardException(result.Message);
    }

    public static async Task ModelGuardAsync<T>(Func<T, CancellationToken, Task<EvaluationResult>> validator, T model, CancellationToken token)
            where T : class, IEntity
    {
        if (validator == null) return;
        var result = await validator.Invoke(model, token);
        if (result is null) return;
        if (!result.Success)
        {
            throw new GuardException(result.Message);
        }
    }
}