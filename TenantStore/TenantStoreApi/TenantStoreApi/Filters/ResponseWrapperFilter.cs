using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace TenantStoreApi.Filters;

public class ResponseWrapperFilter : IResultFilter
{
    public void OnResultExecuting(ResultExecutingContext context)
    {
        // 1. Skip wrapping for non-API paths
        var path = context.HttpContext.Request.Path;
        if (path.StartsWithSegments("/swagger") ||
            path.StartsWithSegments("/favicon.ico"))
        {
            return;
        }

        // 2. Determine Status and Message
        int statusCode = context.HttpContext.Response.StatusCode;
        if (context.Result is ObjectResult obj && obj.StatusCode.HasValue)
        {
            statusCode = obj.StatusCode.Value;
        }

        string message = statusCode < 400 ? "Success" : "Error";

        // 3. Handle ObjectResult (Most common)
        if (context.Result is ObjectResult objectResult && objectResult.Value != null)
        {
            var dataType = objectResult.Value.GetType();

            // Prevent double-wrapping
            if (dataType.IsGenericType && dataType.GetGenericTypeDefinition() == typeof(ResponseModel<>))
            {
                return;
            }

            // --- THE MAGIC PART ---
            // Construct ResponseModel<ActualType> instead of ResponseModel<object>
            var genericType = typeof(ResponseModel<>).MakeGenericType(dataType);
            var wrappedResponse = Activator.CreateInstance(genericType);

            // Populate properties using dynamic to avoid slow reflection calls for setters
            dynamic dynamicResponse = wrappedResponse!;
            dynamicResponse.Status = statusCode;
            dynamicResponse.Message = message;
            dynamicResponse.Data = (dynamic)objectResult.Value;

            context.Result = new ObjectResult(wrappedResponse)
            {
                StatusCode = statusCode
            };
        }
        // 4. Handle Empty/Void Results (return NoContent())
        else if (context.Result is EmptyResult || (context.Result is ObjectResult nullObj && nullObj.Value == null))
        {
            // For empty results, we use <object> as there is no data
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
    }

    public void OnResultExecuted(ResultExecutedContext context) { }
}