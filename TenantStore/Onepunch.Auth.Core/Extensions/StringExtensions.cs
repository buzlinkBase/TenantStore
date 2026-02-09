using Microsoft.AspNetCore.Identity;
using Serilog;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace OnePunch.Auth.Core;

public static class StringExtensions
{
    public static string EnumerateIdentityErrors(this IEnumerable<IdentityError> err)
    {
        if (err == null || !err.Any()) return "";
        return string.Join(Environment.NewLine,
                   err.Select(e => $"{e.Code}: {e.Description}"));
    }
}