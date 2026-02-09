namespace DTR.Core;

public class EmployeePolicyRule
{
    public EmployeePolicyKey Key { get; set; }
    //public OvertimeEligibilityRule? OvertimeEligibilityRule { get; set; }
    //public OvertimeInclusionPolicy? OvertimeInclusionPolicy { get; set; }
}

public class EmployeePolicyService
{
    public Dictionary<EmployeePolicyKey, EmployeePolicyRule> Transform(Dictionary<SettingGroupKey, Dictionary<string, GeneralSettingModel>> data)
    {

        if (data == null) return new Dictionary<EmployeePolicyKey, EmployeePolicyRule>();
        var result = data
            .GroupBy(x => x.Key)
            .Select(x => new EmployeePolicyRule()
            {
                Key = new EmployeePolicyKey(x.Key.IdentityId),
                //OvertimeEligibilityRule = SetElegibility(x.FirstOrDefault().Value),
                //OvertimeInclusionPolicy = SetExlusion(x.FirstOrDefault().Value)
            })
            .ToDictionary(x => x.Key, xxx => xxx)
            ;
        return result;
    }
    //private OvertimeEligibilityRule? SetElegibility(Dictionary<string, GeneralSettingModel>? data)
    //{
    //    if (data == null) return null;
    //    if (data.TryGetValue(SettingKey.OTEligibility.ToString(), out var model))
    //    {
    //        var value = GeneralSettingsUtil.ParseEnum<OvertimeEligibilityRule>(model.Value, OvertimeEligibilityRule.RequireFullRegularHours);
    //        return value;
    //    }
    //    return null;
    //}

    //private OvertimeInclusionPolicy? SetExlusion(Dictionary<string, GeneralSettingModel>? data)
    //{
    //    if (data == null) return null;
    //    if (data.TryGetValue(SettingKey.OTEligibility.ToString(), out var model))
    //    {
    //        var value = GeneralSettingsUtil.ParseEnum(model.Value, OvertimeInclusionPolicy.IncludePostShiftWork);
    //        return value;
    //    }
    //    return null;
    //}
}
public record struct EmployeePolicyKey(Guid EmployeeId);