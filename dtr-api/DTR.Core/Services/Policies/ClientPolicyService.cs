namespace DTR.Core;

public class ClientPolicyRule
{
    public ClientPolicyKey Key { get; set; }
    public OvertimeEligibilityRule? OvertimeEligibilityRule { get; set; }
    public OvertimeInclusionPolicy? OvertimeInclusionPolicy { get; set; }
}

public class ClientPolicyService
{
    public Dictionary<ClientPolicyKey, ClientPolicyRule> Transform(CompanyPolicyRule companyRule, Dictionary<SettingGroupKey, Dictionary<string, GeneralSettingModel>> data)
    {
        if (data == null) return new Dictionary<ClientPolicyKey, ClientPolicyRule>();
        var result = data
            .GroupBy(x => x.Key)
            .Select(x => new ClientPolicyRule()
            {
                Key = new ClientPolicyKey(x.Key.IdentityId),
                OvertimeEligibilityRule = SetElegibility(companyRule,x.FirstOrDefault().Value),
                OvertimeInclusionPolicy = SetExlusion(companyRule,x.FirstOrDefault().Value)
            })
            .ToDictionary(x => x.Key, xxx=>xxx)
            ;
        return result;
    }

    private OvertimeEligibilityRule? SetElegibility(CompanyPolicyRule companyRule, Dictionary<string, GeneralSettingModel>? data)
    {
        if (data == null) return companyRule.OTEligibility;
        if (data.TryGetValue(SettingKey.OTEligibility.ToString(), out var model))
        {
            var value = GeneralSettingsUtil.ParseEnum(model.Value, companyRule.OTEligibility);
            return value;
        }
        return companyRule.OTEligibility;
    }

    private OvertimeInclusionPolicy? SetExlusion(CompanyPolicyRule companyRule, Dictionary<string, GeneralSettingModel>? data)
    {
        if (data == null) return companyRule.OTInclusionPolicy;
        if (data.TryGetValue(SettingKey.OTInclusion.ToString(), out var model))
        {
            var value = GeneralSettingsUtil.ParseEnum(model.Value, companyRule.OTInclusionPolicy);
            return value;
        }
        return companyRule.OTInclusionPolicy;
    } 
}

public record struct ClientPolicyKey(Guid ClientId);