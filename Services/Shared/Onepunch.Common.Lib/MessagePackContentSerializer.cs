using MessagePack;
using Refit;
using System.Net.Http.Headers;
using System.Reflection;

namespace Onepunch.Common.Lib;

public class MessagePackContentSerializer : IHttpContentSerializer
{
    private readonly MessagePackSerializerOptions _options;

    public MessagePackContentSerializer(MessagePackSerializerOptions options = null)
    {
        _options = options ?? MessagePackSerializerOptions.Standard;
    }

    // This is used for creating the request body (Client -> Server)
    public HttpContent ToHttpContent<T>(T item)
    {
        var bytes = MessagePackSerializer.Serialize(item, _options);
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-msgpack");
        return content;
    }

    // This is used for reading the response body (Server -> Client)
    public async Task<T?> DeserializeAsync<T>(HttpContent content, CancellationToken cancellationToken = default)
    {
        if (content.Headers.ContentType?.MediaType != "application/x-msgpack")
        {
            var raw = await content.ReadAsStringAsync();
            throw new Exception($"Expected MsgPack but got {content.Headers.ContentType?.MediaType}. Body: {raw}");
        }
        var stream = await content.ReadAsStreamAsync(cancellationToken);
        return await MessagePackSerializer.DeserializeAsync<T>(stream, _options, cancellationToken);
    }

    // This is an older/alternative signature for deserialization used by some Refit versions
    // We can simply bridge it to our DeserializeAsync method
    public Task<T?> FromHttpContentAsync<T>(HttpContent content, CancellationToken cancellationToken = default)
    {
        return DeserializeAsync<T>(content, cancellationToken);
    }

    // This is used by Refit to determine the property name for form data/multipart
    // MessagePack is usually binary-only, so returning null or the standard name is fine
    public string? GetFieldNameForProperty(PropertyInfo propertyInfo)
    {
        // By default, just return the property name
        return propertyInfo.Name;
    }
}