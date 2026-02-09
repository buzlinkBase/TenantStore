namespace DTR.Core;

public record struct SettingRecordKey(string IdentityType, string? IdentityTypeId, string Description);
public class GeneralSettingService
{
    private readonly IDTRUnitOfWork _uow;

    public GeneralSettingService(IDTRUnitOfWork uow)
    {
        _uow = uow;
    }

    //public async Task<Dictionary<string, GeneralSettingModel>> GetSettingsAsync()
    //{
    //    return await _uow.Repository.FindAll<GeneralSetting>()
    //         .ToDictionaryAsync(x => x.Description, x => new GeneralSettingModel() { Id = x.Id, Key = x.Description, Metadata = x.Metadata, IdentityId = x.IdentityTypeId, Value = x.Value });
    //}
    public async Task Remove(string IdentityType)
    {
        _uow.Repository.Remove<GeneralSetting>(x => x.IdentityType == IdentityType);
        await Task.CompletedTask;
    }
    public async Task Remove(string IdentityType, string IdentityId)
    {
        _uow.Repository.Remove<GeneralSetting>(x => x.IdentityTypeId== IdentityId && x.IdentityType == IdentityType);
        await Task.CompletedTask;
    }

    public async Task<Dictionary<string, GeneralSettingModel>> GetSettingsAsync(string IdentityType)
    {
        return await _uow.Repository
            .FindAll<GeneralSetting>()
            .Where(x => x.IdentityType == IdentityType)
            .ToDictionaryAsync(x => x.Description, x => new GeneralSettingModel() { Id = x.Id, Key = x.Description, Metadata = x.Metadata, IdentityId = x.IdentityTypeId, Value = x.Value });
    }

    public async Task<Dictionary<SettingGroupKey, Dictionary<string, GeneralSettingModel>>> GetSettingsAsync(string IdentityType, HashSet<string>? identities)
    {
        if (identities == null || !identities.Any()) return new Dictionary<SettingGroupKey, Dictionary<string, GeneralSettingModel>>();

        return await _uow.Repository
            .FindAll<GeneralSetting>()
            .Where(x => x.IdentityType == IdentityType && identities.Contains(x.IdentityTypeId))
            .GroupBy(x => x.IdentityTypeId)
            .ToDictionaryAsync(x => new SettingGroupKey(Guid.Parse(x.Key)), y => y
                    .GroupBy(x => x.Description)
                    .ToDictionary(x => x.Key, x => new GeneralSettingModel()
                    {
                        Key = x.Key,
                        Metadata = x.FirstOrDefault().Metadata,
                        IdentityId = x.FirstOrDefault().IdentityTypeId,
                        Value = x.FirstOrDefault().Value
                    }));
    }

