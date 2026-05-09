using System.Text.Json;

namespace Onepunch.Common.Lib;
public static class ObjectSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        // Good for web APIs or JS compatibility
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // Serialize object to string
    public static string Serialize(object obj) =>
        JsonSerializer.Serialize(obj, Options);

    // Deserialize string to object with null check
    public static T? Deserialize<T>(string message) where T : class =>
        string.IsNullOrWhiteSpace(message)
            ? null
            : JsonSerializer.Deserialize<T>(message, Options);
}