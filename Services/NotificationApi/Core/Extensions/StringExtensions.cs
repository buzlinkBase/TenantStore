using Microsoft.AspNetCore.Identity;

namespace OnePunch.Notification.Core.Extensions;

public static class StringExtensions
{
    public static string EnumerateIdentityErrors(this IEnumerable<IdentityError> err)
    {
        if (err == null || !err.Any()) return "";
        return string.Join(Environment.NewLine,
                   err.Select(e => $"{e.Code}: {e.Description}"));
    }
}