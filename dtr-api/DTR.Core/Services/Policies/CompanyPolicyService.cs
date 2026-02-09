namespace DTR.Core;

public class CompanyPolicyRule
{
    public OvertimeInclusionPolicy OTInclusionPolicy { get; set; }
    public OvertimeEligibilityRule OTEligibility { get; set; }
    public bool IsHalfDayLateOn { get; set; }
    public bool IsWholeDayLateOn { get; set; }
    public double HalfDayLateThresholdMinutes { get; set; }
    public double WholeDayLateThresholdMinutes { get; set; }
    public double NightDiffThreshold { get; set; }
    public ManualEntryLimitEnum AttFillLimit { get; set; }
    public HolidayTimeBasis HolidayTimeBasis { get; set; }
    public bool IsHolPlusReg { get; set; }
    public HolidayCreditMode HolidayColumnPresentation { get; set; }
}
public class CompanyPolicyService
{
    public CompanyPolicyRule Transform(Dictionary<string, GeneralSettingModel> data)
    {
        CompanyPolicyRule policy = new CompanyPolicyRule();
        SetOTeligibilityRule(policy, data);
        SetOTInclusionRule(policy, data);
        SetHalfDayLateRule(policy, data);
        SetWholeDayLateRule(policy, data);
        SetNightDiffThreshold(policy, data);
        AutoFillLimit(policy, data);
        SetHolidayComputationBasis(policy, data);
        SetIsHolidayAddedInHolidayColumn(policy, data);
        SetHolidayColumnPresentation(policy, data);
        return policy;
    }

    private void SetOTeligibilityRule(CompanyPolicyRule policy, Dictionary<string, GeneralSettingModel> data)
    {
        policy.OTEligibility = OvertimeEligibilityRule.RequireFullRegularHours;
        if (data.TryGetValue(SettingKey.OTEligibility.ToString(), out GeneralSettingModel? val1))
        {
            var settingvalue = GeneralSettingsUtil.ParseEnum(val1.Value, OvertimeEligibilityRule.RequireFullRegularHours);
            policy.OTEligibility = settingvalue;
        }
    }
    private void SetOTInclusionRule(CompanyPolicyRule policy, Dictionary<string, GeneralSettingModel> data)
    {
        policy.OTInclusionPolicy = OvertimeInclusionPolicy.UsePostShiftWork;
        if (data.TryGetValue(SettingKey.OTInclusion.ToString(), out GeneralSettingModel? OTRule))
        {
            var settingvalue = GeneralSettingsUtil.ParseEnum(OTRule.Value, OvertimeInclusionPolicy.UsePostShiftWork);
            policy.OTInclusionPolicy = settingvalue;
        }
    }
    private void SetHalfDayLateRule(CompanyPolicyRule policy, Dictionary<string, GeneralSettingModel> data)
    {
        policy.IsHalfDayLateOn = false;
        policy.HalfDayLateThresholdMinutes = 0;
        if (data.TryGetValue(SettingKey.IsHalfDayLateOn.ToString(), out GeneralSettingModel? IsLateOn))
        {
            var settingvalue = GeneralSettingsUtil.ParseBool(IsLateOn.Value, false);
            policy.IsHalfDayLateOn = settingvalue;
        }

        if (data.TryGetValue(SettingKey.HalfDayLateThresholdMinutes.ToString(), out GeneralSettingModel? minutes))
        {
            var val = GeneralSettingsUtil.ParseDouble(minutes.Value, 0);
            policy.HalfDayLateThresholdMinutes = val;
        }
    }
    private void SetWholeDayLateRule(CompanyPolicyRule policy, Dictionary<string, GeneralSettingModel> data)
    {
        policy.IsWholeDayLateOn = false;
        policy.WholeDayLateThresholdMinutes = 0;
        if (data.TryGetValue(SettingKey.IsWholeDayLateOn.ToString(), out GeneralSettingModel? IsLateOn))
        {
            var settingvalue = GeneralSettingsUtil.ParseBool(IsLateOn.Value, false);
            policy.IsWholeDayLateOn = settingvalue;
        }

        if (data.TryGetValue(SettingKey.WholeDayLateThresholdMinutes.ToString(), out GeneralSettingModel? minutes))
        {
            var val = GeneralSettingsUtil.ParseDouble(minutes.Value, 0);
            policy.WholeDayLateThresholdMinutes = val;
        }
    }
    private void SetNightDiffThreshold(CompanyPolicyRule policy, Dictionary<string, GeneralSettingModel> data)
    {
        policy.NightDiffThreshold = 0;
        if (data.TryGetValue(SettingKey.NightDiffThreshold.ToString(), out GeneralSettingModel? threshold))
        {
            var settingvalue = GeneralSettingsUtil.ParseDouble(threshold.Value, 0);
            policy.NightDiffThreshold = settingvalue;
        }
    }
    private void AutoFillLimit(CompanyPolicyRule policy, Dictionary<string, GeneralSettingModel> data)
    {
        policy.AttFillLimit = ManualEntryLimitEnum.NOLIMIT;
        if (data.TryGetValue(SettingKey.AttFillLimit.ToString(), out GeneralSettingModel? limit))
        {
            var settingvalue = GeneralSettingsUtil.ParseEnum<ManualEntryLimitEnum>(limit.Value, 0);
            policy.AttFillLimit = settingvalue;
        }
    }

    private void SetHolidayComputationBasis(CompanyPolicyRule policy, Dictionary<string, GeneralSettingModel> data)
    {
        policy.HolidayTimeBasis = HolidayTimeBasis.BasedOnTimeInDayType;
        if (data.TryGetValue(SettingKey.HolidayTimeBasis.ToString(), out GeneralSettingModel? holbasis))
        {
            var settingvalue = GeneralSettingsUtil.ParseEnum(holbasis.Value, HolidayTimeBasis.BasedOnTimeInDayType);
            policy.HolidayTimeBasis = settingvalue;
        }
    }
    private void SetIsHolidayAddedInHolidayColumn(CompanyPolicyRule policy, Dictionary<string, GeneralSettingModel> data)
    {
        policy.IsHolPlusReg = true;
        if (data.TryGetValue(SettingKey.IsHolPlusReg.ToString(), out GeneralSettingModel? IsHolPlusReg))
        {
            var settingvalue = GeneralSettingsUtil.ParseBool(IsHolPlusReg.Value, true);
            policy.IsHolPlusReg = settingvalue;
        }
    }

    private void SetHolidayColumnPresentation(CompanyPolicyRule policy, Dictionary<string, GeneralSettingModel> data)
    {
        policy.HolidayColumnPresentation = HolidayCreditMode.AutoCredit;
        if (data.TryGetValue(SettingKey.HolidayColumnPresentation.ToString(), out GeneralSettingModel? hol))
        {
            var settingvalue = GeneralSettingsUtil.ParseEnum(hol.Value, HolidayCreditMode.AutoCredit);
            policy.HolidayColumnPresentation = settingvalue;
        }
    }

}
