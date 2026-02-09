namespace DTR.Core;

public class AttendanceProvider
{
    private readonly GetCurrentAttendancePayload _payload;
    private readonly ICurrentShiftProvider _currentShiftProvider;
    public AttendanceProvider(GetCurrentAttendancePayload payload, ICurrentShiftProvider CurrentShiftProvider)
    {
        _payload = payload;
        _currentShiftProvider = CurrentShiftProvider;
    }

    //public List<Attendance> GetAttForDate(DateOnly date)
    //{
    //    var currentShift = _currentShiftProvider.GetShiftByDate(date);
    //    var nextShift = _currentShiftProvider.GetTrimmedNextShift();
    //    return GetAttByShift(currentShift, nextShift);
    //}

    public List<Attendance> CurrentShiftAttendance()
    {
        var currentShift = _currentShiftProvider.GetCurrentShift();
        var nextShift = _currentShiftProvider.GetNextShift(currentShift);
        return GetAttByShift(currentShift, nextShift);
    }

    private List<Attendance> GetAttByShift(CurrentShift? currentShift, CurrentShift? nextShift)
    {
        if (currentShift == null || nextShift == null)
            return new List<Attendance>();

        var key = new AttendanceEmpId(_payload.Employee.Id);
        if (!_payload.AllEmployeesAttendances.TryGetValue(key, out var attendances) || attendances == null)
            return new List<Attendance>();

        //hours before shift
        var allowance = currentShift.ShiftType == TimeShiftType.FLEXI ? 0 : TimeAllowance.TimeInAllowance;

        return attendances
            .Where(x =>
                x.WorkDateTime >= currentShift.StartTime.AddMinutes(allowance) &&
                x.WorkDateTime < nextShift.StartTime.AddMinutes(allowance))
            .ToList();
    }
}
