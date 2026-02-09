namespace DTR.Core;

public static class DateOnlyExtensions
{
    public static DateTime AtStartOfDay(this DateOnly date)
        => date.ToDateTime(TimeOnly.MinValue);
    public static DateTime AtStartOfDay(this DateTime date)
     => date.Date;
}

public static class TimeExtensions
{
    public static double ToHour(this TimeRange timeRange) => TimeConverter.MinutesToHour(timeRange.TotalMinutes);
    public static double ToHour(this double minutes) => TimeConverter.MinutesToHour(minutes);
    public static double ToDays(this TimeRange timeRange) => TimeConverter.MinutesToDays(timeRange.TotalMinutes);
    public static double ToDays(this double minutes) => TimeConverter.MinutesToDays(minutes);
    public static string ToRangeString(this TimeRecord record)
    {
        return $"{record.StartTime:HH:mm}–{record.EndTime:HH:mm}";
    }
    public static bool IsEmpty(this TimeRange range)
    {
        return range == null || range.TotalMinutes <= 0;
    }
    public static TimeRecord Tag(this TimeRecord r, string label)
    => new(r.StartTime, r.EndTime, label);
    public static TimeRangeCollection Retag(this TimeRangeCollection source, string newTag)
    {
        if (string.IsNullOrWhiteSpace(newTag) || source == null || source.Count == 0)
            return source ?? new TimeRangeCollection();

        var retagged = source
            .Select(r => r.Tag(newTag))
            .ToTimeRangeCollection();

        return retagged;
    }
    public static double TotalMinutes(this TimeRangeCollection records)
    {
        var result = records
            .Where(x => x.IsValid())
            .Sum(r => r.TotalMinutes());
        return result;
    }
    public static DateTime ToDateTime(this TimeSpan timeSpan, DateOnly date)
    {
        return date.ToDateTime(TimeOnly.MinValue).Add(timeSpan);
    }
    public static bool IsValid(this TimeRecord record)
    {
        return record.StartTime != DateTime.MinValue
             && record.EndTime != DateTime.MinValue
             && record.StartTime < record.EndTime
             ;
    }
    public static double TotalMinutes(this TimeRecord record)
    {
        return TimeRangeCalculator.GetTotalMinutes(record.StartTime, record.EndTime);
    }
    public static TimeRange ToTimeRange(this TimeRangeCollection collection)
    {
        if (collection is null || collection.Count == 0)
            return TimeRange.Empty;

        var totalMinutes = collection.TotalMinutes();

        return new TimeRange(totalMinutes, collection);
    }


