
using DTR.Core.Interfaces;
namespace DTR.Core;

public class EmployeeService
{

    private readonly IEmployeeMessage _client;

    public EmployeeService(IEmployeeMessage client)
    {
        _client = client;
    }

    public async Task<Employee?> GetOne(Guid Id )
    {
        var response = await _client.GetOne(Id);
        return response.Data;
    }

    public async Task<List<Employee>> GetDtrEmployees(EmployeeRequestPayload payload)
    {
        var response = await _client.GetDtrEmployees(payload);
        return response.Data ?? new List<Employee>();
    }
}
