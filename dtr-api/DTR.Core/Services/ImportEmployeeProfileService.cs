//using Ganss.Excel;
//namespace DTR.Core;

//public class ImportEmployeeProfileService
//{
//    private readonly IDTRUnitOfWork _uow;
//    private readonly EmployeeService _employeeService;
//    private readonly TimeShiftService _timeShiftService;
//    public event Action<string> OnMessage;
//    public ImportEmployeeProfileService(IDTRUnitOfWork uow,
//        EmployeeService employeeService,
//        TimeShiftService timeShiftService)
//    {
//        _uow = uow;
//        _employeeService = employeeService;
//        _timeShiftService = timeShiftService;
//    }

//    private void SetDefaults(List<EmployeeImportModel> data)
//    {
//        foreach (var item in data)
//        {
//            if (string.IsNullOrWhiteSpace(item.ShiftName))
//            {
//                item.ShiftName = "Day Shift";
//            }
//            if (string.IsNullOrWhiteSpace(item.ShiftType))
//            {
//                item.ShiftType = "Fix";
//            }
//            if (string.IsNullOrWhiteSpace(item.AMIn))
//            {
//                item.AMIn = "8:00:00";
//                item.PMOut = "17:00:00";
//            }
//            if (string.IsNullOrWhiteSpace(item.DepartmentName))
//            {
//                item.DepartmentName = "--";
//            }
//            if (string.IsNullOrWhiteSpace(item.PayrollGroup))
//            {
//                item.PayrollGroup = "--";
//            }
//            if (string.IsNullOrWhiteSpace(item.ClientName))
//            {
//                item.ClientName = "--";
//            }
//            if (string.IsNullOrWhiteSpace(item.RestDay1))
//            {
//                item.RestDay1 = "";
//            }
//            if (string.IsNullOrWhiteSpace(item.RestDay2))
//            {
//                item.RestDay2 = "";
//            }
//        }

//    }

//    private void MapFields(ExcelMapper mapper)
//    {
//        mapper.AddMapping<EmployeeImportModel>("BioId", p => p.BioId);
//        mapper.AddMapping<EmployeeImportModel>("LastName", p => p.LastName);
//        mapper.AddMapping<EmployeeImportModel>("FirstName", p => p.FirstName);
//        mapper.AddMapping<EmployeeImportModel>("MiddleName", p => p.MiddleName);
//        mapper.AddMapping<EmployeeImportModel>("Suffix", p => p.Suffix);
//        mapper.AddMapping<EmployeeImportModel>("Gender", p => p.Gender);
//        mapper.AddMapping<EmployeeImportModel>("Rest Day 1", p => p.RestDay1);
//        mapper.AddMapping<EmployeeImportModel>("Rest Day 2", p => p.RestDay2);
//        mapper.AddMapping<EmployeeImportModel>("DepartmentName", p => p.DepartmentName);
//        mapper.AddMapping<EmployeeImportModel>("ClientName", p => p.ClientName);
//        mapper.AddMapping<EmployeeImportModel>("Payroll Group", p => p.PayrollGroup);
//        mapper.AddMapping<EmployeeImportModel>("Shift Name", p => p.ShiftName);
//        mapper.AddMapping<EmployeeImportModel>("Type", p => p.ShiftType);
//        //mapper.AddMapping<EmployeeImportModel>("CrossDate", p => p.CrossDate);
//        mapper.AddMapping<EmployeeImportModel>("AMIN", p => p.AMIn);
//        mapper.AddMapping<EmployeeImportModel>("Noon BreakOut", p => p.NoonBreakOut);
//        mapper.AddMapping<EmployeeImportModel>("Break-IN", p => p.NoonBreakIn);
//        mapper.AddMapping<EmployeeImportModel>("PM OUT", p => p.PMOut);

//    }
//    public void Upload(string path,int maxEmpCount,bool excludeDeleted)
//    {
//        var mapper = new ExcelMapper(path)
//        {
//            HeaderRowNumber = 0,
//            MinRowNumber = 1,
//        };
//        MapFields(mapper);
//       var data = mapper.Fetch<EmployeeImportModel>().ToList();

//        SetDefaults(data);
//        var shifts = ExtractShifts(data);
//        var clients = ExtractClients(data);
//        var pyGroups = ExtractPayrollGroups(data);
//        var departments = ExtractDepartments(data);
//        var restDays = ExtractRestDay(data);

