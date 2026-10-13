namespace Onepunch.Common.Lib.Security;

// The systems that share the TenantApi authorization store. A permission code carries its product
// as a prefix ("procurement:Purchase Order:Approve") because the JWT holds only the code strings;
// HRMS codes stay unprefixed ("Payroll Run:Approve") so everything issued before products existed
// keeps working.
public static class PermissionProducts
{
    public const string Hrms = "hrms";
    public const string Procurement = "procurement";
    public const string Accounting = "accounting";
    public const string Billing = "billing";
    public const string ClientPortal = "clientportal";

    public static string BuildCode(string product, string feature, string action)
    {
        return product == Hrms ? $"{feature}:{action}" : $"{product}:{feature}:{action}";
    }

    // An unprefixed code has exactly one colon (Feature:Action); a prefixed one has two or more.
    // No HRMS feature name contains a colon, which BuildCode relies on as well.
    public static string ProductOf(string code)
    {
        var first = code.IndexOf(':');
        var second = first < 0 ? -1 : code.IndexOf(':', first + 1);
        return second < 0 ? Hrms : code[..first];
    }
}
