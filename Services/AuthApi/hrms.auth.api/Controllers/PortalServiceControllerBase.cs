using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using Onepunch.Auth.Core.Portal;
using Onepunch.Common.Lib;
using Onepunch.Common.Lib.Exceptions;

namespace OnePunch.Auth.Api.Controllers;

/// <summary>
/// Base of every endpoint only the Client Portal's server may call (the browser never talks to AuthApi
/// for the portal): each action requires the portal's service key, and nothing else gets an answer.
/// </summary>
public abstract class PortalServiceControllerBase : ControllerBase, IActionFilter
{
    private readonly PortalAuthSettings _settings;

    protected PortalServiceControllerBase(IOptions<PortalAuthSettings> settings)
    {
        _settings = settings.Value;
    }

    /// <summary>Runs before every action: the caller must be the portal's server.</summary>
    [NonAction]
    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (!PortalTokenValidation.KeyMatches(Request.Headers[PortalAuthSettings.KeyHeader].ToString(), _settings.ServiceKey))
            throw new UnauthorizedException("Unauthorized.");
    }

    [NonAction]
    public void OnActionExecuted(ActionExecutedContext context)
    {
    }
}
