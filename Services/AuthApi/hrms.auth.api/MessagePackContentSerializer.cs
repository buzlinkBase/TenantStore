using MessagePack;
using Refit;
using System.Net.Http.Headers;
using System.Reflection;

namespace OnePunch.Auth.Api;

public class MessagePackContentSerializer : IHttpContentSerializer
{
    private readonly MessagePackSerializerOptions _options;

    public MessagePackContentSerializer(MessagePackSerializerOptions? options = null)
    {
        // Fallback to DefaultOptions which should be set in Program.cs
        _options = options ?? MessagePackSerializer.DefaultOptions;
    }

    /// <summary>
    /// Serializes the request body (Client -> Server)
    /// </summary>
    public HttpContent ToHttpContent<T>(T item)
    {
        // MessagePack v3 returns ReadOnlyMemory<byte>
        var bytes = MessagePackSerializer.Serialize(item, _options);
        var content = new ByteArrayContent(bytes.ToArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-msgpack");
        return content;
    }

    /// <summary>
    /// Deserializes the response body (Server -> Client)
    /// </summary>
    public async Task<T?> FromHttpContentAsync<T>(HttpContent content, CancellationToken cancellationToken = default)
    {
        var mediaType = content.Headers.ContentType?.MediaType;
        if (mediaType != "application/x-msgpack")
        {
            // If we got JSON back, read it as a string so we can see the error
            var jsonError = await content.ReadAsStringAsync();
            throw new Exception($"Expected MsgPack but got {mediaType}. Data: {jsonError}");
        }
        // GUARD: If the server returns 500/404, it often sends "application/json" or "text/plain"
        // Attempting to parse JSON as MessagePack causes the 'Unexpected code 123' error.
        if (mediaType != "application/x-msgpack")
        {
            var rawContent = await content.ReadAsStringAsync(cancellationToken);
            throw new Exception($"Expected MessagePack (application/x-msgpack) but received {mediaType}. Raw Content: {rawContent}");
        }
        // v3.x efficient stream reading
        using var stream = await content.ReadAsStreamAsync(cancellationToken);

        // Use the Async variant for better performance in .NET 9
        return await MessagePackSerializer.DeserializeAsync<T>(stream, _options, cancellationToken);
    }

    public string? GetFieldNameForProperty(PropertyInfo propertyInfo)
    {
        return propertyInfo.Name;
    }
}