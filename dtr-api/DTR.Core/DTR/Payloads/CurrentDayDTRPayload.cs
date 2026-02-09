

namespace DTR.Core;

public class CurrentDayDTRPayload
{
    public static DTRProcessorPayload SetPayload(
    DTRContextModel context,
    Employee curEmployee,
    DateOnly currentDate,
    bool processOnlyPairedAtt = true)
    {
        var shiftProvider = CreateShiftProvider(context, curEmployee, currentDate);
        var currentShift = shiftProvider.GetCurrentShift();

        //capture attendance
        var attendanceProvider = CreateAttendanceProvider(context, curEmployee, shiftProvider);
        var currentAtt = attendanceProvider.CurrentShiftAttendance();
        var attendance = processOnlyPairedAtt
            ? AttendancePairGrabber.GetPairs(currentAtt)
            : currentAtt;

        var leaveProvider = new LeaveApplicationServiceProvider(context.Leaves, curEmployee);
        var currentLeave = leaveProvider.GetLeave(currentDate);

        var payload = new DTRProcessorPayloadBuilder()
            .SetEmployee(curEmployee)
            .SetCurrentDate(currentDate)
            .SetCurrentShift(currentShift)
            .SetCurrentShiftProvider(shiftProvider)
            .SetAttendanceProvider(attendanceProvider)
            .SetCurrentAtt(attendance)
            .SetLeaveProvider(leaveProvider)
            .SetCurrentLeave(currentLeave)
            .SetOTProvider(new OverTimeServiceProvider(context.OverTimeApplications, curEmployee))
            .SetUTProvider(new UnderTimeServiceProvider(context.UnderTimeApplications, curEmployee))
            .SetHolidayProvider(HolidayProviderFactory.Create(curEmployee, context.Holidays))
            .SetClientPolicyProvider(new ClientPolicyProvider(context.ClientPolicies))
            .SetCompanyPolicy(context.CompanyPolicy)
            .SetEmployeePolicy(context.EmployeePolicies.TryGetValue(new EmployeePolicyKey(curEmployee.Id), out var policy) ? policy : null)
            .SetDayOff(context.DayOffs)
            .SetDTRContext(context)
            .Build();
        payload.Provider.DtrContextModel = context;
        return payload;

    }
    public static ICurrentShiftProvider CreateShiftProvider(DTRContextModel context, Employee employee, DateOnly curDate)
    {
        return ShiftProviderFactory.Create( context,employee,curDate);
    }

    private static AttendanceProvider CreateAttendanceProvider(
        DTRContextModel context,
        Employee employee,
        ICurrentShiftProvider shiftProvider)
    {
        var payload = new GetCurrentAttendancePayload(context.CleanAttendance, employee);
        return new AttendanceProvider(payload, shiftProvider);
    }
}

public class ShiftProviderFactory
{
    public static ICurrentShiftProvider Create(DTRContextModel context, Employee employee, DateOnly curDate)
    {
        //read settings config to use here
        var payload = new CurrentShiftProviderPayload(context.AllShifts, employee, curDate);
        return new CurrentShiftProvider(context, payload);
        //return new CrossMultiDateCurrentShiftProvider(context, payload);
    }
}