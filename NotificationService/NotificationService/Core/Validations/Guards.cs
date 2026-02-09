namespace OnePunch.Notification.Core.Validations;

public static class Guard
{
    public static void ThrowIfNull<T>(T? value, string paramName)
    {
        if (value is null)
            throw new ArgumentNullException(paramName);

    }
 
    public static async Task ModelGuardAsync<T>(Func<T, Task<ValidationResponse>> validator, T model)
            where T : class, IEntity
    {
        if (validator == null) return;
        var result = await validator.Invoke(model);
        if (result is null) return;
        if (!result.Success)
        {
            // This will be caught by your GlobalExceptionHandler
            throw new ValidationFailureException(result.Message);
        }
    }
}