//        StoreShift(shifts);
//        StoreClients(clients);
//        StorePayrollGroups(pyGroups);
//        StoreDepartments(departments);

//        List<Employee> employees = new List<Employee>();
//        foreach (var item in data)
//        {
//            if (string.IsNullOrWhiteSpace(item.RestDay1)) item.RestDay1 = "";
//            if (string.IsNullOrWhiteSpace(item.RestDay2)) item.RestDay2 = "";
//        }
//        var rests = new List<RestDay>();
//        var progressPercent = data.Count / 100 - 1;

//        foreach (var item in data)
//        {
//            var startTime = GetStartTime(item);
//            var endTime = GetEndTime(item);
//            var lunchOut = GetLunchOut(item);
//            var lunchIn = GetLunchIn(item);
//            var shiftKey = new ShiftKey(item.ShiftName, startTime, endTime, lunchOut, lunchIn);
//            shifts.TryGetValue(shiftKey, out TimeShift timeShift);
//            clients.TryGetValue(item.ClientName, out Client client);
//            pyGroups.TryGetValue(item.PayrollGroup, out PayrollGroup pg);
//            departments.TryGetValue(item.DepartmentName, out Department? department);
//            //int.TryParse(item.BioId, out int bioId);
//            int bioId = item.BioId;

//            var employee = new Employee()
//            {
//                FirstName = item?.FirstName ?? "",
//                LastName = item?.LastName ?? "",
//                MiddleName = item?.MiddleName ?? "",
//                Suffix = item?.Suffix ?? "",
//                Gender = item?.Gender ?? "Male",
//                TimeShiftId = timeShift?.Id ?? Guid.Empty,
//                ClientId = client?.Id ?? Guid.Empty,
//                PayrollGroupId = pg?.Id ?? Guid.Empty,
//                DepartmentId = department?.Id ?? Guid.Empty,
//                BioId = bioId,
//            };

//            employee.RestDays.Clear();

//            if (!string.IsNullOrWhiteSpace(item.RestDay1) && restDays.TryGetValue(item.RestDay1, out var r1))
//            {
//                employee.RestDays.Add(new RestDay
//                {
//                    DayName = r1.DayName,
//                    EmployeeId = employee.Id
//                });
//            }

//            if (!string.IsNullOrWhiteSpace(item.RestDay2) && restDays.TryGetValue(item.RestDay2, out var r2))
//            {
//                employee.RestDays.Add(new RestDay
//                {
//                    DayName = r2.DayName,
//                    EmployeeId = employee.Id
//                });
//            }

//            employees.Add(employee);
//            progressPercent += 1;
//        }

//        var Service = _employeeService;
//        Service.SetEmployeeLimit(maxEmpCount, excludeDeleted);
//        Service.AddRange(employees);
//        Service.OnMessage += Service_OnMessage;
//    }

//    private void Service_OnMessage(string obj)
//    {
//        OnMessage?.Invoke(obj);
//    }

//    private List<Employee> CleanUp(List<Employee> employees)
//    {
//        if (employees == null) return [];
//        return employees
//            .GroupBy(x => x.BioId)
//            .Select(x => x.First())
//            .ToList();
//    }

//    public void StoreShift(Dictionary<ShiftKey, TimeShift> shifts)
//    {
//        var models = shifts.Values.ToList();
//        if (models == null || models.Count == 0) return;

//        var existing = _timeShiftService.FindAll()
//         .GroupBy(x => x.ShiftName)
//         .ToDictionary(x => x.Key.ToLowerInvariant(), x => x.First().Id);

//        var newRecords = models
//            .Where(x => !existing.ContainsKey(x.ShiftName.Trim().ToLowerInvariant()))
//            .ToList();

//        foreach (var item in models)
//        {
//            if (existing.TryGetValue(item.ShiftName.Trim().ToLowerInvariant(), out Guid curId))
//            {
//                item.Id = curId;
//            }
//        }
//        if (newRecords.Count > 0)
//        {
//            _timeShiftService.AddRange(newRecords);
//        }
//    }

//    public void StoreClients(Dictionary<string, Client> clients)
//    {
//        var codeCount = 1;
//        foreach (var item in clients.Values)
//        {
//            item.Code = codeCount.FormatCode();
//            codeCount += 1;
//        }
//        var models = clients.Values.ToList();
//        if (models == null || models.Count == 0) return;

