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
    public static void ThrowIfEmpty(Guid value, string paramName)
    {
        if (Guid.Empty == value)
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
    public static async Task ModelGuardAsync<T>(Func<T, CancellationToken, Task<EvaluationResult>> validator, IEnumerable<T> models, CancellationToken token)
        where T : class, IEntity
    {
        foreach (var model in models)
        {
            await ModelGuardAsync(validator, model, token);
        }
    }

    //public static async Task EmailTokenGuard(string encryptedToken)
    //{
    //    var token = Encoding.UTF8.GetString(TokenEncodingHelper.FromBase64Url(encryptedToken));
    //    var userToken = ObjectSerializer.Deserialize<EmailTokenInfo>(token);
    //    var emailInfoDb = await FindToken(payload.Token);
    //    if (emailInfoDb == null) throw new Exception("Unverified token");
    //    if (emailInfoDb.TenantId != userToken.TenantId) throw new Exception("Invalid payload");
    //    if (emailInfoDb.Email != userToken.Email) throw new Exception("Invalid payload");
    //    if (IsTokenExpired(emailInfoDb) || emailInfoDb.IsUsed) throw new Exception("Token expired");
    //}
}