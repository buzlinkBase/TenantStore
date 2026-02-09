namespace DTR.Core;

public static class StringExtensions
{
    public static string FullName(this Employee? employee)
    {
        if (employee == null) return "";
        string full_name = $"{employee.LastName}, {employee.FirstName} {employee.Suffix} {employee.MiddleName}";
        if (full_name.Trim().StartsWith(","))
        {
            full_name = full_name.Substring(1, full_name.Length - 1);
        }
        else if (full_name.Trim().StartsWith("-, "))
        {
            full_name = full_name.Substring(3, full_name.Length - 3);
        }
        return full_name;
    }

    public static string TrimString (this string str, int length)
    {
        if (string.IsNullOrEmpty(str)) return string.Empty;
        return str.Substring(0, Math.Min(str.Length, length));

        //if (string.IsNullOrEmpty(str)) str = string.Empty;

        //if (str.Length > length)
        //    return str.Substring(0, length); // Trim excess
        //else
        //    return str.PadRight(length);     // Pad with spaces


    }
}