//        var Service = new ClientService(_uow);
//        var existing = Service.FindAll()
//         .GroupBy(x => x.Name)
//         .ToDictionary(x => x.Key.ToLowerInvariant(), x => x.First().Id);

//        var newRecords = models
//            .Where(x => !existing.ContainsKey(x.Name.Trim().ToLowerInvariant()))
//            .ToList();

//        foreach (var item in models)
//        {
//            if (existing.TryGetValue(item.Name.Trim().ToLowerInvariant(), out Guid curId))
//            {
//                item.Id = curId;
//            }
//        }
//        if (newRecords.Count > 0)
//        {
//            Service.AddRange(newRecords);
//        }
//    }
//    public void StorePayrollGroups(Dictionary<string, PayrollGroup> pr)
//    {
//        var codeCount = 1;
//        foreach (var item in pr.Values)
//        {
//            item.Code = codeCount.FormatCode();
//            codeCount += 1;
//        }

//        var models = pr.Values.ToList();
//        if (models == null || models.Count == 0) return;

//        var Service = new PayrollGroupService(_uow);
//        var existing = Service.FindAll()
//         .GroupBy(x => x.Name)
//         .ToDictionary(x => x.Key.ToLowerInvariant(), x => x.First().Id);

      
//        var newRecords = models
//            .Where(x => !existing.ContainsKey(x.Name.Trim().ToLowerInvariant()))
//            .ToList();

//        foreach (var item in models)
//        {
//            if (existing.TryGetValue(item.Name.Trim().ToLowerInvariant(), out Guid curId))
//            {
//                item.Id = curId;
//            }
//        }
//        if (newRecords.Count > 0)
//        {
//            Service.AddRange(newRecords);
//        }

//    }
//    public void StoreDepartments(Dictionary<string, Department> depts)
//    {
//        var models = depts.Values.ToList();
//        if (models == null || models.Count == 0) return;

//        var codeCount = 1;
//        foreach (var item in depts.Values)
//        {
//            item.Code = codeCount.FormatCode();
//            codeCount += 1;
//        }
//        var Service = new DepartmentService(_uow);
//        var existing = Service.FindAll()
//         .GroupBy(x => x.Name)
//         .ToDictionary(x => x.Key.ToLowerInvariant(), x => x.First().Id);

//        var newRecords = models
//            .Where(x => !existing.ContainsKey(x.Name.Trim().ToLowerInvariant()))
//            .ToList();

//        foreach (var item in models)
//        {
//            if (existing.TryGetValue(item.Name.Trim().ToLowerInvariant(), out Guid curId))
//            {
//                item.Id = curId;
//            }
//        }
//        if (newRecords.Count > 0)
//        {
//            Service.AddRange(newRecords);
//        }
//    }
//    public Dictionary<ShiftKey, TimeShift> ExtractShifts(List<EmployeeImportModel> data)
//    {
//        return data
//            .GroupBy(x => new { x.ShiftName, x.AMIn, x.PMOut, x.NoonBreakOut, x.NoonBreakIn })
//            .Select(group =>
//            {
//                var x = group.First();
//                var startTime = GetStartTime(x);
//                var endTime = GetEndTime(x);
//                var lunchOut = GetLunchOut(x);
//                var lunchIn = GetLunchIn(x);

//                var shiftType = (x.ShiftType?.Contains("Fix") == true || x.ShiftType?.Contains("Fixed") == true) ? TimeShiftType.FIXED : TimeShiftType.FLEXI;
//                var hasBreak = lunchOut != TimeSpan.Zero && lunchIn != TimeSpan.Zero;

//                var maxWorkingMinutes = endTime.Subtract(startTime).TotalMinutes;
//                var breakDuration = hasBreak
//                    ? Math.Max(0, lunchIn.Subtract(lunchOut).TotalMinutes)
//                    : maxWorkingMinutes <= 480 || shiftType==TimeShiftType.FLEXI ? 0 :  60;

