using Adms.api.Exceptions;
using BuzlinkRepository;

namespace Adms.api.Validations;

public static class Guard
{
    public static T ThrowIfNull<T>(T? value, string paramName)
    {
        if (value is null)
            throw new ArgumentNullException(paramName);

        return value;
    }

    public static async Task ModelGuardAsync<T>(Func<T, Task<ValidationResult>> validator, T model)
            where T : class, IEntity
    {
        if (validator == null) return;
        var result = await validator.Invoke(model);
        if (result is null) return;
        if (!result.Success)
        {
            // This will be caught by your GlobalExceptionHandler
            throw new ValidationException(result.Message);
        }
    }
}