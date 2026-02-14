namespace DTR.Models;

public class EnumParserConfig
{
    public static TEnum SafeParseEnum<TEnum>(string value, TEnum defaultValue) where TEnum : struct, Enum
    {
        return Enum.TryParse<TEnum>(value, out var result) && Enum.IsDefined(typeof(TEnum), result)
            ? result
            : defaultValue;
    }
}