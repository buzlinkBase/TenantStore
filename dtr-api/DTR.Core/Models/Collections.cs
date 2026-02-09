using System.Collections.ObjectModel;
namespace DTR.Core;
//public class EmployeeCollection : Collection<Employee> {
//    public EmployeeCollection(List<Employee> employees) : base(employees) { }
//    public EmployeeCollection() { }
//}
public class ObjectCollection<T> : Collection<T> { }
public class DailyRecordCollection : ObjectCollection<DailyRecord> { }
public class IncompleteLogCollection  : Collection<ColumnarLogModel> { }
//public class AttendanceCollection : Collection<Attendance> {
//    public AttendanceCollection(List<Attendance> attendances) : base(attendances) { }
//    public AttendanceCollection() { }
//}
//public class TimeShiftCollection : Collection<CurrentShift> {
//    public TimeShiftCollection(List<CurrentShift> CurrentShifts) : base(CurrentShifts) { }
//    public TimeShiftCollection() { }
//}
public class TimeBreakCollection : Collection<TimeRange> {
    public TimeBreakCollection(List<TimeRange> timeRanges) : base(timeRanges) { }
    public TimeBreakCollection() { }
}
//public class LeaveCollection : Collection<LeaveEntity>
//{
//    public LeaveCollection(List<LeaveEntity> models) : base(models) { }

//    public LeaveCollection() { }
//}
//public class LeaveApplicationCollection : Collection<LeaveApplication>
//{
//    public LeaveApplicationCollection(List<LeaveApplication> models) : base(models) { }
//    public LeaveApplicationCollection() { }
//}
//public class HolidayCollection : Collection<HolidayInfo> {
//    public HolidayCollection()
//    {
//    }
//    public HolidayCollection(IEnumerable<HolidayInfo> models):base(models.ToList()) { }

//    public void AddRange(HolidayCollection holidays)
//    {
//        if (!holidays.Any()) return; 
//        foreach (var holiday in holidays)
//        {
//            Add(holiday);
//        }
//    }

//}

//public class CurrentDayoffCollection : Collection<CurrentDayoff>
//{
//public CurrentDayoffCollection(List<CurrentDayoff> models) : base(models) { }
//public CurrentDayoffCollection()
//{
//}
//public void AddRange(IEnumerable<CurrentDayoff> items)
//{
//    foreach (var item in items)
//    {
//         Add(item);
//    }
//}
//}