    public async Task CreateCompanyDefault(Guid TenantId)
    {
        var exists = _uow.Repository.FindAll<GeneralSetting>().FirstOrDefault();
        if (exists != null) return;
        var settings = new List<GeneralSetting>();
        var inclusion = OvertimeInclusionPolicy.UsePostShiftWork;
        var role = OvertimeEligibilityRule.IndependentOfAttendanceIssues;
        var entryLimit = ManualEntryLimitEnum.NOLIMIT;
        var TimeInDayType = HolidayTimeBasis.BasedOnTimeInDayType;
        var HolPresentation  = HolidayCreditMode.AutoCredit;

        settings.Add(new GeneralSetting { TenantId = TenantId, IdentityType = "Company", Description = SettingKey.OTInclusion.ToString(), Value = inclusion.ToString() });
        settings.Add(new GeneralSetting { TenantId = TenantId, IdentityType = "Company", Description = SettingKey.OTEligibility.ToString(), Value = role.ToString() });
        settings.Add(new GeneralSetting { TenantId = TenantId, IdentityType = "Company", Description = SettingKey.AttFillLimit.ToString(), Value = entryLimit.ToString() });
        settings.Add(new GeneralSetting { TenantId = TenantId, IdentityType = "Company", Description = SettingKey.IsHalfDayLateOn.ToString(), Value = "false" });
        settings.Add(new GeneralSetting { TenantId = TenantId, IdentityType = "Company", Description = SettingKey.IsWholeDayLateOn.ToString(), Value = "false" });
        settings.Add(new GeneralSetting { TenantId = TenantId, IdentityType = "Company", Description = SettingKey.HalfDayLateThresholdMinutes.ToString(), Value = "0" });
        settings.Add(new GeneralSetting { TenantId = TenantId, IdentityType = "Company", Description = SettingKey.WholeDayLateThresholdMinutes.ToString(), Value = "0" });
        settings.Add(new GeneralSetting { TenantId = TenantId, IdentityType = "Company", Description = SettingKey.NightDiffThreshold.ToString(), Value = "5" });
        settings.Add(new GeneralSetting { TenantId = TenantId, IdentityType = "Company", Description = SettingKey.HolidayTimeBasis.ToString(), Value = TimeInDayType.ToString() });
        settings.Add(new GeneralSetting { TenantId = TenantId, IdentityType = "Company", Description = SettingKey.IsHolPlusReg.ToString(), Value = "true" });
        settings.Add(new GeneralSetting { TenantId = TenantId, IdentityType = "Company", Description = SettingKey.HolidayColumnPresentation.ToString(), Value = HolPresentation.ToString() });
        await AddRangeAsync(settings, TenantId);

    }
    public async Task AddRangeAsync(List<GeneralSetting> models, Guid tenantId)
    {
        if (models == null || !models.Any()) return;

        // Assign TenantId to all incoming models
        foreach (var item in models)
        {
            item.TenantId = tenantId;
        }

        // Create keys for incoming models
        var incomingKeys = models
            .Select(x => new SettingRecordKey
            {
                IdentityType = x.IdentityType,
                IdentityTypeId = x.IdentityTypeId,
                Description = x.Description
            })
            .ToHashSet();

        // Fetch existing records from DB
        var existingRecords = _uow.Repository
            .FindAll<GeneralSetting>()
            .Where(x => x.TenantId == tenantId)
            .ToList();

        var existingKeys = existingRecords
            .Select(x => new SettingRecordKey
            {
                IdentityType = x.IdentityType,
                IdentityTypeId = x.IdentityTypeId,
                Description = x.Description
            })
            .ToHashSet();

        // Split models into new and existing
        var newModels = models
            .Where(x => !existingKeys.Contains(new SettingRecordKey
            {
                IdentityType = x.IdentityType,
                IdentityTypeId = x.IdentityTypeId,
                Description = x.Description
            }))
            .ToList();

        var updateModels = models
            .Where(x => existingKeys.Contains(new SettingRecordKey
            {
                IdentityType = x.IdentityType,
                IdentityTypeId = x.IdentityTypeId,
                Description = x.Description
            }))
            .ToList();

        // Add new records
        if (newModels.Any())
        {
            await _uow.Repository.AddRangeAsync(newModels);
        }

        // Update existing records
        foreach (var updateModel in updateModels)
        {
            var existing = existingRecords.FirstOrDefault(x =>
                x.IdentityType == updateModel.IdentityType &&
                x.IdentityTypeId == updateModel.IdentityTypeId &&
                x.Description == updateModel.Description);

            if (existing != null)
            {
                // Update only the fields that changed
                existing.Value = updateModel.Value;
                existing.Metadata = updateModel.Metadata;
                _uow.Repository.Update(existing);
            }
        }
    }
    public void AddOrUpdate(GeneralSetting model)
    {
        var existing = _uow.Repository.FindAll<GeneralSetting>()
            .FirstOrDefault(x => x.IdentityType == model.IdentityType
                 && x.IdentityTypeId == model.IdentityTypeId
                 && x.Description == model.Description);
        if (existing == null)
        {
            AddOrUpdate(model);
        }
        else
        {
            existing.Value = model.Value;
            existing.Metadata = model.Metadata;
            AddOrUpdate(existing);
        }
    }
    public async Task<bool> CommitChangesAsync()
    {
        return await _uow.CommitChangesAsync();
    }

}

public record struct SettingGroupKey(Guid IdentityId);

public class GeneralSettingsUtil
{
    public static T ParseEnum<T>(string input, T fallback) where T : struct, Enum
    {
        if (Enum.TryParse<T>(input, ignoreCase: true, out var result))
        {
            return result;
        }
        return fallback;
    }
    public static bool ParseBool(string input, bool fallback)
    {
        if (bool.TryParse(input, out var result))
        {
            return result;
        }
        return fallback;
    }
    public static int ParseInt(string input, int fallback)
    {
        if (int.TryParse(input, out var result))
        {
            return result;
        }
        return fallback;
    }
    public static double ParseDouble(string input, double fallback)
    {
        if (double.TryParse(input, out var result))
        {
            return result;
        }
        return fallback;
    }
}