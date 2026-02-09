using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace TenantStoreApi.Attributes;

public class ApiKeyAuthAttribute : Attribute, IAsyncActionFilter
{
    private const string API_KEY_HEADER = "X-Api-Key";

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var config = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        var apiKey = config["ApiKeySettings:ApiKey"];

        if (!context.HttpContext.Request.Headers.TryGetValue(API_KEY_HEADER, out var extractedKey) ||
            !apiKey.Equals(extractedKey))
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        await next();
    }
}