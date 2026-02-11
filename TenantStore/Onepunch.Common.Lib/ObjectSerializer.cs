using Newtonsoft.Json;

namespace Onepunch.Common.Lib;

public class ObjectSerializer
{
    public static string Serialized(object obj) => JsonConvert.SerializeObject(obj);
    public static T Deserialized<T>(string message) => JsonConvert.DeserializeObject<T>(message);
}
