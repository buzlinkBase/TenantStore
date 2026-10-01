using Microsoft.AspNetCore.Mvc;
using Onepunch.Common.Lib.Subscriptions;
using TenantStoreApi.Core.Services.Subscriptions;

namespace TenantStoreApi.Controllers;

/// <summary>Maps subscription exceptions to the ProblemDetails the frontend switches on
/// (<see cref="SubscriptionProblemTypes"/>).</summary>
public static class SubscriptionResults
{
    public static async Task<IActionResult> Run(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (PlanLimitExceededException ex)
        {
            return Problem(StatusCodes.Status409Conflict, SubscriptionProblemTypes.PlanLimitReached, ex.Message,
                new() { ["violations"] = ex.Violations });
        }
        catch (PlanAllowanceException ex)
        {
            return Forbidden(ex.Allowance, ex.LimitKey);
        }
        catch (SubscriptionRuleException ex)
        {
            return Problem(StatusCodes.Status400BadRequest, null, ex.Message);
        }
        catch (KeyNotFoundException ex)
        {
            return Problem(StatusCodes.Status404NotFound, null, ex.Message);
        }
    }

    public static IActionResult Forbidden(PlanAllowance allowance, string limitKey) =>
        Problem(StatusCodes.Status403Forbidden, allowance.ProblemType ?? SubscriptionProblemTypes.PlanLimitReached, allowance.Error,
            new() { ["limit"] = limitKey, ["max"] = allowance.Max, ["used"] = allowance.Used });

    public static ObjectResult Problem(int status, string? type, string? detail, Dictionary<string, object?>? extensions = null)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Type = type,
            Title = status switch
            {
                StatusCodes.Status403Forbidden => "Not included in your plan",
                StatusCodes.Status404NotFound => "Not found",
                StatusCodes.Status409Conflict => "Over plan limits",
                _ => "Request refused",
            },
            Detail = detail,
        };
        foreach (var (key, value) in extensions ?? []) problem.Extensions[key] = value;
        return new ObjectResult(problem) { StatusCode = status };
    }
}
