namespace TenantStoreApi.Core;

public static class StringFormatter
{
    public static string FormatCode(this int count)
    {
        return count.ToString().PadLeft(6, '0');
    }
}