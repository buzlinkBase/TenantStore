//using Microsoft.Extensions.Options;
//using Onepunch.Common.Lib;

//namespace OnePunch.Auth.Api.Middlewares;

//public class ApiKeyMiddleware
//{
//    private readonly RequestDelegate _next;
//    private readonly ApiKeySetting _config;
//    private const string API_KEY_HEADER = "X-Api-Key";
//    private readonly string _apiKey;

//    public ApiKeyMiddleware(RequestDelegate next, IOptions<ApiKeySetting> config)
//    {
//        _next = next;
//        _config = config.Value;
//        _apiKey = _config.ApiKey;
//    }

//    public async Task InvokeAsync(HttpContext context)
//    {
//        // Example: skip API key check for health or public routes
//        var path = context.Request.Path.Value?.ToLower();

//        if (path != null && (
//            path.StartsWith("/api/v1/user/confirm-email") ||
//            path.StartsWith("/public") ||
//            path.StartsWith("/swagger")))
//        {
//            await _next(context);
//            return;
//        }

//        // Otherwise enforce API key
//        if (!context.Request.Headers.TryGetValue(API_KEY_HEADER, out var extractedApiKey) ||
//            !_apiKey.Equals(extractedApiKey))
//        {
//            context.Response.StatusCode = 401; // Unauthorized
//            await context.Response.WriteAsync("API Key is missing or invalid.");
//            return;
//        }

//        await _next(context);
//    }
//    //public async Task InvokeAsync(HttpContext context)
//    //{
//    //    if (!context.Request.Headers.TryGetValue(API_KEY_HEADER, out var extractedKey) ||
//    //        !_apiKey.Equals(extractedKey))
//    //    {
//    //        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
//    //        await context.Response.WriteAsync("Unauthorized: Invalid API Key");
//    //        return;
//    //    }

//    //    await _next(context);
//    //}
//}
