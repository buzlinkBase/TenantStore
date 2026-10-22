namespace Onepunch.Common.Lib.DTO;

/// <summary>
/// The fixed code lists behind a company's classification fields. The codes are what is stored and
/// sent between services; the labels shown to people live in the front ends. Industry and
/// sub-industry are not here -- they are editable data in Tenant Service's lookup tables.
/// </summary>
public static class CompanyClassification
{
    public static readonly IReadOnlyList<string> BusinessTypes =
        ["sole-proprietorship", "partnership", "corporation", "cooperative", "non-profit", "other"];

    public static readonly IReadOnlyList<string> CompanySizes =
        ["1-10", "11-50", "51-200", "201-500", "501-1000", "1000+"];

    public static readonly IReadOnlyList<string> AcquisitionSources =
        ["facebook", "google", "referral", "event", "other"];

    public const string OtherSource = "other";

    public static bool IsBusinessType(string? code) => code != null && BusinessTypes.Contains(code);
    public static bool IsCompanySize(string? code) => code != null && CompanySizes.Contains(code);
    public static bool IsAcquisitionSource(string? code) => code != null && AcquisitionSources.Contains(code);
}
