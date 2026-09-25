using MessagePack;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Onepunch.Common.Lib;
using Onepunch.Common.Lib.Exceptions;

namespace OnePunch.Auth.Api.Exceptions;

public sealed class GlobalExceptionHandler(IHostEnvironment env) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var statusCode = exception switch
        {
            GuardException => StatusCodes.Status400BadRequest,
            UnauthorizedAccessException => StatusCodes.Status401Unauthorized,
            UnauthorizedException => StatusCodes.Status401Unauthorized,
            ForbiddenException => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status500InternalServerError,
        };

        httpContext.Response.StatusCode = statusCode;

        // 1. Create the detailed error object (ProblemDetails)
        var errorDetail = new ProblemDetails
        {
            Type = $"https://httpstatuses.com/{statusCode}",
            Title = GetTitleForStatus(statusCode),
            Status = statusCode,
            Detail = exception.Message,
            Instance = $"{httpContext.Request.Method} {httpContext.Request.Path}"
        };

        if (exception is GuardException { Code: not null } guardException)
        {
            errorDetail.Extensions.Add("code", guardException.Code);
        }

        if (env.IsDevelopment())
        {
            errorDetail.Extensions.Add("stackTrace", exception.StackTrace);
            if (exception.InnerException != null)
            {
                errorDetail.Extensions.Add("innerException", exception.InnerException.Message);
            }
        }

        // 2. Wrap it in your standard ResponseModel
        var response = new ResponseModel<ProblemDetails>
        {
            Status = statusCode,
            Message = "Error",
            Data = errorDetail
        };

        // 3. Determine Content-Type (Negotiate between MsgPack and JSON)
        var acceptHeader = httpContext.Request.Headers.Accept.ToString();

        if (acceptHeader.Contains("application/x-msgpack"))
        {
            httpContext.Response.ContentType = "application/x-msgpack";
            // Use the DefaultOptions you defined in Program.cs (which includes LZ4)
            await MessagePackSerializer.SerializeAsync(httpContext.Response.Body, response, MessagePackSerializer.DefaultOptions, cancellationToken);
        }
        else
        {
            httpContext.Response.ContentType = "application/json";
            await httpContext.Response.WriteAsJsonAsync(response, cancellationToken);
        }

        return true; // Mark as handled
    }

    private static string GetTitleForStatus(int statusCode) => statusCode switch
    {
        400 => "Bad Request",
        401 => "Unauthorized",
        403 => "Forbidden",
        _ => "An internal server error occurred"
    };
}