//                return new
//                {
//                    Key = new ShiftKey(x.ShiftName, startTime, endTime, lunchOut, lunchIn),
//                    Value = new TimeShift
//                    {
//                        ShiftType = string.IsNullOrEmpty(x.ShiftType) ? TimeShiftType.FIXED : shiftType,
//                        ShiftName = $"{x.ShiftName}-{x.AMIn}-{x.PMOut}" + (hasBreak ? "-WB" : ""),
//                        StartTime = startTime,
//                        LunchStartTime = lunchOut,
//                        LunchEndTime = lunchIn,
//                        EndTime = endTime,
//                        //PunchMode= hasBreak ? PunchMode.FOUR_PUNCHES : PunchMode.TWO_PUNCHES,
//                        WithLunchBreak = breakDuration==0? BreakMode.PAID_BREAK :  BreakMode.UNPAID_BREAK,
//                        MaxWorkingMinutes = 480,
//                        BreakDurationMinutes = breakDuration
//                    }
//                };
//            })
//            .GroupBy(x => x.Key)
//            .Select(g => g.First())
//            .ToDictionary(x => x.Key, x => x.Value);
//    }

//    //UTILITY
//    private TimeSpan GetStartTime(EmployeeImportModel x)
//    {
//        return TimeSpan.TryParse(x.AMIn, out var amIn) ? amIn : new TimeSpan(8, 0, 0);
//    }

//    private TimeSpan GetEndTime(EmployeeImportModel x)
//    {
//        return TimeSpan.TryParse(x.PMOut, out var pmOut) ? pmOut : new TimeSpan(17, 0, 0);
//    }
//    //private TimeSpan GetEndTime(EmployeeImportModel x)
//    //{
//    //    TimeSpan.TryParse(x.PMOut, out var pmOut);
//    //    return string.IsNullOrEmpty(x.ShiftType) ? new TimeSpan(17, 0, 0) : pmOut;
//    //}
//    private TimeSpan GetLunchOut(EmployeeImportModel x)
//    {
//        TimeSpan.TryParse(x.NoonBreakOut, out var lunchOut);
//        return lunchOut;
//    }
//    private TimeSpan GetLunchIn(EmployeeImportModel x)
//    {
//        TimeSpan.TryParse(x.NoonBreakIn, out var lunchOut);
//        return lunchOut;
//    }
//    public Dictionary<string, Client> ExtractClients(List<EmployeeImportModel> data)
//    {
//        return data
//            .GroupBy(x => x.ClientName)
//            .Select(g => new Client { Name = string.IsNullOrWhiteSpace(g.Key) ? "--" : g.Key })
//            .ToDictionary(x => x.Name, x => x);
//        ;
//    }
//    public Dictionary<string, PayrollGroup> ExtractPayrollGroups(List<EmployeeImportModel> data)
//    {
//        return data
//            .GroupBy(x => x.PayrollGroup)
//            .Select(g => new PayrollGroup { Name = string.IsNullOrWhiteSpace(g.Key) ? "--" : g.Key })
//            .ToDictionary(x => x.Name, x => x);
//        ;
//    }
//    public Dictionary<string, Department> ExtractDepartments(List<EmployeeImportModel> data)
//    {
//        return data
//            .GroupBy(x => x.DepartmentName)
//            .Select(g => new Department { Name = string.IsNullOrWhiteSpace(g.Key) ? "--" : g.Key })
//            .ToDictionary(x => x.Name, x => x);
//        ;
//    }
//    public Dictionary<string, RestDay> ExtractRestDay(List<EmployeeImportModel> data)
//    {
//        var r1 = data
//            .Where(x => !string.IsNullOrWhiteSpace(x.RestDay1))
//            .Select(x => x.RestDay1)
//            .ToList();

//        var r2 = data
//            .Where(x => !string.IsNullOrWhiteSpace(x.RestDay2))
//            .Select(x => x.RestDay2)
//            .ToList();

//        // Combine all rest days into r3
//        var r3 = new List<string>();
//        r3.AddRange(r1);
//        r3.AddRange(r2);

//        var restDays = r3
//            .Distinct()
//            .ToDictionary(
//                day => day,
//                day =>
//                {
//                    DayName result = Enum.TryParse(day, true, out DayName parsedDay)
//                        ? parsedDay
//                        : DayName.Saturday;

//                    return new RestDay
//                    {
//                        DayName = result
//                    };
//                });

//        return restDays;
//    }
//}
//public record struct ShiftKey(string ShiftName, TimeSpan am, TimeSpan pm, TimeSpan? l1, TimeSpan? l2);
