namespace DTR.Core;


public class TimeRangeCalculator
{
   //private  const double MinuteEpsilon = 1E-6; // ~0.06 milliseconds
   // private static double NormalizeMinutes(double minutes) =>
   //     Math.Abs(minutes) < MinuteEpsilon ? 0 : minutes;
    public static double GetTotalMinutes(DateTime startTime, DateTime endTime)
    {
        return Math.Max(0, (endTime - startTime).TotalMinutes);
    }

    public static double RoundToMinimumBound(double workingMinutes, double minBoundMinutes, double maxBoundMinutes)
    {
        if (minBoundMinutes >= workingMinutes && minBoundMinutes < maxBoundMinutes)
            return minBoundMinutes; 
        return workingMinutes;
    }

    public static TimeRange GetTimeRange(List<Attendance> pairedAtt)
    {
        if (!pairedAtt.Any()) return TimeRange.Empty; 

        return TimeRangeSetter
            .SetTimeRangeCollection(pairedAtt)
            .ToTimeRange()
            ;

        //var totalMinutes = AttendancePairGrabber.GetTotalMinutesPair(pairedAtt);
        //new TimeRange(totalMinutes, );

    }

    public static TimeRange GetTimeRange(DateTime punchIn, DateTime punchOut, CurrentShift shift)
    {
        return GetTimeRange(punchIn, punchOut, shift.StartTime, shift.EndTime);
    }

    public static TimeRange GetTimeRange(DateTime punchIn, DateTime punchOut, DateTime shiftStartTime, DateTime shiftEndTime)
    {
        var boxTime = EffectiveTimeResolver.CalculateShiftSpan(punchIn, punchOut, shiftStartTime, shiftEndTime);
        double totalMinutes = GetTotalMinutes(boxTime.Start, boxTime.End);
        totalMinutes = Math.Max(0, totalMinutes);//ensure no negative time
        return new TimeRange(totalMinutes, TimeRangeSetter.SetTimeRangeCollection(boxTime.Start, boxTime.End));
    }
}

public class EffectiveTimeResolver
{
    public static (DateTime Start, DateTime End) CalculateShiftSpan(DateTime punchIn, DateTime punchOut, DateTime shiftStartTime, DateTime shiftEndTime)
    {
        DateTime adjustedStart = punchIn < shiftStartTime ? shiftStartTime : punchIn;
        DateTime adjustedEnd = punchOut > shiftEndTime ? shiftEndTime : punchOut;
        return new(adjustedStart, adjustedEnd);
    }
    public static (DateTime Start, DateTime End) CalculateShiftSpan(DateTime punchIn, DateTime punchOut, CurrentShift shift)
    {
        return CalculateShiftSpan(punchIn, punchOut, shift.StartTime, shift.EndTime);
    }
}

