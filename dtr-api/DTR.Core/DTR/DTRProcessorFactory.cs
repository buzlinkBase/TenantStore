namespace DTR.Core;

public interface IDTRProcessor<T>
{
    T? Process(DTRProcessorPayload payload);
}

public enum ProcessorType
{
    DTRDetail,
    Form48,
    Incomplete,
    CleanColumnarLog,
    RawColumnarLog,
    RawRowLog,
    AttLogs,
}

public class DTRProcessorFactory
{
    public static IDTRProcessor<T> Create<T>(ProcessorType type)
    {
        switch (type)
        {
            case ProcessorType.DTRDetail:
                return (IDTRProcessor<T>)new CleanDTRDetailProcessor();
            case ProcessorType.Form48:
                return (IDTRProcessor<T>)new CleanForm48Processor();
            case ProcessorType.Incomplete:
                return (IDTRProcessor<T>)new CleanIncompleteLogsProcessor();
            case ProcessorType.RawColumnarLog:
                return (IDTRProcessor<T>)new RawColumnarLogProcessor();
            case ProcessorType.CleanColumnarLog:
                return (IDTRProcessor<T>)new CleanColumnarLogProcessor();
            case ProcessorType.RawRowLog:
                return (IDTRProcessor<T>)new CleanRowLogProcessor();
            case ProcessorType.AttLogs:
                return (IDTRProcessor<T>)new CleanAttendanceProcessor();
            default:
                throw new NotImplementedException("DTRProcessorFactory");
        }
    }
}