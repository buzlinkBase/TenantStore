using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Onepunch.Common.Lib.Security;

namespace Onepunch.Common.Web.Authorization;

/// <summary>
/// Declarative per-action permission gate for every API behind AuthApi's token: checks the caller's
/// "permission" claims (see <see cref="PermissionClaims"/>) against one or more permission codes,
/// any-of, and answers 403 with the codes it needed if none match.
///
/// Usage: [HttpGet] [RequirePermission("Organization Setup:View")] for an HRMS action, or
/// [RequirePermission("procurement:Purchase Order:Approve")] for another system's.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RequirePermissionAttribute(params string[] codes) : Attribute, IAsyncActionFilter
{
    public string[] Codes => codes;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!context.HttpContext.User.HasAnyPermission(codes))
        {
            context.Result = new ObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "Forbidden",
                Detail = $"Requires one of: {string.Join(", ", codes)}",
            })
            {
                StatusCode = StatusCodes.Status403Forbidden,
            };
            return;
        }

        await next();
    }
}
