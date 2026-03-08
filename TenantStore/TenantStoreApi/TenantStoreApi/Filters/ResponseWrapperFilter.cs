using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace TenantStoreApi.Filters;

public class ResponseWrapperFilter : IResultFilter
{
    public void OnResultExecuting(ResultExecutingContext context)
    {
        var path = context.HttpContext.Request.Path;

        if (path.StartsWithSegments("/swagger") ||
            path.StartsWithSegments("/favicon.ico") ||
            path.StartsWithSegments("/index.html") ||
            path.StartsWithSegments("/iclock"))
        {
            return;
        }

        // 2. Identify the Status and Message
        int statusCode = context.HttpContext.Response.StatusCode;
        // If the action set a specific status code (like 201 Created), use that instead
        if (context.Result is ObjectResult obj && obj.StatusCode.HasValue)
        {
            statusCode = obj.StatusCode.Value;
        }

        string message = statusCode < 400 ? "Success" : "Error";

        // 3. Handle ObjectResult (The most common path for APIs)
        if (context.Result is ObjectResult objectResult)
        {
            // IMPORTANT: Check if it's already wrapped to avoid double-wrapping
            if (objectResult.Value is not null)
            {
                var valueType = objectResult.Value.GetType();
                if (valueType.IsGenericType && valueType.GetGenericTypeDefinition() == typeof(ResponseModel<>))
                {
                    return;
                }
            }
            // USE THE CONCRETE CLASS instead of an anonymous object
            // This allows MessagePack to find the [MessagePackObject] attributes
            var wrappedResponse = new ResponseModel<object>
            {
                Status = statusCode,
                Message = message,
                Data = objectResult.Value
            };

            // Replace the result
            context.Result = new ObjectResult(wrappedResponse)
            {
                StatusCode = statusCode
            };
        }
        // 4. Handle EmptyResult (e.g. return NoContent())
        else if (context.Result is EmptyResult)
        {
            context.Result = new ObjectResult(new ResponseModel<object>
            {
                Status = statusCode,
                Message = message,
                Data = null
            })
            {
                StatusCode = statusCode
            };
        }
        // 5. Handle ContentResult (Plain text)
        else if (context.Result is ContentResult contentResult)
        {
            context.Result = new ObjectResult(new ResponseModel<object>
            {
                Status = statusCode,
                Message = message,
                Data = contentResult.Content
            })
            {
                StatusCode = statusCode
            };
        }
    }

    public void OnResultExecuted(ResultExecutedContext context) { }
}