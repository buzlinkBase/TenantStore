
namespace DTR.Core;
public class TimeShiftService : ServiceBase<TimeShift>
{
    public TimeShiftService(IDTRUnitOfWork uow) : base(uow) { }
}