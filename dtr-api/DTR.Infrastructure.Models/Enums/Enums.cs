using System.ComponentModel.DataAnnotations;

namespace DTR.Models;

public enum BreakMode  
{
    PAID_BREAK,
    UNPAID_BREAK,
    NONE
}

public enum TimeShiftType
{
    FIXED,
    FLEXI
}
public enum SalaryType
{
    HOURLY,
    DAILY,
    WEEKLY,
    SEMI_MONTHLY,
    MONTHLY,
}

public enum PayType
{
    WithPay,
    WithoutPay
}

public enum LeaveDayType
{
    WholeDay,
    HalfDay
}

[Flags]
public enum DayName
{
    Sunday, Monday, Tuesday, Wednesday, Thursday, Friday, Saturday
}
public enum ChangeSchedState
{
    DEFAULT,
    OVERRIDEN,
    REPLACEMENT
}
public enum HolidayType
{
    SPECIAL,
    LEGAL,
}
public enum HolidayWorkType
{
    [Display(Name = "Working")]
    Working,
    [Display(Name = "NonWorking")]
    NonWorking,
}
public enum OTStatus
{
    ForApproval,
    Approved,
    Declined
}
public enum CalculatorsType
{
    RegularTime,
}
public enum DayType
{
    RESTDAY,
    REGULAR,
    REGULAR_OVERTIME,
    LEGAL_HOLIDAY_OVERTIME,
    SPECIAL_HOLIDAY_OVERTIME,
    NIGHT_DIFF,
    NONHOLIDAY,
}
public enum ATT_TYPE
{
    REGISTERED,
    UNREGISTERED
}
public enum LOGSOURCE
{
    MANUAL,
    UPLOADED,
    NETWORK
}
public enum DTRSOURCE
{
    MANUAL,
    SYSTEMCALC,
}

public enum WorkType
{
    RegularWorkDay,
    RestDay,
    RestDayDuty,
    RegularHoliday,
    RegularHolidayDuty,
    PaidLeaveOnLegalHoliday,
    SpecialHoliday,
    SpecialHolidayDutyNW,
    SpecialHolidayDuty,
    RestDayLegalHolidayDuty,
    RestDaySpecialHolidayDuty,
    PaidLeaveOnSpecialHoliday,
    SpecialNonWorking,
    //RestDaySpecialHolidayDutyNW,
    PaidLeave,
    UnpaidLeave,
    Absent,
    Incomplete,
    Skipped,
}

public enum DTRStatus
{
    OPEN,
    LOCKED
}
public enum IncludeNullResponse
{
    Include,
    Ignore
}
public enum SettingKey
{
    OTEligibility,
    OTInclusion,
    AttFillLimit,
    HolidayTimeBasis,
    IsHalfDayLateOn,
    IsWholeDayLateOn,
    HalfDayLateThresholdMinutes,
    WholeDayLateThresholdMinutes,
    NightDiffThreshold,
    IsHolPlusReg,
    HolidayColumnPresentation,
}
public enum HolidayTimeBasis
{
    BasedOnTimeInDayType,// Uses the day type of the employee's time-in (e.g., if they clock in on a holiday)
    BasedOnActualWorkHours // Computes holiday hours based on actual hours worked during the holiday
}
public enum HolidayCreditMode 
{
    AutoCredit,
    NoCredit,
}

public enum ManualEntryLimitEnum
{
    NOLIMIT,
    DONTALLOW,
    ONE,
    TWO,
    THREE,
    FOUR,
}
/// <summary>
/// Defines how overtime (OT) is treated when an employee has undertime (UT) or lateness.
/// </summary>

public enum OvertimeInclusionPolicy
{
    UseEarlyClockIn,        // Includes OT if employee clocks in before scheduled shift
    UsePostShiftWork,       // Includes OT after scheduled shift ends
    UseAllExcessOver8Hours  // Includes any hours beyond 8 as OT, regardless of schedule
}
public enum OvertimeEligibilityRule
{
    RequireFullRegularHours,         // Must complete full 8 regular hours before OT is credited
    OffsetAgainstUndertimeOrLateness, // OT is offset until regular threshold is met
    IndependentOfAttendanceIssues,   // OT is paid regardless of undertime/lateness
    //AllowWithPenaltyAdjustment,      // OT allowed but penalized for undertime
    //BlockIfUndertimeExceedsThreshold, // OT blocked if undertime exceeds limit
    //AllowOnlyIfUndertimeIsApproved,  // OT allowed only for justified undertime
    //OffsetUndertimeNextDayBeforeOT,  // Must offset UT next day before OT is allowed
    //IgnoreUndertimeForCriticalOT     // UT ignored for critical/emergency OT
}