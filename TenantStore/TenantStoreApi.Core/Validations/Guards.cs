using BuzlinkRepository; 

namespace TenantStoreApi.Core;

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
            throw new ValidationFailureException(message);
    }

    public static void EnsureFalse(bool condition, string message)
    {
        if (condition)
            throw new ValidationFailureException(message);
    }

    public static void ThrowIfError(ValidationResponse? result)
    {
        if (result is null)
            throw new ValidationFailureException("Validation result cannot be null.");

        if (!result.Success)
            throw new ValidationFailureException(result.Message);
    }

    public static async Task ModelGuardAsync<T>(
        Func<T, Task<ValidationResponse>> validator,
        T model) where T : class, IEntity
    {
        if (validator is null)
            throw new ArgumentNullException(nameof(validator));

        await validator.Invoke(model);
    }
}