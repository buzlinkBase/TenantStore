using Grpc.Core;

namespace DTR.Core;

public class DTRCalcService
{
    private CancellationToken _token;
    private readonly DataServiceResolver _dataServiceProvider;
    private readonly IDTRUnitOfWork _uow;

    public DTRCalcService(DataServiceResolver dataServiceProvider,
        IDTRUnitOfWork uow)
    {
        _dataServiceProvider = dataServiceProvider;
        _uow = uow;
    }
    public async Task<ObjectCollection<T>> GetDTRInfo<T>(
        DTRRequestPayload payload,
        ProcessorType processorType,
        CancellationToken token,
        IncludeNullResponse ignoreNull = IncludeNullResponse.Include,
        bool processOnlyPairedAtt = true,
        bool removedoublePunch = true) where T : class, new()
    {
        _token = token;
        var processor = DTRProcessorFactory.Create<T>(processorType);
        var canProcess = true;
        var context = await CurrentRangeDTRPayload.SetPayload(canProcess, payload, _dataServiceProvider, _uow, removedoublePunch);
        context.DtrService = this;
        var calculator = new DailyRecordCompute(context);
        return calculator.ProcessDailyRecords(processor, payload.FromDate, payload.ToDate, token, ignoreNull, processOnlyPairedAtt);
    }
    public CancellationToken Token => _token;
}
