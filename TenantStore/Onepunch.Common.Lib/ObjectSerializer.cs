using Newtonsoft.Json;

namespace Onepunch.Common.Lib;

public class ObjectSerializer
{
    public static string Serialized(object obj) => JsonConvert.SerializeObject(obj);
    public static T DeSerialized<T>(string message) => JsonConvert.DeserializeObject<T>(message);
}
