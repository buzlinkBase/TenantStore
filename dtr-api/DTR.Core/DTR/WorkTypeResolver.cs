namespace DTR.Core;

public static class WorkTypeResolver
{
    public static WorkType Resolve(TimeContext context)
    {
        var att = context.Payload.Provider.AttendanceProvider.CurrentShiftAttendance();
        var withAtt = att.Any();
        var leave = context.Payload.Data.CurrentLeave;
        var isLegal = context.IsLegalHoliday();
        var isSpecial = context.IsSpecialHoliday();

        var special = context.Payload.Provider.HolidayProvider
          .GetHolidayInfoDuringShift(HolidayType.SPECIAL, context.Payload.Data.Employee, context.Payload.Data.CurrentShift)
          ;

        var isNonworkingSpecial = special?.WorkType == HolidayWorkType.NonWorking && isSpecial;
        var IsPaidSpecial = (special?.IsPaid ?? false) && special?.WorkType == HolidayWorkType.NonWorking && isSpecial;
        var isRestDay = new IsRestDaySpec().IsSatisfiedBy(context.CanonicalTimeRange, context);

        if (withAtt && att.Count % 2 != 0)
            return WorkType.Incomplete;

        // Rest day + holiday duty
        if (isRestDay && isLegal && withAtt)
            return WorkType.RestDayLegalHolidayDuty;

        //if (isRestDay && isSpecial && withAtt && isNonworkingSpecial)
        //    return WorkType.RestDaySpecialHolidayDutyNW;

        if (isRestDay && isSpecial && withAtt)
            return WorkType.RestDaySpecialHolidayDuty;

        // Holiday duty
        if (isLegal && withAtt)
            return WorkType.RegularHolidayDuty;

        if (isSpecial && withAtt && isNonworkingSpecial)
            return WorkType.SpecialHolidayDutyNW;

        if (isSpecial && withAtt)
            return WorkType.SpecialHolidayDuty;

        // Holiday no duty
        if (isLegal && !withAtt)
            return WorkType.RegularHoliday;

        if (isSpecial && !withAtt)
            return WorkType.SpecialHoliday;

        // Rest day
        if (isRestDay && withAtt)
            return WorkType.RestDayDuty;

        if (isRestDay && !withAtt)
            return WorkType.RestDay;

        // Leave
        if (leave != null && !withAtt)
        {
            if (isLegal)
                return leave.PayType == PayType.WithPay ? WorkType.PaidLeave : WorkType.PaidLeaveOnLegalHoliday; // or define WorkType.PaidLeaveOnLegalHoliday

            //TODO inspect holiday here for non working special
            if (isSpecial)
                return leave.PayType == PayType.WithPay ? WorkType.PaidLeave : WorkType.PaidLeaveOnSpecialHoliday; // or define WorkType.PaidLeaveOnSpecialHoliday

            if (isRestDay)
                return WorkType.RestDay;

            return leave.PayType == PayType.WithPay ? WorkType.PaidLeave : WorkType.UnpaidLeave;
        }

        if (isSpecial && !withAtt && isNonworkingSpecial)
            return WorkType.SpecialNonWorking;

        //shift is not present 
        ///posible the currentshift is covered by the prior shift
        //if (new IsCurrentShiftPresent().IsSatisfiedBy(TimeRange.Empty, context))
        //    return WorkType.Skipped;

        if (!withAtt) // Absent
            return WorkType.Absent;

        return WorkType.RegularWorkDay;
    }
    //private static bool CheckIfSkipped(TimeContext context)
    //{
    //    var shift = context.Payload.Data.CurrentShift;
    //    if (shift == null) return false;

    //    var employeeId = context.Payload.Data.Employee.Id;
    //    var keys = context.Payload.ContextModel.ValueCache.GetAllKeys();
    //    var fullSkipKey = new CachedKey($"skipped:{shift.StartTime:yyyy-MM-dd HH:mm:ss}->{shift.EndTime:yyyy-MM-dd HH:mm:ss}:{employeeId}");
    //    var dateSkipKey = new CachedKey($"skipped:{shift.ShiftDate.ToString()}:{employeeId}");
    //    //foreach (var k in keys)
    //    //{
    //    //    if (k == fullSkipKey || k == dateSkipKey)
    //    //    {
    //    //        return true;
    //    //    }
    //    //    return false;
    //    //}
    //    return false;
    //}
}

