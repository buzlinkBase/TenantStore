namespace DTR.Core;

public class BreakWindowExtractor
{
    public static List<TimeRecord> ExtractBreakWindows(List<TimeRecord> punches)
    {
        var breaks = new List<TimeRecord>();
        for (int i = 0; i < punches.Count - 1; i++)
        {
            var current = punches[i];
            var next = punches[i + 1];

            var gap = next.StartTime - current.EndTime;
            if (gap.TotalMinutes >= 3) // configurable threshold
            {
                breaks.Add(new TimeRecord(current.EndTime, next.StartTime, "PAID_BREAK"));
            }
        }
        return breaks;
    }
}

public interface IBreakExtractor
{
    TimeRangeCollection Extract(TimeRangeCollection punches, CurrentShift shift);
}

public static class BreakWindowHelper
{
    public static (DateTime Start, DateTime End) ClampBreakWindow(
        DateTime rawStart,
        DateTime rawEnd,
        DateTime shiftStart,
        DateTime shiftEnd)
    {
        // Clamp to shift boundaries
        var start = rawStart < shiftStart ? shiftStart : rawStart;
        var end = rawEnd > shiftEnd ? shiftEnd : rawEnd;

        // Ensure valid window
        if (start >= end)
        {
            // Return an empty/invalid range
            return (shiftStart, shiftStart);
        }

        return (start, end);
    }
}

public class BreakExtractorFactory
{
    public static List<IBreakExtractor> Create(TimeContext context)
    {
        //only capture breaks within time window 
        return new List<IBreakExtractor>()
        {
            new AmBreakExtractor(context),
            new PmBreakExtractor(context),
            new LunchBreakExtractor(context)
        };
    }
}

public class AmBreakExtractor : IBreakExtractor
{
    private readonly TimeContext _context;

    public AmBreakExtractor(TimeContext context)
    {
        _context = context;
    }

    public TimeRangeCollection Extract(TimeRangeCollection punches, CurrentShift shift)
    {
        // Guard clause: AM break must be enabled and have valid times
        if (shift.WithAMBreak != BreakMode.PAID_BREAK
            || !shift.AMBreakStartTime.HasValue
            || !shift.AMBreakEndTime.HasValue)
        {
            return new TimeRangeCollection();
        }

        // Raw allowance-adjusted times
        var rawStart = shift.AMBreakStartTime.Value.AddMinutes(TimeAllowance.SnackBreakAllowance * -1);
        var rawEnd = shift.AMBreakEndTime.Value.AddMinutes(TimeAllowance.SnackBreakAllowance);

        // Clamp to shift boundaries using helper
        var (startTime, endTime) = BreakWindowHelper.ClampBreakWindow(rawStart, rawEnd, shift.StartTime, shift.EndTime);

        // If invalid window, return empty
        if (startTime == endTime)
        {
            return new TimeRangeCollection();
        }

        var breaks = new TimeRangeCollection();

        // Look for gaps between punches inside the break window
        for (int i = 0; i < punches.Count - 1; i++)
        {
            var current = punches[i];
            var next = punches[i + 1];

            DateTime gapStart = current.EndTime;
            DateTime gapEnd = next.StartTime;

            if (gapStart >= startTime && gapEnd <= endTime)
            {
                breaks.Add(new TimeRecord(gapStart, gapEnd, "AM_BREAK"));
            }
        }

        // Record results in ledger
        _context.Payload.Ledger.RecordByTag("AM_BREAK", _context, breaks.ToTimeRange());

        return breaks;
    }
}

public class PmBreakExtractor : IBreakExtractor
{
    private readonly TimeContext _context;

    public PmBreakExtractor(TimeContext context)
    {
        _context = context;
    }

    public TimeRangeCollection Extract(TimeRangeCollection punches, CurrentShift shift)
    {
        if (shift.WithPMBreakTime != BreakMode.PAID_BREAK
            || !shift.PMBreakStartTime.HasValue
            || !shift.PMBreakEndTime.HasValue)
        {
            return new TimeRangeCollection();
        }

        // Raw allowance-adjusted times
        var rawStart = shift.PMBreakStartTime.Value.AddMinutes(TimeAllowance.SnackBreakAllowance * -1);
        var rawEnd = shift.PMBreakEndTime.Value.AddMinutes(TimeAllowance.SnackBreakAllowance);

        // Clamp to shift boundaries
        var (startTime, endTime) = BreakWindowHelper.ClampBreakWindow(rawStart, rawEnd, shift.StartTime, shift.EndTime);

        if (startTime == endTime) return new TimeRangeCollection(); // invalid window

        var breaks = new TimeRangeCollection();

        for (int i = 0; i < punches.Count - 1; i++)
        {
            var current = punches[i];
            var next = punches[i + 1];

            DateTime gapStart = current.EndTime;
            DateTime gapEnd = next.StartTime;

            if (gapStart >= startTime && gapEnd <= endTime)
            {
                breaks.Add(new TimeRecord(gapStart, gapEnd, "PM_BREAK"));
            }
        }

        _context.Payload.Ledger.RecordByTag("PM_BREAK", _context, breaks.ToTimeRange());
        return breaks;
    }
}
public class LunchBreakExtractor : IBreakExtractor
{
    private readonly TimeContext _context;

