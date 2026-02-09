namespace DTR.Core;

public class IsEligibleForHoliday : IRuleSpecification
{
    private readonly HolidayType _holidayType;

    public IsEligibleForHoliday(HolidayType holidayType)
    {
        _holidayType = holidayType;
    }
    public bool IsSatisfiedBy(TimeRange input, TimeContext context)
    {
        var evaluator = HolidayEligibiltyEvaluatorFactory.Create(_holidayType);
        var result = evaluator.Evaluate(input, context);
        return result;
    }
}

public interface IHolidayEligibiltyEvaluator
{
    bool Evaluate(TimeRange input, TimeContext context);
}

public class HolidayEligibiltyEvaluatorFactory
{
    public static IHolidayEligibiltyEvaluator Create(HolidayType type)
    {
        switch (type)
        {
            case HolidayType.SPECIAL:
                return new SpecialHolidayEligibiltyEvaluator();
            case HolidayType.LEGAL:
                return new LegalHolidayEligibiltyEvaluator();
            default:
                throw new NotImplementedException("HolidayEligibiltyEvaluatorFactory");
        }
    }
}

public class LegalHolidayEligibiltyEvaluator : IHolidayEligibiltyEvaluator
{
    public bool Evaluate(TimeRange input, TimeContext context)
    {
        var payload = context.Payload;
        var key = SpecEvaluationCache.CreateKey<IsEligibleForHoliday>(context);
        var cached = context.Payload.SharedSpecCache.GetByKey(key);
        if (cached.Found)
            return cached.Value;

        bool isEligible = false;
        var minWorkingMinutes = payload.Data.CurrentShift.MinimumWorkingMinutes;

        // Look back prior days until we find a valid eligibility day
        for (DateOnly curDate = payload.Data.CurrentDate.AddDays(-1);
             curDate >= payload.Data.PayrollStartDate.AddDays(TimeAllowance.AttLookbackDays);
             curDate = curDate.AddDays(-1))
        {
            var result = ProcessLineAsync(curDate, context).GetAwaiter().GetResult();
            if (result == null)
                continue; // skip null results, keep searching

            if (result.WorkTypeEnum == WorkType.Skipped) continue;

            // If no work recorded
            if (result.WorkDate == DateOnly.MinValue)
            {
                if (result.WorkTypeEnum == WorkType.RestDay)
                    continue; // skip rest day, check earlier

                // Eligible if prior day is holiday or paid leave
                isEligible =
                    result.HolCount > 0 ||
                    result.WorkTypeEnum == WorkType.SpecialNonWorking ||
                    result.WorkTypeEnum == WorkType.PaidLeaveOnLegalHoliday ||
                    result.WorkTypeEnum == WorkType.PaidLeave;
                break;
            }

            // If there was work, check hours
            var workhours =
                  result.RegularNetHours
                + result.RestDayHours
                + result.SpecialHolHours
                + result.LegalHolHours;

            if (result.WorkTypeEnum == WorkType.RestDayDuty && minWorkingMinutes.ToHour() > workhours)
                continue; // not enough hours, check earlier

            isEligible = workhours >= minWorkingMinutes.ToHour();
            break;
        }

        payload.SharedSpecCache.Record(key, isEligible);
        return isEligible;

    }


    private async Task<DailyRecord?> ProcessLineAsync(DateOnly curDate, TimeContext context)
    {
        //run dtr line for the specified date
        DailyRecord? result = default;
        var payload = context.Payload;
        var employee = payload.Data.Employee;

        var dtrService = payload.Provider.DtrContextModel.DtrService;
        if (dtrService != null)
        {

            var prioDtr  = await dtrService.GetDTRInfo<DailyRecord>(new DTRRequestPayload(curDate, curDate,
                     employee.DepartmentId, employee.Id, employee.ClientId, employee.PayrollGroupId), ProcessorType.DTRDetail, dtrService.Token,   IncludeNullResponse.Include, true);

            return prioDtr.FirstOrDefault();
        }
        return result;
    }
}
public class SpecialHolidayEligibiltyEvaluator : IHolidayEligibiltyEvaluator
{
    public bool Evaluate(TimeRange input, TimeContext context)
    {
        return true;//eligible automaticaly
    }
}