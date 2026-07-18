namespace OnePunch.Auth.Api.Extensions;
public static class HttpContextExtensions
{
    /// <summary>
    /// Extracts strictly the host/domain name (e.g., "myfrontend.com" or "localhost") 
    /// from the Origin or Referer HTTP headers.
    /// </summary>
    public static string GetFrontendHost(this HttpContext context)
    {
        if (context == null) return string.Empty;

        // 1. Try Origin header first (Best for CORS/API requests)
        if (context.Request.Headers.TryGetValue("Origin", out var originValue) && !string.IsNullOrEmpty(originValue))
        {
            if (Uri.TryCreate(originValue.ToString(), UriKind.Absolute, out var originUri))
            {
                return originUri.Host;
            }
        }
        // 2. Fallback to Referer header
        if (context.Request.Headers.TryGetValue("Referer", out var refererValue) && !string.IsNullOrEmpty(refererValue))
        {
            if (Uri.TryCreate(refererValue.ToString(), UriKind.Absolute, out var refererUri))
            {
                return refererUri.Host;
            }
        }
        return string.Empty;
    }
}