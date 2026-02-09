using MessagePack;
using Refit;
using System.Net.Http.Headers;
using System.Reflection;

namespace DTR.Api;

public class MessagePackContentSerializer : IHttpContentSerializer
{
    private readonly MessagePackSerializerOptions _options;

    public MessagePackContentSerializer(MessagePackSerializerOptions options)
        => _options = options;

    // Latest Refit uses 'ToHttpContent' for serialization (Sending data)
    public HttpContent ToHttpContent<T>(T item)
    {
        var bytes = MessagePackSerializer.Serialize(item, _options);
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-msgpack");
        return content;
    }

    public async Task<T?> FromHttpContentAsync<T>(HttpContent content, CancellationToken cancellationToken = default)
    {
        using var stream = await content.ReadAsStreamAsync(cancellationToken);
        return await MessagePackSerializer.DeserializeAsync<T>(stream, _options, cancellationToken);
    }

    public string? GetFieldNameForProperty(System.Reflection.PropertyInfo propertyInfo, Microsoft.AspNetCore.Mvc.JsonOptions? jsonOptions)
        => propertyInfo.Name;

    public string? GetFieldNameForProperty(PropertyInfo propertyInfo)
    {
        // Check if the property has a [Key] attribute from MessagePack
        var keyAttr = propertyInfo.GetCustomAttribute<MessagePack.KeyAttribute>();
        if (keyAttr != null)
        {
            // If the key is an int index, return it as string
            if (keyAttr.IntKey >= 0)
                return keyAttr.IntKey.ToString();

            // Otherwise return the string key
            if (!string.IsNullOrEmpty(keyAttr.StringKey))
                return keyAttr.StringKey;
        }

        // Fallback: just use the property name
        return propertyInfo.Name;
    }

}