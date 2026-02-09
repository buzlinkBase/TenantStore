namespace DTR.Core;

public class CleanAttendanceProcessor : IDTRProcessor<List<Attendance>>
{
    public List<Attendance> Process(DTRProcessorPayload payload)
    {
        return payload.Data.CurrentAttendance;
    }
}
