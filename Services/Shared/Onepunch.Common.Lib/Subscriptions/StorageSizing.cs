namespace Onepunch.Common.Lib.Subscriptions;

public enum StorageLevel
{
    Normal,
    /// <summary>80% or more of the limit is used.</summary>
    Warning,
    /// <summary>95% or more of the limit is used.</summary>
    Critical,
}

/// <summary>
/// Storage arithmetic shared by every service: the limit key is in MB, so a 50 GB block is
/// 51,200 MB. Thresholds follow the product rules (80% warning, 95% critical).
/// </summary>
public static class StorageSizing
{
    public const int MbPerGb = 1024;
    public const int WarningPercent = 80;
    public const int CriticalPercent = 95;

    public static int GbToMb(int gb) => checked(gb * MbPerGb);

    /// <summary>Whole percent used, uncapped; null when there is no finite limit to measure against.</summary>
    public static int? PercentUsed(long usedMb, int? limitMb)
    {
        if (limitMb is null) return null;
        if (limitMb <= 0) return usedMb > 0 ? 100 : 0;
        return (int)(usedMb * 100 / limitMb.Value);
    }

    public static StorageLevel LevelOf(long usedMb, int? limitMb) => PercentUsed(usedMb, limitMb) switch
    {
        null => StorageLevel.Normal,
        >= CriticalPercent => StorageLevel.Critical,
        >= WarningPercent => StorageLevel.Warning,
        _ => StorageLevel.Normal,
    };
}