    public LunchBreakExtractor(TimeContext context)
    {
        _context = context;
    }

    public TimeRangeCollection Extract(TimeRangeCollection punches, CurrentShift shift)
    {
        if (shift.LunchBreakOption != BreakMode.PAID_BREAK ) return new TimeRangeCollection();

        var breakAllowance = TimeAllowance.LunchPaidBreakCaptureAllowance;

        // Calculate raw allowance-adjusted times
        var rawStart = shift.LunchStartTime.HasValue
            ? shift.LunchStartTime.Value.AddMinutes(breakAllowance * -1)
            : shift.StartTime;

        var rawEnd = shift.LunchEndTime.HasValue
            ? shift.LunchEndTime.Value.AddMinutes(breakAllowance)
            : shift.EndTime;

        // Clamp to shift boundaries
        var (startTime, endTime) = BreakWindowHelper.ClampBreakWindow(rawStart, rawEnd, shift.StartTime, shift.EndTime);

        if (startTime == endTime) return new TimeRangeCollection(); // invalid window

        var breaks = new TimeRangeCollection();

        for (int i = 0; i < punches.Count - 1; i++)
        {
            var current = punches[i];
            var next = punches[i + 1];

            DateTime gapStart = current.EndTime;
            DateTime gapEnd = next.StartTime;

            if (gapStart >= startTime && gapEnd <= endTime)
            {
                breaks.Add(new TimeRecord(gapStart, gapEnd, "LUNCH_BREAK"));
            }
        }

        _context.Payload.Ledger.RecordByTag("LUNCH_BREAK", _context, breaks.ToTimeRange());
        return breaks;
    }
}

public static class PaidBreakCalculator
{
    /// <summary>
    /// Filters actual break windows to fit within the allowed paid break duration.
    /// Truncates the last break if it exceeds the remaining allowance.
    /// </summary>
    /// <param name="actualBreaks">List of detected break windows</param>
    /// <param name="allowedBreakMinutes">Total allowed paid break minutes</param>
    /// <returns>List of TimeRecords tagged as "PAID_BREAK"</returns>
    public static TimeRangeCollection ComputePaidBreaks(TimeRangeCollection actualBreaks, double allowedBreakMinutes)
    {
        if (!actualBreaks.Any()) return new();
        var paidBreaks = new TimeRangeCollection();
        double accumulated = 0;

        foreach (var br in actualBreaks.OrderBy(b => b.StartTime))
        {
            var duration = (br.EndTime - br.StartTime).TotalMinutes;
            var remaining = allowedBreakMinutes - accumulated;

            if (remaining <= 0)
                break;

            if (duration <= remaining)
            {
                // Full break fits within allowance
                paidBreaks.Add(new TimeRecord(br.StartTime, br.EndTime, "PAID_BREAK"));
                accumulated += duration;
            }
            else
            {
                // Truncate break to fit remaining allowance
                var truncatedEnd = br.StartTime.Add(TimeSpan.FromMinutes(remaining));
                paidBreaks.Add(new TimeRecord(br.StartTime, truncatedEnd, "PAID_BREAK"));
                accumulated += remaining;
                break;
            }
        }
        return paidBreaks;
    }
}
public class OverbreakCalculator
{
    public static TimeRangeCollection ComputeOverbreaks(TimeRangeCollection actualBreaks, double allowedBreakMinutes)
    {
        if (!actualBreaks.Any()) return new();
        var overBreaks = new TimeRangeCollection();
        double accumulated = 0;

        foreach (var br in actualBreaks.OrderBy(b => b.StartTime))
        {
            var duration = (br.EndTime - br.StartTime).TotalMinutes;
            var remaining = allowedBreakMinutes - accumulated;

            if (remaining <= 0)
            {
                // Entire break is overbreak
                overBreaks.Add(new TimeRecord(br.StartTime, br.EndTime, "OVERBREAK"));
                continue;
            }

            if (duration > remaining)
            {
                // Partial overbreak
                var overStart = br.StartTime.Add(TimeSpan.FromMinutes(remaining));
                overBreaks.Add(new TimeRecord(overStart, br.EndTime, "OVERBREAK"));
            }

            accumulated += duration;
        }
        return overBreaks;
    }
}

