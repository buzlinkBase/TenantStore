using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Onepunch.Common.Lib;

namespace OnePunch.Auth.Api.Filters;

public class ResponseWrapperFilter : IResultFilter
{
    public void OnResultExecuting(ResultExecutingContext context)
    {
        // 1. Skip wrapping for non-API paths/Swagger
        var path = context.HttpContext.Request.Path;
        if (path.StartsWithSegments("/swagger") ||
             path.StartsWithSegments("/favicon.ico") ||
             path.StartsWithSegments("/index.html"))
        {
            return;
        }

        // 2. Determine Status
        int statusCode = context.HttpContext.Response.StatusCode;
        if (context.Result is ObjectResult obj && obj.StatusCode.HasValue)
        {
            statusCode = obj.StatusCode.Value;
        }

        string message = statusCode < 400 ? "Success" : "Error";

        // 3. Handle ObjectResult
        if (context.Result is ObjectResult objectResult && objectResult.Value != null)
        {
            var dataType = objectResult.Value.GetType();

            // Prevent double-wrapping
            if (dataType.IsGenericType && dataType.GetGenericTypeDefinition() == typeof(ResponseModel<>))
            {
                return;
            }

            // Construct ResponseModel<T> at runtime
            var genericType = typeof(ResponseModel<>).MakeGenericType(dataType);
            var wrappedResponse = Activator.CreateInstance(genericType);

            dynamic dynamicResponse = wrappedResponse!;
            dynamicResponse.Status = statusCode;
            dynamicResponse.Message = message;
            dynamicResponse.Data = (dynamic)objectResult.Value;

            // Create the new result
            var newResult = new ObjectResult(wrappedResponse)
            {
                StatusCode = statusCode,
                DeclaredType = genericType
            };

            // --- THE FIX: CONTENT NEGOTIATION ---
            var acceptHeader = context.HttpContext.Request.Headers.Accept.ToString();

            if (acceptHeader.Contains("application/x-msgpack"))
            {
                // If client asked for MsgPack, force it
                newResult.ContentTypes.Add("application/x-msgpack");
            }
            else
            {
                // Default to JSON for Swagger/Postman/Browsers
                newResult.ContentTypes.Add("application/json");
            }

            context.Result = newResult;
        }
        // 4. Handle Empty Results
        else if (context.Result is EmptyResult || (context.Result is ObjectResult nullObj && nullObj.Value == null))
        {
            var emptyResult = new ObjectResult(new ResponseModel<object>
            {
                Status = statusCode,
                Message = message,
                Data = null
            })
            {
                StatusCode = statusCode,
                DeclaredType = typeof(ResponseModel<object>)
            };

            // Apply same content-type logic to empty results
            if (context.HttpContext.Request.Headers.Accept.ToString().Contains("application/x-msgpack"))
                emptyResult.ContentTypes.Add("application/x-msgpack");
            else
                emptyResult.ContentTypes.Add("application/json");

            context.Result = emptyResult;
        }
    }

    public void OnResultExecuted(ResultExecutedContext context) { }
}