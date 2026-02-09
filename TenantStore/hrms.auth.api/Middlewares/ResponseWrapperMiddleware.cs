using System.Text.Json;

namespace OnePunch.Auth.Api.Middlewares;
public class ResponseWrapperMiddleware
{
    private readonly RequestDelegate _next;

    public ResponseWrapperMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task Invoke(HttpContext context)
    {
        // 1. Skip wrapping for Swagger or system paths
        if (context.Request.Path.StartsWithSegments("/swagger") ||
            context.Request.Path.StartsWithSegments("/favicon.ico"))
        {
            await _next(context);
            return;
        }

        var originalBodyStream = context.Response.Body;
        using var memoryStream = new MemoryStream();
        context.Response.Body = memoryStream;

        try
        {
            // 2. Execute the rest of the pipeline
            await _next(context);

            // 3. If the response has already started or it's an error (400+), 
            // we do NOT wrap. We just copy the stream back and exit.
            if (context.Response.HasStarted || context.Response.StatusCode >= 400)
            {
                memoryStream.Seek(0, SeekOrigin.Begin);
                await memoryStream.CopyToAsync(originalBodyStream);
                return;
            }

            // 4. Handle Success Wrapping
            memoryStream.Seek(0, SeekOrigin.Begin);
            using var reader = new StreamReader(memoryStream, leaveOpen: true);
            var responseBody = await reader.ReadToEndAsync();

            // Restore the original stream pointer
            context.Response.Body = originalBodyStream;

            object? dataContent = null;
            if (!string.IsNullOrWhiteSpace(responseBody))
            {
                try
                {
                    // Use JsonDocument to parse raw JSON safely without Type mapping
                    dataContent = JsonDocument.Parse(responseBody).RootElement;
                }
                catch (JsonException)
                {
                    // If it's not valid JSON, treat it as a plain string
                    dataContent = responseBody;
                }
            }

            var wrappedResponse = new
            {
                Status = context.Response.StatusCode,
                Message = "Success",
                Data = dataContent
            };

            context.Response.ContentType = "application/json";

            // Use built-in JSON writing (handles formatting and performance)
            await context.Response.WriteAsJsonAsync(wrappedResponse);
        }
        catch (Exception)
        {
            // Restore stream and re-throw so GlobalExceptionHandler can catch it
            context.Response.Body = originalBodyStream;
            throw;
        }
    }
}