namespace DTR.Core;

public class AttendancePairGrabber
{
    public static List<Attendance> GetPairs(List<Attendance> currentDateAttendance, double gapToCaptureMinutes = 2)
    {
        if (!currentDateAttendance.Any()) return [];
        List<Attendance> record = new List<Attendance>();
        Attendance? previousLog = null;

        foreach (var log in currentDateAttendance.OrderBy(x => x.WorkDateTime))
        {
            if (previousLog != null)
            {
                double timeDifference = (log.WorkDateTime - previousLog.WorkDateTime).TotalMinutes;

                if (timeDifference >= gapToCaptureMinutes)
                {
                    // Add both logs as a valid pair
                    record.Add(previousLog);
                    record.Add(log);

                    // Mark this log as processed, so it won't be paired again
                    previousLog = default;
                    continue;
                }
            }
            // Move to the next log only if a pair was NOT created
            previousLog = log;
        }
        return record;
    }
    public static double GetTotalMinutesPair(List<Attendance> pairedAttendance)
    {
        if (pairedAttendance == null || pairedAttendance.Count < 2)
            return 0;

        var sorted = pairedAttendance.OrderBy(x => x.WorkDateTime).ToList();
        double totalMinutes = 0;

        for (int i = 0; i < sorted.Count - 1; i += 2)
        {
            totalMinutes += CalculateWorkHours(sorted[i].WorkDateTime, sorted[i + 1].WorkDateTime);
        }

        return totalMinutes;
    }

    private static double CalculateWorkHours(DateTime start, DateTime end)
    {
        if (end < start) return 0;
        return TimeRangeCalculator.GetTotalMinutes( start, end);
    }

}
