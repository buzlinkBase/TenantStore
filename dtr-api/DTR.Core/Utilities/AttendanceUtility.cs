namespace DTR.Core;

public class AttendanceUtility
{
    private readonly Dictionary<AttendanceEmpId, List<Attendance>> _rawAttendance;
    public AttendanceUtility(Dictionary<AttendanceEmpId, List<Attendance>> rawAttendance)
    {
        _rawAttendance = rawAttendance;
    }

    public Dictionary<AttendanceEmpId, List<Attendance>> RemoveDoublePunch(double minDifferenceMinutes)
    {
        return new DupplicatePunchRemover(_rawAttendance)
            .Process(minDifferenceMinutes);
    }
}

public class DupplicatePunchRemover
{
    private readonly Dictionary<AttendanceEmpId, List<Attendance>> _rawAttendance;
    public DupplicatePunchRemover(Dictionary<AttendanceEmpId, List<Attendance>> rawAttendance)
    {
        _rawAttendance = rawAttendance;
    }

    public Dictionary<AttendanceEmpId, List<Attendance>> Process(double minDifferenceMinutes)
    {
        if (_rawAttendance == null || !_rawAttendance.Any())
            return new Dictionary<AttendanceEmpId, List<Attendance>>();

        var result = new Dictionary<AttendanceEmpId, List<Attendance>>();

        foreach (var (employeeId, attendances) in _rawAttendance)
        {
            var sorted = attendances
                .OrderBy(a => a.WorkDateTime)
                .ToList();

            var filteredAttendances = new List<Attendance>();
            DateTime? previousTime = null;

            foreach (var record in sorted)
            {
                if (previousTime == null || (record.WorkDateTime - previousTime.Value).TotalMinutes >= minDifferenceMinutes)
                {
                    filteredAttendances.Add(record);
                    previousTime = record.WorkDateTime;
                }
            }
            result[employeeId] = filteredAttendances;
        }

        return result;
    }
}