    public static TimeRangeCollection ToTimeRangeCollection(this IEnumerable<TimeRecord> ranges)
    {
        // Defensive fallback if null
        if (ranges is null)
            return new TimeRangeCollection();

        // Optionally: filter invalid or zero-length ranges
        var validRanges = ranges
            .Where(r => TimeRangeCalculator.GetTotalMinutes(r.StartTime, r.EndTime) > 0)
            .OrderBy(r => r.StartTime)
            .ToList();
        return new TimeRangeCollection(validRanges);
    }
    public static TimeRangeCollection MergeOverlapping(this TimeRangeCollection records)
    {
        var sorted = records
             .OrderBy(r => r.StartTime)
             .ToList();

        var merged = new TimeRangeCollection();
        foreach (var range in sorted)
        {
            if (!merged.Any())
            {
                merged.Add(range);
                continue;
            }

            var last = merged.Last();
            if (range.StartTime <= last.EndTime)
            {
                last.EndTime = new[] { last.EndTime, range.EndTime }.Max();
            }
            else
            {
                merged.Add(range);
            }
        }
        return merged;
    }
    public static TimeRangeCollection Exclude(this TimeRangeCollection baseRange, TimeRangeCollection exclusions)
    {
        var result = new TimeRangeCollection();
        foreach (var baseItem in baseRange)
        {
            var remainingSlices = new List<TimeRecord> { baseItem };

            foreach (var exclusion in exclusions)
            {
                remainingSlices = remainingSlices
                    .SelectMany(slice => SubtractRange(slice, exclusion))
                    .ToList();
            }
            result.AddRange(remainingSlices);
        }
        return result;
    }
    private static IEnumerable<TimeRecord> SubtractRange(TimeRecord original, TimeRecord removal)
    {
        var oStart = original.StartTime;
        var oEnd = original.EndTime;
        var rStart = removal.StartTime;
        var rEnd = removal.EndTime;

        // ⛔ No overlap
        if (rEnd <= oStart || rStart >= oEnd)
        {
            yield return original;
            yield break;
        }

        // ✂️ Trim start if there's room
        if (rStart > oStart)
            yield return new TimeRecord(oStart, rStart, original.Tag);

        // ✂️ Trim end if there's room
        if (rEnd < oEnd)
            yield return new TimeRecord(rEnd, oEnd, original.Tag);
    }
    public static TimeRange CapAndCrop(this TimeRangeCollection actualTime, CurrentShift shift)
    {
        return CapAndCrop(actualTime, shift.StartTime, shift.EndTime, TimeRangeCalculator.GetTotalMinutes(shift.StartTime, shift.EndTime));
    }
    public static TimeRange CapAndCrop(this TimeRangeCollection actualTime, CurrentShift shift, double maxMinutes)
    {
        return CapAndCrop(actualTime, shift.StartTime, shift.EndTime, maxMinutes);
    }
    public static TimeRange CapAndCrop(this TimeRangeCollection actualTime, DateTime shiftStart, DateTime shiftEnd, double maxMinutes)
    {
        var shiftRange = new TimeRangeCollection
        {
            new TimeRecord(shiftStart, shiftEnd)
        };

        // Limit only to shift boundary
        var withinShift = actualTime.Intersect(shiftRange);
        TimeRangeCollection merged = withinShift.MergeOverlapping();            // Avoid over-count
        var total = merged.TotalMinutes();

        if (total <= maxMinutes)
            return new TimeRange(total, merged);

        // Cap at Max Working Limit
        var capped = new TimeRangeCollection();
        double remaining = maxMinutes;

        foreach (var slice in merged)
        {
            var duration = (slice.EndTime - slice.StartTime).TotalMinutes;
            if (duration <= 0) continue;

            if (duration <= remaining)
            {
                capped.Add(slice);
                remaining -= duration;
            }
            else
            {
                var cappedEnd = slice.StartTime.AddMinutes(remaining);
                capped.Add(new TimeRecord(slice.StartTime, cappedEnd));
                break;
            }
        }

        return new TimeRange(maxMinutes, capped);
    }
    public static TimeRange CropFromStart(this TimeRangeCollection source, double minutesToRetain)
    {
        if (source == null || source.Count == 0 || minutesToRetain <= 0)
            return new TimeRange();

        var cropped = new TimeRangeCollection();
        double remaining = minutesToRetain;

        foreach (var record in source.OrderBy(r => r.StartTime))
        {
            if (remaining <= 0)
                break;

            var duration = (record.EndTime - record.StartTime).TotalMinutes;
            if (duration <= 0)
                continue;

            if (duration <= remaining)
            {
                cropped.Add(record);
                remaining -= duration;
            }
            else
            {
                var newEnd = record.StartTime.AddMinutes(remaining);
                cropped.Add(new TimeRecord
                {
                    StartTime = record.StartTime,
                    EndTime = newEnd,
                    Tag = record.Tag
                });
                remaining = 0;
            }
        }
        return cropped.ToTimeRange();
    }
    public static TimeRange CropFromEnd(this TimeRangeCollection source, double minutesToRetain)
    {
        if (source == null || source.Count == 0 || minutesToRetain <= 0)
            return new TimeRange();

        var cropped = new TimeRangeCollection();
        double remaining = minutesToRetain;

        foreach (var record in source.OrderByDescending(r => r.EndTime))
        {
            if (remaining <= 0)
                break;

            var duration = (record.EndTime - record.StartTime).TotalMinutes;
            if (duration <= 0)
                continue;

            if (duration <= remaining)
            {
                cropped.Add(record);
                remaining -= duration;
            }
            else
            {
                var newStart = record.EndTime.AddMinutes(-remaining);
                cropped.Add(new TimeRecord
                {
                    StartTime = newStart,
                    EndTime = record.EndTime,
                    Tag = record.Tag
                });
                remaining = 0;
            }
        }

        return cropped.ToTimeRange();
    }
    public static TimeRange DeductFromEnd(this TimeRangeCollection source, double minutesToRemove)
    {
        if (source == null || source.Count == 0 || minutesToRemove <= 0)
            return source.ToTimeRange();

        var result = new TimeRangeCollection();
        double remaining = minutesToRemove;

        foreach (var record in source.OrderByDescending(r => r.EndTime))
        {
            if (remaining <= 0)
            {
                result.Add(record);
                continue;
            }

            var duration = (record.EndTime - record.StartTime).TotalMinutes;
            if (duration <= 0)
                continue;

            if (duration <= remaining)
            {
                // Skip entire record
                remaining -= duration;
            }
            else
            {
                // Trim from end
                var newEnd = record.EndTime.AddMinutes(-remaining);
                result.Add(new TimeRecord
                {
                    StartTime = record.StartTime,
                    EndTime = newEnd,
                    Tag = record.Tag
                });
                remaining = 0;
            }
        }

        // Reorder to preserve original ascending sequence
        return new TimeRangeCollection(result.OrderBy(r => r.StartTime)).ToTimeRange();
    }
    public static TimeRangeCollection Intersect(this TimeRangeCollection source, TimeRangeCollection mask, string tag = "")
    {
        var result = new TimeRangeCollection();
        foreach (var src in source)
        {
            foreach (var m in mask)
            {
                var start = new[] { src.StartTime, m.StartTime }.Max();
                var end = new[] { src.EndTime, m.EndTime }.Min();
                if (start < end)
                    result.Add(new TimeRecord(start, end, tag ?? $"{src.Tag}_x_{m.Tag}"));
            }
        }
        return result;
    }
    public static TimeRecord? Intersect(this TimeRecord a, TimeRecord b, string? tag = null)
    {
        var start = a.StartTime > b.StartTime ? a.StartTime : b.StartTime;
        var end = a.EndTime < b.EndTime ? a.EndTime : b.EndTime;

        if (start >= end)
            return TimeRecord.Null();

        return new TimeRecord(start, end, tag ?? $"{a.Tag}_x_{b.Tag}");

    }
    public static bool IsOverlaps(this TimeRecord a, TimeRecord b)
    {
        return a.StartTime < b.EndTime && b.StartTime < a.EndTime;
    }
    public static TimeRange FlattenUntilShiftEnd(this TimeRangeCollection records, DateTime shiftStartTime, TimeSpan shiftDuration)
    {
        if (records == null || !records.Any())
            return new TimeRange(0, new TimeRangeCollection());

        var shiftEndTime = shiftStartTime.Add(shiftDuration);
        var sorted = records.OrderBy(r => r.StartTime).ToList();
        var collected = new TimeRangeCollection();
        double accumulated = 0;

        foreach (var r in sorted)
        {
            // Skip records that end before the shift starts
            if (r.EndTime <= shiftStartTime)
                continue;

            // Skip records that start after the shift ends
            if (r.StartTime >= shiftEndTime)
                break;

            // Crop record if it overlaps the shift boundary
            var effectiveStart = r.StartTime < shiftStartTime ? shiftStartTime : r.StartTime;
            var effectiveEnd = r.EndTime > shiftEndTime ? shiftEndTime : r.EndTime;

            var croppedDuration = (effectiveEnd - effectiveStart).TotalMinutes;
            if (croppedDuration > 0)
            {
                var cropped = TimeRecord.Set(effectiveStart, effectiveEnd, r.Tag);
                collected.Add(cropped);
                accumulated += croppedDuration;
            }
        }
        return new TimeRange(accumulated, collected);
    }

