using System.Collections.Concurrent;
using System.Text;

namespace DTR.Core;

public static class StringFormatter
{
    public static string FormatCode(this int count)
    {
        return count.ToString().PadLeft(6,'0');
    } 
}
public class EnumExtrator
{
    public static string[] GetNames<T>() => Enum.GetNames(typeof(T));
    public static string GetName<T>(T evalue) where T : Enum
    {
          return Enum.GetName(typeof(T), evalue);
    }

    public static TEnum GetValue<TEnum>(string name) where TEnum : struct, Enum
    {
        if (Enum.TryParse<TEnum>(name, out var result))
            return result;
        throw new ArgumentException($"'{name}' is not a valid name for enum {typeof(TEnum).Name}");
    }
}

public static class StringHelpers
{
    private static readonly ConcurrentDictionary<string, string> _cache = new();
    public static string AddSpacesBeforeCaps(string input)
    {
        if (string.IsNullOrEmpty(input) || input.Length == 1)
            return input;

        // Return cached result if available
        if (_cache.TryGetValue(input, out var cached))
            return cached;

        var result = new StringBuilder(input.Length + 5);
        char prev = input[0];
        result.Append(prev);

        for (int i = 1; i < input.Length; i++)
        {
            char current = input[i];
            if (current >= 'A' && current <= 'Z' && prev != ' ')
                result.Append(' ');

            result.Append(current);
            prev = current;
        }

        var final = result.ToString();
        _cache[input] = final;
        return final;
    }
}

