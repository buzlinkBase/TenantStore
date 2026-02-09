namespace DTR.Core;

public class HolidayOTCalculator
{ 
    public static TimeRange Calculate(TimeRange ot, TimeRange holiday)
    {
        return ot.TimeRecords
            .Intersect(holiday.TimeRecords)
            .ToTimeRange(); 
    }
}
