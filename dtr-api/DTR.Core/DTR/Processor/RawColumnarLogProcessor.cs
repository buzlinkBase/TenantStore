namespace DTR.Core;

public class RawColumnarLogProcessor  :  IDTRProcessor<ColumnarLogModel>
{
    public ColumnarLogModel?  Process(DTRProcessorPayload payload)
    {
        var logs = payload.Data.CurrentAttendance;
        if (!logs.Any()) return null;
        var mapper = IncompleteLogMapperFactory.Create(payload);
        return  mapper.MapData(payload, logs, payload.Data.CurrentDate);
    }
}
