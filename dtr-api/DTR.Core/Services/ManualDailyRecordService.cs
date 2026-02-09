using Ganss.Excel;

namespace DTR.Core;

public class ManualDailyRecordService : DailyRecordService
{
    public ManualDailyRecordService(IDTRUnitOfWork uow,
        AttendanceService attendanceService , DataServiceResolver dataService) : base(uow, dataService)
    {
    }

    public void Upload(List<DailyRecord> models)
    {
        base.AddRange(models);
    }

    public List<DTRImportModel> DeserializedExcelFile(string fileName)
    {
        try
        {
            var mapper = new ExcelMapper(fileName)
            {
                HeaderRowNumber = 3,
                MinRowNumber = 4,
            }
            ;
            var exls = mapper.Fetch<DTRImportModel>();
            return exls.ToList();
        }
        catch (Exception e)
        {

            throw;
        }
    }

}