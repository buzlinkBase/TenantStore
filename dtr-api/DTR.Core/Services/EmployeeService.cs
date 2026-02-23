
using System.Data.Common;
using System.Diagnostics;
using System.Linq.Expressions;

namespace DTR.Core;

public class EmployeeService : ServiceBase<Employee>
{
    private TimeShift? _timeShift;
    private readonly AttendanceService _attendanceService;
    private readonly UnknownEmployeeService _unknownEmployeeService;
    private readonly ITenantProvider _tenantProvider;
    private int remainingEmployeeCount = 100_000;
    public event Action<string> OnMessage;

    public EmployeeService(IDTRUnitOfWork uow,
        AttendanceService attendanceService,
        UnknownEmployeeService unknownEmployeeService,
        ITenantProvider tenantProvider) : base(uow)
    {
        _attendanceService = attendanceService;
        _unknownEmployeeService = unknownEmployeeService;
        _tenantProvider = tenantProvider;
    }
    public async Task AddOrUpdateAsync(Employee model, List<RestDay> restDay)
    {
        try
        {
            if (remainingEmployeeCount == 0) return;
            model.TenantId = _tenantProvider.TenantId;

            var existing = await _uow.Repository
                .Find<Employee>(x => x.Id == model.Id)
                .AsNoTracking()
                .FirstOrDefaultAsync();

            var validationResult = AddOrUpdateValidation(model);
            if (!validationResult.Success)
            {
                throw new Exception(validationResult.Message);
            }
            if (existing == null)
            {
                _uow.Repository.Add(model);
            }
            else
            {
                _uow.Repository.Update(model);
            }
            //_uow.Repository.AddOrUpdate(model);
            _uow.SaveChanges();

            // 1. Delete all existing RestDay records for the employee
            await _uow.Repository
                .Find<RestDay>(x => x.EmployeeId == model.Id)
                .ExecuteDeleteAsync();

            // 2. Assign EmployeeId to each new RestDay entry
            foreach (var restday in restDay)
            {
                restday.Id = Guid.Empty; // force new insert
                restday.EmployeeId = model.Id;
                restday.TenantId = model.TenantId;
            }

            // 3. Add the new list
            _uow.Repository.AddRange(restDay);
            _uow.SaveChanges();
            ProcessUnknowEmp(model);
        }
        catch (DbException ex)
        {
            Debug.WriteLine($"[DB ERROR] {ex}");
            // Consider logging to audit trail here
        }
    }

    public void SetEmployeeLimit(int maxLimit, bool ExludeDeleted)
    {

        var query = FindAll()
            .Where(x => x.TenantId == _tenantProvider.TenantId);

        if (ExludeDeleted)//to conditionaly exclude deleted employee
        {
            query.IgnoreQueryFilters();
        }
        var totalCount = query.Count();
        remainingEmployeeCount = Math.Max(maxLimit - totalCount, 0);
        if (remainingEmployeeCount <= 0)
        {
            OnMessage?.Invoke("maximum employee count is reached");
        }
    }

    public override void AddRange(List<Employee> models)
    {
        if (models == null || models.Count == 0) return;
        var empBios = FindAll().Select(x => x.BioId).ToList();
        var existingBioIds = new HashSet<int>(empBios);
        var newEmployees = models
            .Where(x => !existingBioIds.Contains(x.BioId))
            .Skip(0)
            .Take(remainingEmployeeCount)
            .ToList();

        foreach (var emp in newEmployees)
        {
            if (emp.Id == Guid.Empty)
            {
                emp.DateRegistered = DateTime.Now;
            }
        }
        if (newEmployees.Count > 0)
        {
            base.AddRange(newEmployees);
        }
    }

    public Employee? FindBio(int bioId)
    {
        return FindAll().FirstOrDefault(x => x.BioId == bioId);
    }

    private void ProcessUnknowEmp(Employee model)
    {
        var uemp = _unknownEmployeeService.FindAll()
             .Where(x => x.BioId == model.BioId)
             .ToList();
        var first = uemp.FirstOrDefault();
        var username = _userContext.CurrentUser.FullName ?? string.Empty;
        if (first != null)
        {
            username = _userContext.CurrentUser.FullName;
        }

        List<Attendance> attendances = new List<Attendance>();
        foreach (var item in uemp)
        {
            var att = new Attendance
            {
                BatchCode = item.BatchCode,
                BioId = item.BioId,
                WorkDateTime = item.WorkDateTime,
                Workstate = item.Workstate,
                DeviceName = item.DeviceName,
                LogSource = item.LogSource,
                LogRemarks = item.LogRemarks,
                IP = item.IP,
                EditRemarks = item.EditRemarks,
                Verifycode = item.Verifycode,
                ClientId = model.ClientId,
                BranchId = model.BranchId,
                DepartmentId = model.DepartmentId,
                EmployeeId = model.Id,
                TenantId = model.TenantId,
                UserId = item.UserId,
                UserName = username
            };
            attendances.Add(att);
        }
        _attendanceService.AddRange(attendances);
        _unknownEmployeeService.RemoveRange(uemp);
    }