    public static TimeRange FlattenUntil(this TimeRangeCollection records, double targetMinutes)
    {
        var sorted = records.OrderBy(r => r.StartTime).ToList();
        var collected = new TimeRangeCollection();
        double accumulated = 0;

        foreach (var r in sorted)
        {
            var duration = r.TotalMinutes();
            if (accumulated + duration <= targetMinutes)
            {
                collected.Add(r);
                accumulated += duration;
            }
            else
            {
                // Crop the last record to only reach target
                var remaining = targetMinutes - accumulated;
                if (remaining > 0)
                {
                    var cropped = TimeRecord.Set(r.StartTime, r.StartTime.AddMinutes(remaining), r.Tag);
                    collected.Add(cropped);
                    accumulated += remaining;
                }
                break;
            }
        }

        return new TimeRange(accumulated, collected);
    }
    public static TimeRange FlattenAfter(this TimeRangeCollection records, DateTime afterTime)
    {
        if (records == null || records.Count == 0)
            return TimeRange.Empty;

        var filtered = records
            .Where(r => r.EndTime > afterTime) // Only use records ending after the boundary
            .Select(r =>
            {
                if (r.StartTime >= afterTime)
                    return r;

                // If overlapping, crop to afterTime
                var croppedStart = afterTime < r.EndTime ? afterTime : r.StartTime;
                return new TimeRecord(croppedStart, r.EndTime, r.Tag);
            })
            .Where(r => r.IsValid()) // Remove invalid records
            .ToTimeRangeCollection();

        var total = filtered.TotalMinutes();
        return new TimeRange(total, filtered);
    }


}

internal static class AttendanceHelper
{
    public static bool IsWithinTolerance(DateTime a, DateTime b, double toleranceMinutes) => Math.Abs((a - b).TotalMinutes) <= toleranceMinutes;
    public static Attendance? GetPunchNear(DateTime expectedTime, List<Attendance> records, double toleranceMinutes = 5) =>
        records.FirstOrDefault(r => IsWithinTolerance(r.WorkDateTime, expectedTime, toleranceMinutes));

    public static TResult? Let<TSource, TResult>(this TSource? source, Func<TSource, TResult> selector) where TSource : class =>
        source == null ? default : selector(source);
}