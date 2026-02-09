using System.ComponentModel.DataAnnotations.Schema;

namespace DTR.Models;

[DisableSoftDelete]
public class DailyRecord : BaseEntity, IUserField
{
    [NotMapped]
    public WorkType WorkTypeEnum { get; set; }  
    public string WorkType { get; set; } = "REGULAR WOsRK DAY";
    public string FullName { get; set; }
    public Guid EmployeeId { get; set; }
    public string empCode { get; set; }
    public int BioId { get; set; } = 0;

    public virtual Employee Employee { get; set; }
    public DateOnly WorkDate { get; set; }
    public string ShiftName { get; set; } = string.Empty;
    public DateTime ShiftStartTime { get; set; }
    public DateTime ShiftEndTime { get; set; }

    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }


    //regualar Days
    public double LHHolidayTotalDays { get; set; }
    public double SPHolidayTotalDays { get; set; }
    public double OTOnSpecialHolidayDays { get; set; }
    public double OTOnLegalHolidayDays { get; set; }

    //Days
    public double RegularWorkingDays { get; set; }
    public double RegularNDDays { get; set; }
    public double RegularOTDays { get; set; }
    public double RegularNDOTDays { get; set; }
    public double RestDayDays { get; set; }
    public double RestDayNDDays { get; set; }
    public double RestDayOTDays { get; set; }
    public double RestDayNDODays { get; set; }

    public double OTOnSpecialHolidayMinutes { get; set; }
    public double OTOnLegalHolidayMinutes { get; set; }


    //minutes regardless if off or not

    public double LateMinutes { get; set; } = 0;
    public double UTMinutes { get; set; } = 0;
    public double OverBreakMinutes { get; set; } = 0;
    public double OTMinutes { get; set; } = 0;
    public double ND { get; set; } = 0;
    public double NDOT { get; set; } = 0;
    public double SP { get; set; } = 0;
    public double LH { get; set; } = 0;

    [NotMapped]
    public double LegalHolOTMinutes { get; set; } = 0;
    [NotMapped]
    public double LegalHolNightDiffMinutes { get; set; } = 0;
    [NotMapped]
    public double LegalHolNightDiffOTMinutes { get; set; } = 0;

    [NotMapped]
    public double SpecialHolOTMinutes { get; set; } = 0;
    [NotMapped]
    public double SpecialHolNightDiffMinutes { get; set; } = 0;
    [NotMapped]
    public double SpecialHolNightDiffOTMinutes { get; set; } = 0; 

    //end minutes


    public double RegDayMinutes { get; set; }
    public double RegDayNDMinutes { get; set; }
    public double RegDayOTMinutes { get; set; }
    public double RegDayNDOMinutes { get; set; }

    //rest day [Regular]
    public double RestDayMinutes { get; set; }
    public double RestDayNDMinutes { get; set; }
    public double RestDayOTMinutes { get; set; }
    public double RestDayNDOMinutes { get; set; }
    ///  

    //hours
    public double LateHours { get; set; }
    [NotMapped]
    public double UTHours { get; set; }
    public double OverBreakHours { get; set; }
    public double LateForOTHours { get; set; }

    public double RegularNetHours { get; set; }
    public double RegularOTHours { get; set; }
    public double RegularNDHours { get; set; }
    public double RegularNDOTHours { get; set; }

    public double RestDayHours { get; set; }
    public double RestDayOTHours { get; set; }
    public double RestDayNDHours { get; set; }
    public double RestDayNDOTHours { get; set; }

    public double LegalHolHours { get; set; }
    public double LegalHolOTHours { get; set; }
    public double LegalHolNightDiffHours { get; set; }
    public double LegalHolNightDiffOTHours { get; set; }

    public double SpecialHolHours { get; set; }
    public double SpecialHolOTHours { get; set; }
    public double SpecialHolNightDiffHours { get; set; }
    public double SpecialHolNightDiffOTHours { get; set; }

    public double RestLegalDayHours { get; set; }
    public double RestLegalDayOTHours { get; set; }
    public double RestLegalDayNDHours { get; set; }
    public double RestLegalDayNDOTHours { get; set; }

    public double RestSpecialDayHours { get; set; }
    public double RestSpecialDayOTHours { get; set; }
    public double RestSpecialDayNDHours { get; set; }
    public double RestSpecialDayNDOTHours { get; set; }

    public double RawOTHours { get; set; }
    public double LeaveMinutes { get; set; }
    public double OB { get; set; }
    public int Absent { get; set; }

    [NotMapped]
    public double TotalOTHours
    {
        get
        {
            return RegularOTHours + RestDayOTHours;
        }
    }

    [NotMapped]
    public double TotalHours => new[]
                    {
                    RegularNetHours, RegularOTHours, RegularNDHours, RegularNDOTHours,
                    RestDayHours, RestDayOTHours, RestDayNDHours, RestDayNDOTHours,
                    //RestLegalDayHours, RestLegalDayOTHours, RestLegalDayNDHours, RestLegalDayNDOTHours,
                    //RestSpecialDayHours, RestSpecialDayOTHours, RestSpecialDayNDHours, RestSpecialDayNDOTHours,
                    LegalHolHours, LegalHolOTHours, LegalHolNightDiffHours, LegalHolNightDiffOTHours,
                    SpecialHolHours, SpecialHolOTHours, SpecialHolNightDiffHours, SpecialHolNightDiffOTHours
                }.Sum();
    public string Note { get; set; } = string.Empty;
    public DTRStatus RecordStatus { get; set; } = DTRStatus.OPEN;
    [NotMapped]
    public int AttStatus { get; set; }
    public DTRSOURCE Source { get; set; }
    public Guid UserId { get; set; }
    public Guid? ClientId { get; set; }
    public Guid? BranchId  { get; set; }
    public Guid? PayrollGroupId { get; set; }
    public Guid? DepartmentId { get; set; }
    public int HolCount { get; set; } = 0;
    public int SPCount { get; set; } = 0;
    public double ShiftWorkingHour  { get; set; }
}
public class DailyRecordPunchCounter : BaseEntity
{
    public DateOnly PayrollDate { get; set; }
    public Guid EmployeeId { get; set; }
    public int ManualPunchCount { get; set; }
    public DateTime WorkTime { get; set; }
    [NotMapped]
    public override string EntityType { get => this.GetType().Name; set => base.EntityType = value; }
}