    public List<EmployeeRecord> FindByGroup(PayrollGroup? payrollGroup, Employee? employee,
        Client? client,
        Department? department)
    {
        return FindAll()
            .Where(x => (employee == null || x.Id == employee.Id)
                 && (payrollGroup == null || x.PayrollGroupId == null || x.PayrollGroupId == payrollGroup.Id)
                 && (client == null || x.ClientId == null || x.ClientId == client.Id)
                 && (department == null || x.DepartmentId == null || x.DepartmentId == department.Id))
            .Select(x => new EmployeeRecord
            {
                Id = x.Id,
                FullName = x.FullName, 
                Selection = false,
            })
            .AsEnumerable()
            .OrderBy(x => x.FullName)
            .ToList()
            ;
    }

    protected override ValidationMessage AddOrUpdateValidation(Employee model)
    {
        var existing = _uow.Context
            .Employees
            .AsNoTracking()
            .FirstOrDefault(x => x.Id == model.Id);

        if (existing == null)//create registered date
        {
            DateTime dte = _uow.Repository.GetServerDate();
            model.DateRegistered = dte;
        }
        //find dupplicate bioId

        if (model != null && model.DateRegistered == DateTime.MinValue)
        {
            DateTime dte = _uow.Repository.GetServerDate();
            model.DateRegistered = dte;
        }

        var bioExist = _uow.Repository
            .Find<Employee>(x => x.BioId == model.BioId && x.Id != model.Id)
            .FirstOrDefault();

        if (bioExist != null)
        {
            return new ValidationMessage(false, $"Biometric Id is already used by another Employee [{bioExist.FullName}]");
        }

        var shiftId = model.TimeShiftId.HasValue && model.TimeShiftId != Guid.Empty ? model.TimeShiftId.Value : Guid.Empty;
        _timeShift = _uow.Repository.FindOne<TimeShift>(shiftId);
        model.TimeShiftId = _timeShift?.Id ?? null;
        //if (_timeShift == null)
        //{
        //    return new ValidationMessage(false, "Invalid Time Shift");
        //}
        var nameExists = _uow.Repository
            .Find<Employee>(x => x.FirstName == model.FirstName
                && x.MiddleName == model.MiddleName
                && x.LastName == model.LastName
                && x.Suffix == model.Suffix
                && x.Id != model.Id)
            ;

        if (nameExists.Any())
        {
            return new ValidationMessage(false, "employee is already added");
        }
        else if (string.IsNullOrWhiteSpace(model.FullName))
        {
            return new ValidationMessage(false, "Invalid employee name");
        }
        return base.AddOrUpdateValidation(model);
    }

    public Employee? FindOneForEdit(Guid Id)
    {
        return _uow.Repository.Find(new FindOneWithOff())
            //.AsNoTracking()
            .FirstOrDefault(x => x.Id == Id);
    }

    public long FindNextBioId()
    {
        DateTime dte = _uow.Repository.GetServerDate();

        // Ensure safe handling of null values by using `DefaultIfEmpty`
        var maxBioId = _uow.Repository
            .FindAll<Employee>()
            .IgnoreQueryFilters()
            .Where(x => x.TenantId == _tenantProvider.TenantId && x.DateRegistered.Year == dte.Year && x.DateRegistered.Month == dte.Month)
            .Select(x => x.BioId)
            .Count()
            ;

        //var sql = $"SELECT COALESCE(Count(BioId), 0) AS MaxBioId " +
        //    $"FROM Employees" +
        //    $" WHERE YEAR(DateRegistered) = {dte.Year} AND MONTH(DateRegistered) = {dte.Month}";

        //long? maxBioId = _uow.Repository.ExecuteRawSqlScalar<long>(sql);
        maxBioId += 1;
        string paddedId = maxBioId.ToString().PadLeft(3, '0');
        var formatted = $"{dte:yyMM}{paddedId}";

        return long.Parse(formatted);
    }
}
//public class EmployeeRecord
//{
//    public Guid Id { get; set; }
//    public bool Selection { get; set; }
//    public string FullName { get; set; } = string.Empty;
//    public int BioId { get; set; }
//    public Guid? DepartmentId { get; set; }
//    public Guid? ClientId { get; set; }
//    public Guid? BranchId { get; set; }
//}
public class FindOneWithOff : Specification<Employee>
{
    public Func<IQueryable<Employee>, IQueryable<Employee>> Include => x => x.Include(x => x.RestDays);
    public override Expression<Func<Employee, bool>> Criteria => x => x.Status == "Active";

}
